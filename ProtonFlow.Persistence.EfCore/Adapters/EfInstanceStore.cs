using System.Text.Json;
using BpmnEngine.Interfaces;
using BpmnEngine.Models;
using ProtonFlow.Persistence.EfCore.Storage;
using ProtonFlow.Persistence.EfCore.Storage.Models;

namespace ProtonFlow.Persistence.EfCore.Adapters;

/// <summary>
/// Adapter bridging existing engine <see cref="IInstanceStore"/> contract to the richer <see cref="IBpmnStorage"/>.
/// Keeps backward compatibility so engine code and existing tests continue to function when EF persistence is plugged in.
/// </summary>
public class EfInstanceStore : IInstanceStore
{
    private readonly IBpmnStorage _storage;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.General);

    /// <summary>Create a new adapter around a unified storage implementation.</summary>
    public EfInstanceStore(IBpmnStorage storage) => _storage = storage;

    /// <inheritdoc />
    public async Task SaveAsync(ProcessInstance instance, CancellationToken ct = default)
    {
        // Load existing persisted state (if any)
        var existing = await _storage.GetProcessInstanceByIdAsync(instance.Id, ct);
        var stored = existing ?? new StoredProcessInstance
        {
            Id = instance.Id,
            ProcessDefinitionId = instance.ProcessDefinitionId,
            ProcessKey = instance.ProcessKey
        };

        // Try to flow row version forward (for readers observing it). This does not enforce OCC in adapter path but preserves the value.
        stored.RowVersion = instance.RowVersion;

        stored.VariablesJson = JsonSerializer.Serialize(instance.Variables, _json);
        stored.ActiveTokensJson = JsonSerializer.Serialize(instance.ActiveTokens, _json);
        stored.ParallelJoinWaitsJson = JsonSerializer.Serialize(instance.ParallelJoinWaits, _json);

        if (instance.IsCompleted && stored.Status == ProcessInstanceStatus.Running)
        {
            stored.Status = ProcessInstanceStatus.Completed;
            stored.CompletedUtc = DateTime.UtcNow;
        }

        if (existing == null)
            await _storage.CreateProcessInstanceAsync(stored, ct);
        else
            await _storage.UpdateProcessInstanceAsync(stored, ct);

        // Reflect back any provider-updated row version
        instance.RowVersion = stored.RowVersion;
    }

    /// <inheritdoc />
    public async Task<ProcessInstance?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var stored = await _storage.GetProcessInstanceByIdAsync(id, ct);
        if (stored == null) return null;

        var inst = new ProcessInstance
        {
            Id = stored.Id,
            ProcessDefinitionId = stored.ProcessDefinitionId,
            ProcessKey = stored.ProcessKey,
            IsCompleted = stored.Status == ProcessInstanceStatus.Completed,
            RowVersion = stored.RowVersion
        };

        // Deserialize serialized state back into runtime instance
        var vars = Deserialize<Dictionary<string, object?>>(stored.VariablesJson) ?? new();
        foreach (var kv in vars) inst.Variables[kv.Key] = kv.Value;
        var tokens = Deserialize<List<string>>(stored.ActiveTokensJson) ?? new();
        foreach (var t in tokens) inst.ActiveTokens.Add(t);
        var joins = Deserialize<Dictionary<string, int>>(stored.ParallelJoinWaitsJson) ?? new();
        foreach (var kv in joins) inst.ParallelJoinWaits[kv.Key] = kv.Value;

        return inst;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ProcessInstance>> GetByProcessKeyAsync(string processKey, CancellationToken ct = default)
    {
        var stored = await _storage.QueryProcessInstancesAsync(new ProcessInstanceQuery { ProcessKey = processKey, Take = 1000 }, ct);
        var list = new List<ProcessInstance>();
        foreach (var s in stored)
        {
            var inst = await GetByIdAsync(s.Id, ct);
            if (inst != null) list.Add(inst);
        }
        return list;
    }

    private T? Deserialize<T>(string json) => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, _json);
}
