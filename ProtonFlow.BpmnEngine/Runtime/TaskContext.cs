namespace BpmnEngine.Runtime;

using BpmnEngine.Models;

public class TaskContext
{
    public ProcessInstance Instance { get; set; }
    public string ElementId { get; set; }

    public TaskContext(ProcessInstance instance, string elementId)
    {
        Instance = instance;
        ElementId = elementId;
    }

    public T? GetVariable<T>(string name)
    {
        if (Instance.Variables.TryGetValue(name, out var value) && value is T t)
            return t;
        return default;
    }
}
