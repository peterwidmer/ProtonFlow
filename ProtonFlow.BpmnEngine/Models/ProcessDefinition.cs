using System.Collections.Generic;

namespace BpmnEngine.Models;

/// <summary>
/// Represents a deployed BPMN process definition containing the parsed elements and metadata.
/// Immutable once created to ensure process execution consistency.
/// </summary>
public class ProcessDefinition
{
    /// <summary>
    /// Unique identifier for this specific process definition deployment.
    /// </summary>
    public string Id { get; private set; }
    
    /// <summary>
    /// Business key identifying the process type (corresponds to BPMN process id attribute).
    /// Multiple versions of the same process will share this key.
    /// </summary>
    public string Key { get; private set; }
    
    /// <summary>
    /// Human-readable name of the process (from BPMN process name attribute).
    /// </summary>
    public string Name { get; private set; }
    
    /// <summary>
    /// Original BPMN XML source used to create this definition.
    /// </summary>
    public string Xml { get; private set; }
    
    /// <summary>
    /// Dictionary of parsed BPMN elements indexed by their element IDs.
    /// Includes all startEvent, endEvent, task, and gateway elements.
    /// </summary>
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

/// <summary>
/// Base class for all BPMN process elements (events, tasks, gateways).
/// Provides common identification and type information.
/// </summary>
public abstract class BpmnElement
{
    /// <summary>
    /// Unique identifier of this element within the process definition.
    /// Corresponds to the 'id' attribute in BPMN XML.
    /// </summary>
    public string Id { get; private set; }
    
    /// <summary>
    /// BPMN element type name (e.g., "startEvent", "serviceTask", "exclusiveGateway").
    /// </summary>
    public string Type { get; private set; }

    protected BpmnElement(string id, string type)
    {
        Id = id;
        Type = type;
    }
}

/// <summary>
/// Represents a BPMN start event that initiates process execution.
/// When a process instance is started, an initial token is placed on all start events.
/// </summary>
public class StartEvent : BpmnElement
{
    public StartEvent(string id) : base(id, "startEvent") { }
}

/// <summary>
/// Represents a BPMN end event that terminates a process execution path.
/// When a token reaches an end event, that token is consumed and the path completes.
/// The process instance completes when all tokens reach end events.
/// </summary>
public class EndEvent : BpmnElement
{
    public EndEvent(string id) : base(id, "endEvent") { }
}

/// <summary>
/// Represents a BPMN service task that executes automated business logic.
/// Service tasks are handled by registered task handlers based on their implementation type.
/// </summary>
public class ServiceTask : BpmnElement
{
    /// <summary>
    /// Implementation type identifier used to resolve the appropriate task handler.
    /// Corresponds to the 'implementation' or 'type' attribute in BPMN XML.
    /// </summary>
    public string ImplementationType { get; private set; }

    public ServiceTask(string id, string implementationType) : base(id, "serviceTask")
    {
        ImplementationType = implementationType;
    }
}

/// <summary>
/// Represents a BPMN script task containing inline script code.
/// Currently implemented as a no-op placeholder for future script execution capabilities.
/// </summary>
public class ScriptTask : BpmnElement
{
    /// <summary>
    /// Script content to be executed (currently unused in the engine).
    /// </summary>
    public string Script { get; private set; }

    public ScriptTask(string id, string script) : base(id, "scriptTask")
    {
        Script = script;
    }
}

/// <summary>
/// Represents a BPMN exclusive gateway that provides conditional routing.
/// Only one outgoing path is taken based on sequence flow conditions.
/// Supports default flows when no conditions are met.
/// </summary>
public class ExclusiveGateway : BpmnElement
{
    public ExclusiveGateway(string id) : base(id, "exclusiveGateway") { }
}

/// <summary>
/// Represents a BPMN parallel gateway that enables concurrent execution.
/// As a fork: Creates multiple tokens for parallel execution paths.
/// As a join: Waits for all incoming tokens before proceeding.
/// </summary>
public class ParallelGateway : BpmnElement
{
    public ParallelGateway(string id) : base(id, "parallelGateway") { }
}

/// <summary>
/// Represents a BPMN sequence flow connecting two process elements.
/// Defines the execution path and optional conditions for token movement.
/// </summary>
public class SequenceFlow
{
    /// <summary>
    /// Unique identifier of this sequence flow.
    /// </summary>
    public string Id { get; private set; }
    
    /// <summary>
    /// Element ID that this flow originates from.
    /// </summary>
    public string SourceRef { get; private set; }
    
    /// <summary>
    /// Element ID that this flow targets.
    /// </summary>
    public string TargetRef { get; private set; }
    
    /// <summary>
    /// Optional condition expression that must evaluate to true for the flow to be taken.
    /// Used with exclusive gateways for conditional routing.
    /// Note: C# 7.3 doesn't support 'string?' for nullable reference types
    /// without enabling C# 8 features. Using 'string' and relying on null.
    /// </summary>
    public string ConditionExpression { get; private set; }

    public SequenceFlow(string id, string sourceRef, string targetRef, string conditionExpression = null)
    {
        Id = id;
        SourceRef = sourceRef;
        TargetRef = targetRef;
        ConditionExpression = conditionExpression;
    }
}