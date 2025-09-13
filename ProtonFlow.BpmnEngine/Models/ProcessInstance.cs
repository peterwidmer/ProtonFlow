namespace BpmnEngine.Models;

/// <summary>
/// Represents a running instance of a BPMN process with its current execution state.
/// Contains variables, active token positions, and lifecycle information.
/// Mutable during execution to track state changes as the process progresses.
/// </summary>
public class ProcessInstance
{
    /// <summary>
    /// Unique identifier for this process instance.
    /// Generated automatically when the instance is created.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    
    /// <summary>
    /// Reference to the process definition ID that this instance is executing.
    /// Links the instance to its deployed process definition.
    /// </summary>
    public string ProcessDefinitionId { get; set; } = string.Empty;
    
    /// <summary>
    /// Business key of the process type being executed.
    /// Corresponds to the BPMN process id attribute.
    /// </summary>
    public string ProcessKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Process variables containing business data and execution context.
    /// Variables can be read and modified by task handlers during execution.
    /// Automatically serialized for persistence in durable storage providers.
    /// </summary>
    public Dictionary<string, object?> Variables { get; } = new Dictionary<string, object?>();
    
    /// <summary>
    /// Set of element IDs where execution tokens are currently positioned.
    /// Represents the current execution state of the process instance.
    /// Empty when the process has completed or not yet started.
    /// </summary>
    public HashSet<string> ActiveTokens { get; } = new HashSet<string>();
    
    /// <summary>
    /// Indicates whether this process instance has completed execution.
    /// True when all tokens have reached end events or the process was terminated.
    /// </summary>
    public bool IsCompleted { get; set; }
    
    /// <summary>
    /// Indicates whether this instance is running in simulation mode.
    /// When true, task handlers are not executed and only token movement is simulated.
    /// Used for process validation and testing without side effects.
    /// </summary>
    public bool SimulationMode { get; set; }

    /// <summary>
    /// Tracks the number of incoming tokens that have arrived at parallel join gateways.
    /// Key: gateway element ID, Value: count of tokens received.
    /// Used to coordinate parallel execution and determine when joins can proceed.
    /// </summary>
    public Dictionary<string, int> ParallelJoinWaits { get; } = new Dictionary<string, int>();

    /// <summary>
    /// Optimistic concurrency control token used by persistence providers.
    /// EF Core and other providers may use this to prevent concurrent modification conflicts.
    /// Unused in in-memory scenarios but preserved for storage provider compatibility.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}
