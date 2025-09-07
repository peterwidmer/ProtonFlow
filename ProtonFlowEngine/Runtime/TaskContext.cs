namespace BpmnEngine.Runtime;

using BpmnEngine.Models;

public class TaskContext
{
    public required ProcessInstance Instance { get; init; }
    public required string ElementId { get; init; }
    public T? GetVariable<T>(string name)
    {
        if (Instance.Variables.TryGetValue(name, out var value) && value is T t)
            return t;
        return default;
    }
}
