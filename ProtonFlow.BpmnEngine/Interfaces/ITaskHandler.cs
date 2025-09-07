namespace BpmnEngine.Interfaces;

using BpmnEngine.Runtime;

/// <summary>
/// Contract for extending the engine with custom behavior for BPMN task elements
/// (e.g., serviceTask, scriptTask). Implementations are DI-friendly.
/// </summary>
public interface ITaskHandler
{
    /// <summary>
    /// Logical handler type, matched against the task's 'implementation' or 'type' attribute.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Executes the task in the context of a running process instance.
    /// </summary>
    /// <param name="context">Execution context providing access to variables and instance metadata.</param>
    /// <param name="ct">A cancellation token.</param>
    Task ExecuteAsync(TaskContext context, CancellationToken ct = default);
}
