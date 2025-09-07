namespace BpmnEngine.Runtime;

using BpmnEngine.Interfaces;
using BpmnEngine.Models;

public class Simulator
{
    private readonly IProcessExecutor _executor;
    private readonly ProcessInstance _instance;

    public Simulator(IProcessExecutor executor, ProcessInstance instance)
    {
        _executor = executor;
        _instance = instance;
    }

    public bool CanStep => _executor.CanStep(_instance);

    public Task StepAsync(CancellationToken ct = default) => _executor.StepAsync(_instance, ct);
}
