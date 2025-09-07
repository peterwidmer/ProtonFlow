namespace BpmnEngine.Interfaces;

using BpmnEngine.Models;

/// <summary>
/// Abstraction over persistence for BPMN process definitions.
/// Allows different storage providers (e.g., in-memory, EF Core, MongoDB) to be plugged in.
/// </summary>
public interface IProcessStore
{
    /// <summary>
    /// Persists or updates a process definition.
    /// </summary>
    /// <param name="definition">The process definition to save.</param>
    /// <param name="ct">A cancellation token.</param>
    Task SaveAsync(ProcessDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a process definition by its business key.
    /// </summary>
    /// <param name="processKey">The process key (typically the BPMN process id).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The matching <see cref="ProcessDefinition"/> or null if not found.</returns>
    Task<ProcessDefinition?> GetByKeyAsync(string processKey, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a process definition by its internal identifier.
    /// </summary>
    /// <param name="id">The definition id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The matching <see cref="ProcessDefinition"/> or null if not found.</returns>
    Task<ProcessDefinition?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all stored process definitions.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An enumeration of definitions.</returns>
    Task<IEnumerable<ProcessDefinition>> GetAllAsync(CancellationToken ct = default);
}
