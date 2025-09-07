namespace BpmnEngine.Interfaces;

using BpmnEngine.Runtime;

/// <summary>
/// Contract for extending the engine with custom behavior for BPMN event elements
/// (e.g., message events, signal events). Implementations are DI-friendly.
/// </summary>
public interface IEventHandler
{
    /// <summary>
    /// Logical handler type, matched against the event element type or custom attributes.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Handles the event in the context of a running process instance.
    /// </summary>
    /// <param name="context">Execution context describing the event and instance.</param>
    /// <param name="ct">A cancellation token.</param>
    Task HandleAsync(EventContext context, CancellationToken ct = default);
}
