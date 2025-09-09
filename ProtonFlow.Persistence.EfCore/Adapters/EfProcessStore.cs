using System.Security.Cryptography;
using System.Text;
using BpmnEngine.Interfaces;
using BpmnEngine.Models;
using ProtonFlow.Persistence.EfCore.Storage;
using ProtonFlow.Persistence.EfCore.Storage.Models;

namespace ProtonFlow.Persistence.EfCore.Adapters;

/// <summary>
/// Adapter implementing the engine's legacy <see cref="IProcessStore"/> over the new unified <see cref="IBpmnStorage"/>.
/// Converts between runtime <see cref="ProcessDefinition"/> and persistence <see cref="StoredProcessDefinition"/>.
/// </summary>
public class EfProcessStore : IProcessStore
{
    private readonly IBpmnStorage _storage;

    /// <summary>Create a new adapter.</summary>
    public EfProcessStore(IBpmnStorage storage) => _storage = storage;

    /// <inheritdoc />
    public async Task SaveAsync(ProcessDefinition definition, CancellationToken ct = default)
    {
        // Determine hash to identify duplicate content deployments.
        var hash = ComputeHash(definition.Xml);
        var stored = new StoredProcessDefinition
        {
            Key = definition.Key,
            Name = definition.Name,
            Xml = definition.Xml,
            ContentHash = hash,
            // Version auto-assigned by storage if 0.
            Version = 0,
            IsLatest = true
        };
        await _storage.SaveProcessDefinitionAsync(stored, ct);

        // Update runtime id mapping if storage produced a new id. (Engine currently uses runtime definition.Id internally.)
        // We leave runtime object unchanged; for EF backed scenarios engine will still load definitions through adapter.
    }

    /// <inheritdoc />
    public async Task<ProcessDefinition?> GetByKeyAsync(string processKey, CancellationToken ct = default)
    {
        var stored = await _storage.GetProcessDefinitionByKeyAsync(processKey, null, ct);
        if (stored == null) return null;
        return MapRuntime(stored);
    }

    /// <inheritdoc />
    public async Task<ProcessDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var stored = await _storage.GetProcessDefinitionByIdAsync(id, ct);
        return stored == null ? null : MapRuntime(stored);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ProcessDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await _storage.GetProcessDefinitionsAsync(null, ct);
        return all.Select(MapRuntime);
    }

    private static ProcessDefinition MapRuntime(StoredProcessDefinition stored)
    {
        // Rebuild lightweight runtime definition (without element model reconstruction - parsing occurs elsewhere on load by engine).
        return new ProcessDefinition(
            id: stored.Id,
            key: stored.Key,
            name: stored.Name,
            xml: stored.Xml,
            elements: new Dictionary<string, BpmnElement>() // engine will re-parse on LoadBpmnXml; persisted def used mainly for lookup.
        );
    }

    private static string ComputeHash(string xml)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(xml)));
    }
}
