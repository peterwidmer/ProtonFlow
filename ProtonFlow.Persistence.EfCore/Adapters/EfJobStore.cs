using BpmnEngine.Interfaces;
using Microsoft.EntityFrameworkCore;
using ProtonFlow.Persistence.EfCore.Storage;
using ProtonFlow.Persistence.EfCore.Storage.Models;

namespace ProtonFlow.Persistence.EfCore.Adapters;

/// <summary>
/// EF Core-backed implementation of the engine <see cref="IJobStore"/> providing atomic claim semantics using
/// optimistic concurrency and transactions. Works with SQLite and other EF providers.
/// </summary>
public class EfJobStore : IJobStore
{
    private readonly ProtonFlowDbContext _db;

    public EfJobStore(ProtonFlowDbContext db) => _db = db;

    public async Task EnqueueJobAsync(BpmnEngine.Interfaces.Job job, CancellationToken cancellationToken = default)
    {
        var entity = new ProtonFlow.Persistence.EfCore.Storage.Models.Job
        {
            Id = job.Id == Guid.Empty ? Guid.NewGuid() : job.Id,
            Type = job.Type,
            ProcessInstanceId = job.ProcessInstanceId,
            RunAt = job.RunAt,
            OwnerId = job.OwnerId,
            LockedUntil = job.LockedUntil,
            Attempt = job.Attempt,
            RowVersion = job.RowVersion is { Length: > 0 } ? job.RowVersion : new byte[] { 1 }
        };
        _db.Jobs.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        job.Id = entity.Id;
        job.RowVersion = entity.RowVersion;
    }

    public async Task<BpmnEngine.Interfaces.Job?> ClaimNextJobAsync(string nodeId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        ProtonFlow.Persistence.EfCore.Storage.Models.Job? candidate;

        if (_db.Database.IsSqlite())
        {
            // Use FromSqlRaw to bypass LINQ translation issues with SQLite provider for complex OR conditions.
            candidate = await _db.Jobs
                .FromSqlRaw("SELECT * FROM Jobs WHERE (RunAt <= {0} OR RunAt IS NULL) AND (LockedUntil < {0} OR LockedUntil IS NULL) ORDER BY RunAt LIMIT 1", now)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            // This LINQ query is portable to more advanced providers like SQL Server and PostgreSQL.
            candidate = await _db.Jobs
                .Where(j => j.RunAt <= now || j.RunAt == null)
                .Where(j => j.LockedUntil < now || j.LockedUntil == null)
                .OrderBy(j => j.RunAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (candidate == null)
        {
            await tx.CommitAsync(cancellationToken);
            return null;
        }

        // Mark as claimed and bump version
        candidate.OwnerId = nodeId;
        candidate.LockedUntil = now.Add(leaseDuration);
        candidate.Attempt += 1;
        candidate.RowVersion = Increment(candidate.RowVersion);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(cancellationToken);
            return null;
        }

        return new BpmnEngine.Interfaces.Job
        {
            Id = candidate.Id,
            Type = candidate.Type,
            ProcessInstanceId = candidate.ProcessInstanceId,
            RunAt = candidate.RunAt,
            OwnerId = candidate.OwnerId,
            LockedUntil = candidate.LockedUntil,
            Attempt = candidate.Attempt,
            RowVersion = candidate.RowVersion
        };
    }

    public async Task CompleteJobAsync(Guid jobId, string nodeId, CancellationToken cancellationToken = default)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job == null) return;
        if (!string.Equals(job.OwnerId, nodeId, StringComparison.Ordinal)) return;
        _db.Jobs.Remove(job);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static byte[] Increment(byte[]? value)
    {
        if (value == null || value.Length == 0) return new byte[] { 1 };
        var copy = (byte[])value.Clone();
        for (int i = copy.Length - 1; i >= 0; i--)
        {
            if (copy[i] < byte.MaxValue) { copy[i]++; break; }
            copy[i] = 0;
        }
        return copy;
    }
}
