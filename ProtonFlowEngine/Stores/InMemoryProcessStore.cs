namespace BpmnEngine.Stores;

using BpmnEngine.Interfaces;
using BpmnEngine.Models;

public class InMemoryProcessStore : IProcessStore
{
    private readonly Dictionary<string, ProcessDefinition> _byId = new();
    private readonly Dictionary<string, string> _keyIndex = new();

    public Task SaveAsync(ProcessDefinition definition, CancellationToken ct = default)
    {
        _byId[definition.Id] = definition;
        _keyIndex[definition.Key] = definition.Id;
        return Task.CompletedTask;
    }

    public Task<ProcessDefinition?> GetByKeyAsync(string processKey, CancellationToken ct = default)
    {
        if (_keyIndex.TryGetValue(processKey, out var id) && _byId.TryGetValue(id, out var def))
            return Task.FromResult<ProcessDefinition?>(def);
        return Task.FromResult<ProcessDefinition?>(null);
    }

    public Task<ProcessDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _byId.TryGetValue(id, out var def);
        return Task.FromResult<ProcessDefinition?>(def);
    }

    public Task<IEnumerable<ProcessDefinition>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<ProcessDefinition>>(_byId.Values);
}
