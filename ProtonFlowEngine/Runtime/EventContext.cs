namespace BpmnEngine.Runtime;

using BpmnEngine.Models;

public class EventContext
{
    public required ProcessInstance Instance { get; init; }
    public required string ElementId { get; init; }
}
