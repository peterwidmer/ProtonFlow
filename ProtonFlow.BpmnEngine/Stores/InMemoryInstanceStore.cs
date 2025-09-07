namespace BpmnEngine.Stores;

using BpmnEngine.Interfaces;
using BpmnEngine.Models;

public class InMemoryInstanceStore : IInstanceStore
{
    private readonly Dictionary<string, ProcessInstance> _byId = new();
    private readonly Dictionary<string, List<string>> _byKey = new();

    public Task SaveAsync(ProcessInstance instance, CancellationToken ct = default)
    {
        _byId[instance.Id] = instance;
        if (!_byKey.TryGetValue(instance.ProcessKey, out var list))
        {
            list = new();
            _byKey[instance.ProcessKey] = list;
        }
        if (!list.Contains(instance.Id)) list.Add(instance.Id);
        return Task.CompletedTask;
    }

    public Task<ProcessInstance?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _byId.TryGetValue(id, out var inst);
        return Task.FromResult<ProcessInstance?>(inst);
    }

    public Task<IEnumerable<ProcessInstance>> GetByProcessKeyAsync(string processKey, CancellationToken ct = default)
    {
        if (_byKey.TryGetValue(processKey, out var list))
            return Task.FromResult<IEnumerable<ProcessInstance>>(list.Select(id => _byId[id]));
        return Task.FromResult<IEnumerable<ProcessInstance>>(Enumerable.Empty<ProcessInstance>());
    }
}
