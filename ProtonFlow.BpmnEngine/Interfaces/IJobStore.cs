namespace BpmnEngine.Interfaces;

public class Job
{
    public Guid Id { get; set; }
    public string Type { get; set; } = default!;
    public string ProcessInstanceId { get; set; } = default!;
    public DateTimeOffset? RunAt { get; set; }
    public string? OwnerId { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public int Attempt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public interface IJobStore
{
    Task EnqueueJobAsync(Job job, CancellationToken cancellationToken = default);
    Task<Job?> ClaimNextJobAsync(string nodeId, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task CompleteJobAsync(Guid jobId, string nodeId, CancellationToken cancellationToken = default);
}
