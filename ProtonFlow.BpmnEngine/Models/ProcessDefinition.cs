using System.Collections.Generic;

namespace BpmnEngine.Models;

public class ProcessDefinition
{
    public string Id { get; private set; }
    public string Key { get; private set; }
    public string Name { get; private set; }
    public string Xml { get; private set; }
    public IReadOnlyDictionary<string, BpmnElement> Elements { get; private set; }

    public ProcessDefinition(
        string id,
        string key,
        string name,
        string xml,
        IReadOnlyDictionary<string, BpmnElement> elements)
    {
        Id = id;
        Key = key;
        Name = name;
        Xml = xml;
        Elements = elements;
    }
}

public abstract class BpmnElement
{
    public string Id { get; private set; }
    public string Type { get; private set; }

    protected BpmnElement(string id, string type)
    {
        Id = id;
        Type = type;
    }
}

public class StartEvent : BpmnElement
{
    public StartEvent(string id) : base(id, "startEvent") { }
}

public class EndEvent : BpmnElement
{
    public EndEvent(string id) : base(id, "endEvent") { }
}

public class ServiceTask : BpmnElement
{
    public string ImplementationType { get; private set; }

    public ServiceTask(string id, string implementationType) : base(id, "serviceTask")
    {
        ImplementationType = implementationType;
    }
}

public class ScriptTask : BpmnElement
{
    public string Script { get; private set; }

    public ScriptTask(string id, string script) : base(id, "scriptTask")
    {
        Script = script;
    }
}

public class ExclusiveGateway : BpmnElement
{
    public ExclusiveGateway(string id) : base(id, "exclusiveGateway") { }
}

public class ParallelGateway : BpmnElement
{
    public ParallelGateway(string id) : base(id, "parallelGateway") { }
}

public class SequenceFlow
{
    public string Id { get; private set; }
    public string SourceRef { get; private set; }
    public string TargetRef { get; private set; }
    // Note: C# 7.3 doesn't support 'string?' for nullable reference types
    // without enabling C# 8 features. Using 'string' and relying on null.
    public string ConditionExpression { get; private set; }

    public SequenceFlow(string id, string sourceRef, string targetRef, string conditionExpression = null)
    {
        Id = id;
        SourceRef = sourceRef;
        TargetRef = targetRef;
        ConditionExpression = conditionExpression;
    }
}