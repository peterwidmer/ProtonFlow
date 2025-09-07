namespace BpmnEngine.Models;

public record ProcessDefinition(
    string Id,
    string Key,
    string Name,
    string Xml,
    IReadOnlyDictionary<string, BpmnElement> Elements
);

public abstract record BpmnElement(string Id, string Type);

public record StartEvent(string Id) : BpmnElement(Id, "startEvent");
public record EndEvent(string Id) : BpmnElement(Id, "endEvent");
public record ServiceTask(string Id, string ImplementationType) : BpmnElement(Id, "serviceTask");
public record ScriptTask(string Id, string Script) : BpmnElement(Id, "scriptTask");
public record ExclusiveGateway(string Id) : BpmnElement(Id, "exclusiveGateway");
public record ParallelGateway(string Id) : BpmnElement(Id, "parallelGateway");

public record SequenceFlow(string Id, string SourceRef, string TargetRef, string? ConditionExpression = null);
