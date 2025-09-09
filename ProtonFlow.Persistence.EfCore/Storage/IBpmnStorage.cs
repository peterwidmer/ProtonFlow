using ProtonFlow.Persistence.EfCore.Storage.Models;

namespace ProtonFlow.Persistence.EfCore.Storage;

/// <summary>
/// Unified persistence abstraction for ProtonFlow BPMN runtime elements.
/// This interface groups operations for process definitions, process instances,
/// step / token execution history and KPI queries. The intent is to offer a single
/// seam where alternative storage providers (EF Core, Document DB, etc.) can plug in
/// while the higher engine / API layers depend only on this contract.
/// </summary>
/// <remarks>
/// Design principles:
/// 1. Read model first: runtime engine keeps lightweight in-memory objects; persistence serializes opaque state.
/// 2. Evolvable: additional concerns (timers, messages, audit, multi-tenancy) can be appended without breaking existing members.
/// 3. Testability: all methods are async and cancellation aware; EF implementation is deterministic under SQLite in-memory.
/// </remarks>
public interface IBpmnStorage
{
    #region Process Definitions
    /// <summary>
    /// Persists a new process definition version. Implementations should automatically manage the <see cref="StoredProcessDefinition.IsLatest"/> flag.
    /// </summary>
    Task<StoredProcessDefinition> SaveProcessDefinitionAsync(StoredProcessDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a process definition by its internal identifier.
    /// </summary>
    Task<StoredProcessDefinition?> GetProcessDefinitionByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a process definition by business key (and optional version). When version is null the latest version is returned.
    /// </summary>
    Task<StoredProcessDefinition?> GetProcessDefinitionByKeyAsync(string key, int? version = null, CancellationToken ct = default);

    /// <summary>
    /// Returns all definitions optionally filtered by key (all versions returned ordered descending by version).
    /// </summary>
    Task<IReadOnlyList<StoredProcessDefinition>> GetProcessDefinitionsAsync(string? key = null, CancellationToken ct = default);
    #endregion

    #region Process Instances
    /// <summary>
    /// Creates (persists) a new process instance. Caller must ensure the referenced definition exists.
    /// </summary>
    Task<StoredProcessInstance> CreateProcessInstanceAsync(StoredProcessInstance instance, CancellationToken ct = default);

    /// <summary>
    /// Updates a previously stored instance. Implementations may apply optimistic concurrency via row version.
    /// </summary>
    Task UpdateProcessInstanceAsync(StoredProcessInstance instance, CancellationToken ct = default);

    /// <summary>
    /// Fetches a stored process instance including basic navigation properties (e.g. step executions) if desired by implementation.
    /// </summary>
    Task<StoredProcessInstance?> GetProcessInstanceByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Flexible query endpoint for listing process instances by status, key, correlation etc.
    /// </summary>
    Task<IReadOnlyList<StoredProcessInstance>> QueryProcessInstancesAsync(ProcessInstanceQuery query, CancellationToken ct = default);
    #endregion

    #region Step Executions & History
    /// <summary>
    /// Appends a single step execution record (token movement, task execution etc.). Sequence must be monotonically increasing per instance.
    /// </summary>
    Task<StepExecutionRecord> AppendStepExecutionAsync(StepExecutionRecord record, CancellationToken ct = default);

    /// <summary>
    /// Returns ordered step execution history for a given instance.
    /// </summary>
    Task<IReadOnlyList<StepExecutionRecord>> GetStepExecutionsAsync(string instanceId, CancellationToken ct = default);
    #endregion

    #region KPI Queries
    /// <summary>
    /// Dynamically aggregates KPI metrics (count, min, max, average duration) from raw step executions.
    /// </summary>
    Task<IReadOnlyList<StepKpiAggregate>> QueryStepKpisAsync(StepKpiQuery query, CancellationToken ct = default);
    #endregion

    #region Instance Status Transitions
    /// <summary>
    /// Marks an instance as cancelled, persisting the cancellation timestamp and optional human-readable reason.
    /// </summary>
    Task MarkInstanceCancelledAsync(string instanceId, string? reason, DateTime utcNow, CancellationToken ct = default);

    /// <summary>
    /// Marks an instance as failed, recording the error message and timestamp for audit / troubleshooting.
    /// </summary>
    Task MarkInstanceFailedAsync(string instanceId, string error, DateTime utcNow, CancellationToken ct = default);
    #endregion

    #region Notes / Lightweight Audit
    /// <summary>
    /// Persists a note (lightweight audit / annotation) attached to an instance. Future audit log features can build on this concept.
    /// </summary>
    Task AddInstanceNoteAsync(InstanceNote note, CancellationToken ct = default);
    #endregion

    #region Persistence Unit
    /// <summary>
    /// Flushes outstanding changes if the implementation defers writes (EF implementation simply delegates to DbContext.SaveChangesAsync).
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    #endregion
}

/// <summary>
/// Encapsulates common filters when querying for process instances.
/// </summary>
public sealed class ProcessInstanceQuery
{
    /// <summary>Filter by business key (BPMN process id).</summary>
    public string? ProcessKey { get; set; }
    /// <summary>Filter by specific deployed definition id.</summary>
    public string? DefinitionId { get; set; }
    /// <summary>External correlation (e.g., OrderId) enabling cross-system joins.</summary>
    public string? BusinessCorrelationId { get; set; }
    /// <summary>Restrict to one or more lifecycle statuses.</summary>
    public ProcessInstanceStatus[]? Statuses { get; set; }
    /// <summary>Instances started on or after this UTC instant.</summary>
    public DateTime? StartedFromUtc { get; set; }
    /// <summary>Instances started strictly before this UTC instant.</summary>
    public DateTime? StartedToUtc { get; set; }
    /// <summary>Number of rows to skip for pagination (defaults to 0).</summary>
    public int Skip { get; set; }
    /// <summary>Maximum number of rows to return (defaults to 100 to protect from accidental large queries).</summary>
    public int Take { get; set; } = 100;
}

/// <summary>
/// Filter information for dynamic KPI aggregation queries.
/// </summary>
public sealed class StepKpiQuery
{
    /// <summary>Restrict KPI aggregation to a single process key.</summary>
    public string? ProcessKey { get; set; }
    /// <summary>Restrict to a single deployed definition id (allows version-scoped analysis).</summary>
    public string? DefinitionId { get; set; }
    /// <summary>Restrict to a single BPMN element id (e.g., a task id).</summary>
    public string? ElementId { get; set; }
    /// <summary>Only include steps starting on or after this UTC instant.</summary>
    public DateTime? FromUtc { get; set; }
    /// <summary>Only include steps starting strictly before this UTC instant.</summary>
    public DateTime? ToUtc { get; set; }
    /// <summary>Controls aggregation grouping granularity (process vs element vs both).</summary>
    public KpiGroupBy GroupBy { get; set; } = KpiGroupBy.ProcessAndElement;
}

/// <summary>
/// Defines grouping shapes for KPI aggregation enabling caller-controlled granularity.
/// </summary>
public enum KpiGroupBy
{
    /// <summary>Aggregate over process key only (merging all element ids).</summary>
    ProcessOnly,
    /// <summary>Aggregate over both process key and element id (default).</summary>
    ProcessAndElement,
    /// <summary>Aggregate over element id only across all processes (useful for shared task performance).</summary>
    ElementOnly
}

/// <summary>
/// Lifecycle statuses for persisted process instances (superset of in-memory state flags).
/// </summary>
public enum ProcessInstanceStatus
{
    /// <summary>Instance is actively progressing or waiting.</summary>
    Running,
    /// <summary>All tokens reached end event(s) successfully.</summary>
    Completed,
    /// <summary>Manually or system-cancelled prior to completion.</summary>
    Cancelled,
    /// <summary>Terminated due to an unhandled exception / failure.</summary>
    Failed
}

/// <summary>
/// Result object representing aggregated KPI statistics for a grouping bucket.
/// Not mapped as table in MVP (computed dynamically) but structured for potential future persistence.
/// </summary>
public class StepKpiAggregate
{
    /// <summary>Process key grouping value (may be null if grouping by element only).</summary>
    public string ProcessKey { get; set; } = string.Empty;
    /// <summary>Element id grouping value (may be null if grouping by process only).</summary>
    public string? ElementId { get; set; }
    /// <summary>Total number of completed step executions in bucket.</summary>
    public int Count { get; set; }
    /// <summary>Mean duration in milliseconds.</summary>
    public double AvgDurationMs { get; set; }
    /// <summary>Shortest observed duration in milliseconds.</summary>
    public long MinDurationMs { get; set; }
    /// <summary>Longest observed duration in milliseconds.</summary>
    public long MaxDurationMs { get; set; }
}
