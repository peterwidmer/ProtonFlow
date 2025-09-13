using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
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
        // Parse the XML to rebuild the elements dictionary so that StartAsync can find start events
        var elements = ParseElementsFromXml(stored.Xml);

        return new ProcessDefinition(
            id: stored.Id,
            key: stored.Key,
            name: stored.Name,
            xml: stored.Xml,
            elements: elements
        );
    }

    private static Dictionary<string, BpmnElement> ParseElementsFromXml(string xml)
    {
        var elements = new Dictionary<string, BpmnElement>();
        var xdoc = XDocument.Parse(xml);
        var process = xdoc.Root!.Descendants().FirstOrDefault(e => e.Name.LocalName == "process");

        if (process == null) return elements;

        foreach (var el in process.Descendants())
        {
            var id = el.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id)) continue;

            switch (el.Name.LocalName)
            {
                case "startEvent":
                    elements[id] = new StartEvent(id);
                    break;
                case "endEvent":
                    elements[id] = new EndEvent(id);
                    break;
                case "serviceTask":
                    var implType = el.Attribute("implementation")?.Value ?? el.Attribute("type")?.Value ?? "";
                    elements[id] = new ServiceTask(id, implType);
                    break;
                case "scriptTask":
                    var script = el.Value;
                    elements[id] = new ScriptTask(id, script);
                    break;
                case "exclusiveGateway":
                    elements[id] = new ExclusiveGateway(id);
                    break;
                case "parallelGateway":
                    elements[id] = new ParallelGateway(id);
                    break;
            }
        }

        return elements;
    }

    private static string ComputeHash(string xml)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(xml)));
    }
}
