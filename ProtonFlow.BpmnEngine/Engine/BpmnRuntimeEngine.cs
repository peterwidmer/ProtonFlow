namespace BpmnEngine.Engine;

using System.Xml.Linq;
using BpmnEngine.Interfaces;
using BpmnEngine.Models;
using BpmnEngine.Runtime;
using Microsoft.Extensions.DependencyInjection;

public class BpmnRuntimeEngine
{
    private readonly IProcessStore _processStore;
    private readonly IInstanceStore _instanceStore;
    private readonly IProcessExecutor _executor;
    private readonly IEnumerable<ITaskHandler> _taskHandlers;

    public BpmnRuntimeEngine(IProcessStore processStore, IInstanceStore instanceStore, IProcessExecutor executor, IEnumerable<ITaskHandler> taskHandlers)
    {
        _processStore = processStore;
        _instanceStore = instanceStore;
        _executor = executor;
        _taskHandlers = taskHandlers;
    }

    public async Task<ProcessDefinition> LoadBpmnXml(string xmlOrPath)
    {
        // Accepts xml content or a file path
        string xml;
        if (System.IO.File.Exists(xmlOrPath))
        {
            xml = System.IO.File.ReadAllText(xmlOrPath);
        }
        else
        {
            xml = xmlOrPath;
        }

        var xdoc = XDocument.Parse(xml);
        var process = xdoc.Root!.Descendants().FirstOrDefault(e => e.Name.LocalName == "process") ?? throw new InvalidOperationException("No <process> element found");
        var key = process.Attribute("id")?.Value ?? Guid.NewGuid().ToString("n");

        var elements = new Dictionary<string, BpmnElement>();
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

        var def = new ProcessDefinition(
            id: Guid.NewGuid().ToString("n"),
            key: key,
            name: process.Attribute("name")?.Value ?? key,
            xml: xml,
            elements: elements
        );
        await _processStore.SaveAsync(def);
        return def;
    }

    public async Task<ProcessInstance> StartProcessAsync(string processKey, object? variables = null, CancellationToken ct = default)
    {
        var def = await _processStore.GetByKeyAsync(processKey, ct) ?? throw new InvalidOperationException($"Process '{processKey}' not found");
        var instance = await _executor.StartAsync(def, variables, ct);
        await _instanceStore.SaveAsync(instance, ct);
        return instance;
    }

    public Simulator Simulate(string processKey)
    {
        var def = _processStore.GetByKeyAsync(processKey).GetAwaiter().GetResult() ?? throw new InvalidOperationException($"Process '{processKey}' not found");
        var instance = _executor.StartAsync(def, null).GetAwaiter().GetResult();
        instance.SimulationMode = true;
        return new Simulator(_executor, instance);
    }

    public async Task<IEnumerable<string>> GetCurrentTokenPositions(string instanceId, CancellationToken ct = default)
    {
        var inst = await _instanceStore.GetByIdAsync(instanceId, ct) ?? throw new InvalidOperationException("Instance not found");
        return inst.ActiveTokens.ToArray();
    }

    public ITaskHandler? ResolveTaskHandler(string type) => _taskHandlers.FirstOrDefault(h => string.Equals(h.Type, type, StringComparison.OrdinalIgnoreCase));

    public async Task<bool> CanStepAsync(string instanceId, CancellationToken ct = default)
    {
        var inst = await _instanceStore.GetByIdAsync(instanceId, ct) ?? throw new InvalidOperationException("Instance not found");
        return _executor.CanStep(inst);
    }

    public async Task StepAsync(string instanceId, CancellationToken ct = default)
    {
        var inst = await _instanceStore.GetByIdAsync(instanceId, ct) ?? throw new InvalidOperationException("Instance not found");
        await _executor.StepAsync(inst, ct);
        await _instanceStore.SaveAsync(inst, ct);
    }
}
