namespace BpmnEngine.Interfaces;

using BpmnEngine.Models;

/// <summary>
/// Abstraction over persistence for process instances.
/// Implementations can store instances in-memory or in durable stores.
/// </summary>
public interface IInstanceStore
{
    /// <summary>
    /// Persists or updates a process instance.
    /// </summary>
    /// <param name="instance">The instance to save.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SaveAsync(ProcessInstance instance, CancellationToken ct = default);

    /// <summary>
    /// Retrieves an instance by id.
    /// </summary>
    /// <param name="id">The instance id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The instance or null if not found.</returns>
    Task<ProcessInstance?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all instances for a given process key.
    /// </summary>
    /// <param name="processKey">The process definition key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An enumeration of matching instances.</returns>
    Task<IEnumerable<ProcessInstance>> GetByProcessKeyAsync(string processKey, CancellationToken ct = default);
}
