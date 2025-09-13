namespace BpmnEngine.Stores;

using System.Collections.Concurrent;
using BpmnEngine.Interfaces;

/// <summary>
/// In-memory job store to support unit tests and lightweight scenarios. Not suitable for HA but enforces
/// consistent semantics (single-claim, lease, complete) within a single process using locks.
/// </summary>
public class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly object _claimLock = new();

    public Task EnqueueJobAsync(Job job, CancellationToken cancellationToken = default)
    {
        if (job.Id == Guid.Empty) job.Id = Guid.NewGuid();
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<Job?> ClaimNextJobAsync(string nodeId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_claimLock)
        {
            var next = _jobs.Values
                .Where(j => (j.RunAt == null || j.RunAt <= now) && (j.LockedUntil == null || j.LockedUntil < now))
                .OrderBy(j => j.RunAt ?? now)
                .FirstOrDefault();
            if (next == null) return Task.FromResult<Job?>(null);

            // Claim atomically under lock
            next.OwnerId = nodeId;
            next.LockedUntil = now.Add(leaseDuration);
            next.Attempt += 1;
            next.RowVersion = IncrementRowVersion(next.RowVersion);
            return Task.FromResult<Job?>(Clone(next));
        }
    }

    public Task CompleteJobAsync(Guid jobId, string nodeId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.OwnerId == nodeId)
            {
                _jobs.TryRemove(jobId, out _);
            }
        }
        return Task.CompletedTask;
    }

    private static byte[] IncrementRowVersion(byte[] rv)
    {
        if (rv == null || rv.Length == 0) return new byte[] { 1 };
        var copy = (byte[])rv.Clone();
        for (int i = copy.Length - 1; i >= 0; i--)
        {
            if (copy[i] < byte.MaxValue) { copy[i]++; break; }
            copy[i] = 0;
        }
        return copy;
    }

    private static Job Clone(Job j) => new()
    {
        Id = j.Id,
        Type = j.Type,
        ProcessInstanceId = j.ProcessInstanceId,
        RunAt = j.RunAt,
        OwnerId = j.OwnerId,
        LockedUntil = j.LockedUntil,
        Attempt = j.Attempt,
        RowVersion = (byte[])j.RowVersion.Clone()
    };
}
