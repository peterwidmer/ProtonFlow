namespace BpmnEngine.Runtime;

using BpmnEngine.Models;

public class EventContext
{
    public ProcessInstance Instance { get; set; }
    public string ElementId { get; set; }

    public EventContext(ProcessInstance instance, string elementId)
    {
        Instance = instance;
        ElementId = elementId;
    }
}
