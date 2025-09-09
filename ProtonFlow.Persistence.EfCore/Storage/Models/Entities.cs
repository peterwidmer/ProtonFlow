using ProtonFlow.Persistence.EfCore.Storage;

namespace ProtonFlow.Persistence.EfCore.Storage.Models;

/// <summary>
/// Persistent representation of a deployed BPMN process definition. Contains metadata required for versioning,
/// lookup and integrity validation. This model is deliberately separate from the runtime ProcessDefinition (lightweight
/// engine model) to avoid persistence concerns leaking into engine types.
/// </summary>
/// <remarks>
/// Keep this class focused on durable concerns only. Runtime parsing of BPMN XML into executable element graph lives in the engine layer.
/// </remarks>
public class StoredProcessDefinition
{
    /// <summary>Internal unique identifier (stable across deployments; generated per version record).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    /// <summary>Business key (BPMN process id); multiple versions share the same key.</summary>
    public string Key { get; set; } = null!;
    /// <summary>Semantic version sequence (integer increment per deployment for same key).</summary>
    public int Version { get; set; }
    /// <summary>Human-friendly name (mirrors BPMN process name attribute).</summary>
    public string Name { get; set; } = null!;
    /// <summary>Raw BPMN XML text exactly as supplied during deployment.</summary>
    public string Xml { get; set; } = null!;
    /// <summary>SHA256 or equivalent content hash used for idempotent re-deploy detection.</summary>
    public string ContentHash { get; set; } = null!;
    /// <summary>UTC timestamp marking deployment time for auditing.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Flag indicating this definition is the latest active version for the given key.</summary>
    public bool IsLatest { get; set; }

    /// <summary>Navigation collection of process instances created from this definition.</summary>
    public ICollection<StoredProcessInstance> Instances { get; set; } = new List<StoredProcessInstance>();
}

/// <summary>
/// Persistent representation of a running or completed BPMN process instance. Stores serialized runtime state
/// (variables, tokens) plus lifecycle metadata used for queries &amp; KPIs.
/// </summary>
public class StoredProcessInstance
{
    /// <summary>Stable unique identifier (matches in-memory instance id).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    /// <summary>Foreign key referencing deployed process definition.</summary>
    public string ProcessDefinitionId { get; set; } = null!;
    /// <summary>Navigation to owning definition (optional eager load in queries).</summary>
    public StoredProcessDefinition Definition { get; set; } = null!;
    /// <summary>Convenience copy of definition business key to allow definition deletion or faster queries.</summary>
    public string ProcessKey { get; set; } = null!;
    /// <summary>External business correlation identifier (e.g., InvoiceId) enabling cross-system joins.</summary>
    public string? BusinessCorrelationId { get; set; }
    /// <summary>Current lifecycle status of the instance.</summary>
    public ProcessInstanceStatus Status { get; set; } = ProcessInstanceStatus.Running;

    /// <summary>UTC timestamp when instance started (created).</summary>
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>UTC timestamp when instance completed successfully.</summary>
    public DateTime? CompletedUtc { get; set; }
    /// <summary>UTC timestamp when instance was cancelled.</summary>
    public DateTime? CancelledUtc { get; set; }
    /// <summary>UTC timestamp when instance failed.</summary>
    public DateTime? FailedUtc { get; set; }
    /// <summary>UTC timestamp of last persisted state change (used for staleness detection / housekeeping).</summary>
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>JSON serialized dictionary of process variables (string -&gt; object).</summary>
    public string VariablesJson { get; set; } = "{}";
    /// <summary>JSON serialized list of currently active token element ids.</summary>
    public string ActiveTokensJson { get; set; } = "[]";
    /// <summary>JSON serialized map of parallel join gateway id -&gt; arrival count.</summary>
    public string ParallelJoinWaitsJson { get; set; } = "{}";

    /// <summary>Error / exception details when status is Failed.</summary>
    public string? FailureReason { get; set; }
    /// <summary>Human readable explanation for cancellation (user / system initiated).</summary>
    public string? CancellationReason { get; set; }

    /// <summary>Related step execution history records.</summary>
    public ICollection<StepExecutionRecord> StepExecutions { get; set; } = new List<StepExecutionRecord>();
    /// <summary>Attached notes (lightweight audit log).</summary>
    public ICollection<InstanceNote> Notes { get; set; } = new List<InstanceNote>();

    /// <summary>Concurrency token enabling optimistic concurrency checks; nullable for SQLite compatibility.</summary>
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Atomic record describing execution of a single BPMN element (task, gateway, event) within an instance.
/// Used to reconstruct execution timeline and derive KPI metrics.
/// </summary>
public class StepExecutionRecord
{
    /// <summary>Surrogate identity (auto increment for relational providers).</summary>
    public long Id { get; set; }
    /// <summary>Foreign key referencing owning process instance.</summary>
    public string InstanceId { get; set; } = null!;
    /// <summary>Navigation to instance.</summary>
    public StoredProcessInstance Instance { get; set; } = null!;
    /// <summary>Foreign key for process definition active at time of execution (denormalized for faster KPI grouping even after instance deletion).</summary>
    public string ProcessDefinitionId { get; set; } = null!;
    /// <summary>Business key copy for quick grouping (denormalized).</summary>
    public string ProcessKey { get; set; } = null!;
    /// <summary>Identifier of BPMN element (startEvent id, task id etc.).</summary>
    public string ElementId { get; set; } = null!;
    /// <summary>BPMN element type (e.g., startEvent, serviceTask) for analytics.</summary>
    public string ElementType { get; set; } = null!;
    /// <summary>Monotonically increasing sequence number per instance (1-based).</summary>
    public int Sequence { get; set; }
    /// <summary>UTC timestamp when execution started.</summary>
    public DateTime StartUtc { get; set; }
    /// <summary>UTC timestamp when execution ended (null for still-running async tasks).</summary>
    public DateTime? EndUtc { get; set; }
    /// <summary>Cached duration in milliseconds (EndUtc - StartUtc) for performance.</summary>
    public long? DurationMs { get; set; }
    /// <summary>Lifecycle status of the step (Started, Completed, Failed, Skipped).</summary>
    public string StepStatus { get; set; } = "Started";
    /// <summary>Error / exception information when status is Failed.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Free-form note / annotation attached to an instance, supporting emerging audit or user comment needs without heavy schema.
/// </summary>
public class InstanceNote
{
    /// <summary>Surrogate identity (auto increment).</summary>
    public long Id { get; set; }
    /// <summary>Foreign key linking to owning instance.</summary>
    public string InstanceId { get; set; } = null!;
    /// <summary>Navigation to owning instance.</summary>
    public StoredProcessInstance Instance { get; set; } = null!;
    /// <summary>UTC timestamp user/system created the note.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Semantic category (Info, User, System, Audit).</summary>
    public string Type { get; set; } = "Info";
    /// <summary>Free-form textual content (short; for large payloads a dedicated audit log extension can be added).</summary>
    public string Message { get; set; } = null!;
}
