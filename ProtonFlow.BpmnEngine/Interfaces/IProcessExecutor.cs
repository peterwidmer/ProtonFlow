namespace BpmnEngine.Interfaces;

using BpmnEngine.Models;

/// <summary>
/// Executes a BPMN process definition by advancing process instances through the model.
/// </summary>
public interface IProcessExecutor
{
    /// <summary>
    /// Creates a new process instance for the given definition and initializes its state.
    /// </summary>
    /// <param name="definition">The process definition to instantiate.</param>
    /// <param name="variables">An optional anonymous object carrying initial variables.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created <see cref="ProcessInstance"/>.</returns>
    Task<ProcessInstance> StartAsync(ProcessDefinition definition, object? variables = null, CancellationToken ct = default);

    /// <summary>
    /// Advances the specified instance by one deterministic step.
    /// </summary>
    /// <param name="instance">The instance to step.</param>
    /// <param name="ct">A cancellation token.</param>
    Task StepAsync(ProcessInstance instance, CancellationToken ct = default);

    /// <summary>
    /// Indicates whether the instance can be stepped (has active tokens and is not completed).
    /// </summary>
    /// <param name="instance">The instance to evaluate.</param>
    bool CanStep(ProcessInstance instance);
}
