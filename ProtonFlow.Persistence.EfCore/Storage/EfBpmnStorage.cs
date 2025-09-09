using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProtonFlow.Persistence.EfCore.Storage.Models;

namespace ProtonFlow.Persistence.EfCore.Storage;

/// <summary>
/// EF Core implementation of <see cref="IBpmnStorage"/> providing durable persistence using a relational database (SQLite, SQL Server, etc.).
/// The implementation focuses on correctness and clarity for MVP while leaving room for batching, caching and advanced optimizations.
/// </summary>
public class EfBpmnStorage : IBpmnStorage
{
    private readonly ProtonFlowDbContext _db;

    /// <summary>Create a new storage instance for a given DbContext.</summary>
    public EfBpmnStorage(ProtonFlowDbContext db) => _db = db;

    #region Definitions
    /// <inheritdoc />
    public async Task<StoredProcessDefinition> SaveProcessDefinitionAsync(StoredProcessDefinition definition, CancellationToken ct = default)
    {
        // Ensure version auto increment for same key when not specified
        if (definition.Version == 0)
        {
            var latest = await _db.ProcessDefinitions.Where(d => d.Key == definition.Key)
                .OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);
            definition.Version = (latest?.Version ?? 0) + 1;
        }

        // Mark previous latest records
        var previous = await _db.ProcessDefinitions.Where(d => d.Key == definition.Key && d.IsLatest).ToListAsync(ct);
        foreach (var p in previous) p.IsLatest = false;

        definition.IsLatest = true;
        if (string.IsNullOrEmpty(definition.ContentHash))
        {
            definition.ContentHash = ComputeHash(definition.Xml);
        }

        _db.ProcessDefinitions.Add(definition);
        await _db.SaveChangesAsync(ct);
        return definition;
    }

    /// <inheritdoc />
    public Task<StoredProcessDefinition?> GetProcessDefinitionByIdAsync(string id, CancellationToken ct = default)
        => _db.ProcessDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);

    /// <inheritdoc />
    public Task<StoredProcessDefinition?> GetProcessDefinitionByKeyAsync(string key, int? version = null, CancellationToken ct = default)
    {
        var q = _db.ProcessDefinitions.Where(d => d.Key == key);
        if (version.HasValue)
        {
            q = q.Where(d => d.Version == version.Value);
        }
        else
        {
            q = q.Where(d => d.IsLatest);
        }
        return q.OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredProcessDefinition>> GetProcessDefinitionsAsync(string? key = null, CancellationToken ct = default)
    {
        var q = _db.ProcessDefinitions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(key)) q = q.Where(d => d.Key == key);
        return await q.OrderBy(d => d.Key).ThenByDescending(d => d.Version).ToListAsync(ct);
    }
    #endregion

    #region Instances
    /// <inheritdoc />
    public async Task<StoredProcessInstance> CreateProcessInstanceAsync(StoredProcessInstance instance, CancellationToken ct = default)
    {
        instance.LastUpdatedUtc = DateTime.UtcNow;
        _db.ProcessInstances.Add(instance);
        await _db.SaveChangesAsync(ct);
        return instance;
    }

    /// <inheritdoc />
    public async Task UpdateProcessInstanceAsync(StoredProcessInstance instance, CancellationToken ct = default)
    {
        instance.LastUpdatedUtc = DateTime.UtcNow;
        _db.ProcessInstances.Update(instance);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<StoredProcessInstance?> GetProcessInstanceByIdAsync(string id, CancellationToken ct = default)
        => _db.ProcessInstances.Include(i => i.StepExecutions).Include(i => i.Notes).FirstOrDefaultAsync(i => i.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredProcessInstance>> QueryProcessInstancesAsync(ProcessInstanceQuery query, CancellationToken ct = default)
    {
        var q = _db.ProcessInstances.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.ProcessKey)) q = q.Where(i => i.ProcessKey == query.ProcessKey);
        if (!string.IsNullOrWhiteSpace(query.DefinitionId)) q = q.Where(i => i.ProcessDefinitionId == query.DefinitionId);
        if (!string.IsNullOrWhiteSpace(query.BusinessCorrelationId)) q = q.Where(i => i.BusinessCorrelationId == query.BusinessCorrelationId);
        if (query.Statuses != null && query.Statuses.Length > 0) q = q.Where(i => query.Statuses.Contains(i.Status));
        if (query.StartedFromUtc.HasValue) q = q.Where(i => i.StartedUtc >= query.StartedFromUtc);
        if (query.StartedToUtc.HasValue) q = q.Where(i => i.StartedUtc < query.StartedToUtc);
        q = q.OrderByDescending(i => i.StartedUtc).Skip(query.Skip).Take(query.Take);
        return await q.ToListAsync(ct);
    }
    #endregion

    #region Step Executions
    /// <inheritdoc />
    public async Task<StepExecutionRecord> AppendStepExecutionAsync(StepExecutionRecord record, CancellationToken ct = default)
    {
        // Pre-calc duration if both timestamps known
        if (record.EndUtc.HasValue && record.StartUtc != default && !record.DurationMs.HasValue)
        {
            record.DurationMs = (long)(record.EndUtc.Value - record.StartUtc).TotalMilliseconds;
        }
        _db.StepExecutions.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StepExecutionRecord>> GetStepExecutionsAsync(string instanceId, CancellationToken ct = default)
        => await _db.StepExecutions.Where(s => s.InstanceId == instanceId).OrderBy(s => s.Sequence).ToListAsync(ct);
    #endregion

    #region KPI Aggregation
    /// <inheritdoc />
    public async Task<IReadOnlyList<StepKpiAggregate>> QueryStepKpisAsync(StepKpiQuery query, CancellationToken ct = default)
    {
        var q = _db.StepExecutions.AsNoTracking().Where(s => s.EndUtc != null && s.DurationMs != null);
        if (!string.IsNullOrWhiteSpace(query.ProcessKey)) q = q.Where(s => s.ProcessKey == query.ProcessKey);
        if (!string.IsNullOrWhiteSpace(query.DefinitionId)) q = q.Where(s => s.ProcessDefinitionId == query.DefinitionId);
        if (!string.IsNullOrWhiteSpace(query.ElementId)) q = q.Where(s => s.ElementId == query.ElementId);
        if (query.FromUtc.HasValue) q = q.Where(s => s.StartUtc >= query.FromUtc);
        if (query.ToUtc.HasValue) q = q.Where(s => s.StartUtc < query.ToUtc);

        switch (query.GroupBy)
        {
            case KpiGroupBy.ProcessOnly:
                return await q.GroupBy(s => new { s.ProcessKey })
                    .Select(g => new StepKpiAggregate
                    {
                        ProcessKey = g.Key.ProcessKey,
                        ElementId = null,
                        Count = g.Count(),
                        AvgDurationMs = g.Average(x => x.DurationMs!.Value),
                        MinDurationMs = g.Min(x => x.DurationMs!.Value),
                        MaxDurationMs = g.Max(x => x.DurationMs!.Value)
                    }).ToListAsync(ct);
            case KpiGroupBy.ElementOnly:
                return await q.GroupBy(s => new { s.ElementId })
                    .Select(g => new StepKpiAggregate
                    {
                        ProcessKey = string.Empty,
                        ElementId = g.Key.ElementId,
                        Count = g.Count(),
                        AvgDurationMs = g.Average(x => x.DurationMs!.Value),
                        MinDurationMs = g.Min(x => x.DurationMs!.Value),
                        MaxDurationMs = g.Max(x => x.DurationMs!.Value)
                    }).ToListAsync(ct);
            default:
                return await q.GroupBy(s => new { s.ProcessKey, s.ElementId })
                    .Select(g => new StepKpiAggregate
                    {
                        ProcessKey = g.Key.ProcessKey,
                        ElementId = g.Key.ElementId,
                        Count = g.Count(),
                        AvgDurationMs = g.Average(x => x.DurationMs!.Value),
                        MinDurationMs = g.Min(x => x.DurationMs!.Value),
                        MaxDurationMs = g.Max(x => x.DurationMs!.Value)
                    }).ToListAsync(ct);
        }
    }
    #endregion

    #region Status Transitions
    /// <inheritdoc />
    public async Task MarkInstanceCancelledAsync(string instanceId, string? reason, DateTime utcNow, CancellationToken ct = default)
    {
        var inst = await GetProcessInstanceByIdAsync(instanceId, ct) ?? throw new InvalidOperationException("Instance not found");
        if (inst.Status == ProcessInstanceStatus.Completed) return; // no-op if already terminal
        inst.Status = ProcessInstanceStatus.Cancelled;
        inst.CancellationReason = reason;
        inst.CancelledUtc = utcNow;
        await UpdateProcessInstanceAsync(inst, ct);
    }

    /// <inheritdoc />
    public async Task MarkInstanceFailedAsync(string instanceId, string error, DateTime utcNow, CancellationToken ct = default)
    {
        var inst = await GetProcessInstanceByIdAsync(instanceId, ct) ?? throw new InvalidOperationException("Instance not found");
        if (inst.Status == ProcessInstanceStatus.Completed) return; // no-op if already terminal
        inst.Status = ProcessInstanceStatus.Failed;
        inst.FailureReason = error;
        inst.FailedUtc = utcNow;
        await UpdateProcessInstanceAsync(inst, ct);
    }
    #endregion

    #region Notes
    /// <inheritdoc />
    public async Task AddInstanceNoteAsync(InstanceNote note, CancellationToken ct = default)
    {
        _db.InstanceNotes.Add(note);
        await _db.SaveChangesAsync(ct);
    }
    #endregion

    #region SaveChanges
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
    #endregion

    /// <summary>Computes SHA256 hash (hex) for supplied content.</summary>
    public static string ComputeHash(string content)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}
