namespace BpmnEngine.Engine;

using BpmnEngine.Interfaces;
using BpmnEngine.Models;
using BpmnEngine.Runtime;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/*
Pseudocode (refactor plan)
- Keep StartAsync and CanStep unchanged.
- Split StepAsync into small, focused helpers:
  - GetProcessElement(xdoc): XElement -> locate BPMN <process>.
  - ProcessActiveTokensAsync(instance, xproc, ct): HashSet<string>
    - newTokens = empty set
    - For each token in snapshot(instance.ActiveTokens):
      - current = FindElementById(xproc, token); if null -> continue
      - if IsEndEvent(current) -> continue (consume token)
      - if not SimulationMode -> ExecuteTaskIfNeededAsync(instance, current, token, ct)
      - outgoing = FindOutgoingFlows(xproc, token); if none -> continue
      - foreach target in ResolveTargets(outgoing) -> newTokens.Add(target)
    - return newTokens
  - FindElementById(xproc, id): XElement?
  - FindOutgoingFlows(xproc, sourceRef): List<XElement>
  - ResolveTargets(flows): IEnumerable<string>
  - ExecuteTaskIfNeededAsync(instance, current, token, ct): await handler if serviceTask; noop for scriptTask.
  - ReplaceActiveTokens(instance, newTokens): clear and add.
  - UpdateCompletion(instance, def): set IsCompleted if no tokens or all at EndEvent.
- StepAsync flow:
  - Load definition; parse XML; get process element.
  - newTokens = await ProcessActiveTokensAsync(...)
  - ReplaceActiveTokens(...)
  - UpdateCompletion(...)
*/

public class SimpleProcessExecutor : IProcessExecutor
{
    private readonly IProcessStore _processStore;
    private readonly IServiceProvider _services;

    public SimpleProcessExecutor(IProcessStore processStore, IServiceProvider services)
    {
        _processStore = processStore;
        _services = services;
    }

    public async Task<ProcessInstance> StartAsync(ProcessDefinition definition, object? variables = null, CancellationToken ct = default)
    {
        var instance = new ProcessInstance { ProcessDefinitionId = definition.Id, ProcessKey = definition.Key };
        if (variables != null)
        {
            foreach (var prop in variables.GetType().GetProperties())
            {
                instance.Variables[prop.Name] = prop.GetValue(variables);
            }
        }

        // Place token at the start event(s)
        foreach (var el in definition.Elements.Values.OfType<StartEvent>())
        {
            instance.ActiveTokens.Add(el.Id);
        }

        return instance;
    }

    public bool CanStep(ProcessInstance instance)
    {
        return !instance.IsCompleted && instance.ActiveTokens.Count > 0;
    }

    public async Task StepAsync(ProcessInstance instance, CancellationToken ct = default)
    {
        var processDefinition = await _processStore.GetByIdAsync(instance.ProcessDefinitionId, ct)
                  ?? throw new InvalidOperationException("Process definition not found");

        var processXml = XDocument.Parse(processDefinition.Xml);
        var processElements = GetProcessElement(processXml);

        var newTokens = await ProcessActiveTokensAsync(instance, processElements, ct);

        ReplaceActiveTokens(instance, newTokens);

        UpdateCompletion(instance, processDefinition);
    }

    private static XElement GetProcessElement(XDocument xdoc)
    {
        return xdoc.Root!.Descendants().First(e => e.Name.LocalName == "process");
    }

    private async Task<HashSet<string>> ProcessActiveTokensAsync(ProcessInstance instance, XElement xproc, CancellationToken ct)
    {
        var newTokens = new HashSet<string>();

        foreach (var token in instance.ActiveTokens.ToArray())
        {
            ct.ThrowIfCancellationRequested();

            var current = FindElementById(xproc, token);
            if (current == null)
                continue;

            if (IsEndEvent(current))
            {
                // Token consumed at an end event; do not create new tokens.
                continue;
            }

            if (!instance.SimulationMode)
            {
                await ExecuteTaskIfNeededAsync(instance, current, token, ct);
            }

            var outgoing = FindOutgoingFlows(xproc, token);
            if (outgoing.Count == 0)
            {
                // Dead end -> no outgoing flows; token is consumed.
                continue;
            }

            if (IsExclusiveGateway(current))
            {
                foreach (var target in ResolveExclusiveGatewayTargets(current, outgoing, instance))
                {
                    AddTargetWithParallelJoinAccounting(newTokens, xproc, target, instance);
                }
            }
            else if (IsParallelGateway(current))
            {
                var incomingCount = FindIncomingFlows(xproc, token).Count;
                if (outgoing.Count > 1 && incomingCount <= 1)
                {
                    // Parallel split: produce tokens for all outgoing
                    foreach (var t in ResolveTargets(outgoing))
                    {
                        AddTargetWithParallelJoinAccounting(newTokens, xproc, t, instance);
                    }
                }
                else if (incomingCount > 1)
                {
                    // Parallel join: wait for all incoming branches
                    var arrived = instance.ParallelJoinWaits.TryGetValue(token, out var cnt) ? cnt : 0;
                    if (arrived >= incomingCount)
                    {
                        // All required branches have arrived; consume them and move forward
                        instance.ParallelJoinWaits[token] = arrived - incomingCount;
                        foreach (var t in ResolveTargets(outgoing))
                        {
                            AddTargetWithParallelJoinAccounting(newTokens, xproc, t, instance);
                        }
                    }
                    else
                    {
                        // Keep waiting token at the join
                        newTokens.Add(token);
                    }
                }
                else
                {
                    // Degenerate gateway: pass-through
                    foreach (var t in ResolveTargets(outgoing))
                    {
                        AddTargetWithParallelJoinAccounting(newTokens, xproc, t, instance);
                    }
                }
            }
            else
            {
                foreach (var target in ResolveTargets(outgoing))
                {
                    AddTargetWithParallelJoinAccounting(newTokens, xproc, target, instance);
                }
            }
        }

        return newTokens;
    }

    private static XElement? FindElementById(XElement xproc, string id)
    {
        return xproc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == id);
    }

    private static bool IsEndEvent(XElement element)
    {
        return element.Name.LocalName == "endEvent";
    }

    private static bool IsExclusiveGateway(XElement element)
    {
        return element.Name.LocalName == "exclusiveGateway";
    }

    private static bool IsParallelGateway(XElement element)
    {
        return element.Name.LocalName == "parallelGateway";
    }

    private async Task ExecuteTaskIfNeededAsync(ProcessInstance instance, XElement current, string token, CancellationToken ct)
    {
        var localName = current.Name.LocalName;

        if (localName == "serviceTask")
        {
            var type = current.Attribute("implementation")?.Value ?? current.Attribute("type")?.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(type))
            {
                var handlers = (IEnumerable<ITaskHandler>?)_services.GetService(typeof(IEnumerable<ITaskHandler>));
                var handler = handlers?.FirstOrDefault(h => string.Equals(h.Type, type, StringComparison.OrdinalIgnoreCase));
                if (handler != null)
                {
                    var ctx = new TaskContext(instance, elementId: token);
                    await handler.ExecuteAsync(ctx, ct);
                }
            }
        }
        else if (localName == "scriptTask")
        {
            // No-op placeholder for script execution
        }
    }

    private static List<XElement> FindOutgoingFlows(XElement xproc, string sourceRef)
    {
        return xproc
            .Descendants()
            .Where(e => e.Name.LocalName == "sequenceFlow" && e.Attribute("sourceRef")?.Value == sourceRef)
            .ToList();
    }

    private static List<XElement> FindIncomingFlows(XElement xproc, string targetRef)
    {
        return xproc
            .Descendants()
            .Where(e => e.Name.LocalName == "sequenceFlow" && e.Attribute("targetRef")?.Value == targetRef)
            .ToList();
    }

    private static IEnumerable<string> ResolveTargets(IEnumerable<XElement> flows)
    {
        return flows
            .Select(f => f.Attribute("targetRef")?.Value)
            .Where(t => !string.IsNullOrWhiteSpace(t))!
            .Select(t => t!);
    }

    private static IEnumerable<string> ResolveExclusiveGatewayTargets(XElement gateway, List<XElement> outgoing, ProcessInstance instance)
    {
        // Evaluate conditional flows in document order; pick first true.
        foreach (var flow in outgoing)
        {
            if (HasCondition(flow) && EvaluateCondition(flow, instance))
            {
                var target = flow.Attribute("targetRef")?.Value;
                if (!string.IsNullOrWhiteSpace(target))
                    yield return target!;
                yield break;
            }
        }

        // If no conditions matched, try default flow
        var defaultFlowId = gateway.Attribute("default")?.Value;
        if (!string.IsNullOrWhiteSpace(defaultFlowId))
        {
            var def = outgoing.FirstOrDefault(f => f.Attribute("id")?.Value == defaultFlowId);
            var target = def?.Attribute("targetRef")?.Value;
            if (!string.IsNullOrWhiteSpace(target))
                yield return target!;
        }
        // If neither matched, no token is produced (dead end)
    }

    private static bool HasCondition(XElement flow)
    {
        return flow.Descendants().Any(e => e.Name.LocalName == "conditionExpression");
    }

    private static bool EvaluateCondition(XElement flow, ProcessInstance instance)
    {
        var condEl = flow.Descendants().FirstOrDefault(e => e.Name.LocalName == "conditionExpression");
        if (condEl == null)
            return false;

        var expr = condEl.Value?.Trim() ?? string.Empty;
        if (expr.StartsWith("${") && expr.EndsWith("}"))
        {
            expr = expr.Substring(2, expr.Length - 3).Trim();
        }

        // Support simple expressions: variable OP number
        var m = Regex.Match(expr, @"^\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*(==|!=|>=|<=|>|<)\s*([-+]?[0-9]+(?:\.[0-9]+)?)\s*$");
        if (!m.Success)
            return false;

        var varName = m.Groups[1].Value;
        var op = m.Groups[2].Value;
        var rightText = m.Groups[3].Value;

        if (!instance.Variables.TryGetValue(varName, out var leftObj) || leftObj is null)
            return false;

        if (!double.TryParse(rightText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var right))
            return false;

        double left;
        try
        {
            if (leftObj is IConvertible)
            {
                left = Convert.ToDouble(leftObj, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (leftObj is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tmp))
            {
                left = tmp;
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        return op switch
        {
            ">" => left > right,
            ">=" => left >= right,
            "<" => left < right,
            "<=" => left <= right,
            "==" => Math.Abs(left - right) < double.Epsilon,
            "!=" => Math.Abs(left - right) > double.Epsilon,
            _ => false
        };
    }

    private static void AddTargetWithParallelJoinAccounting(HashSet<string> newTokens, XElement xproc, string targetId, ProcessInstance instance)
    {
        var targetEl = FindElementById(xproc, targetId);
        if (targetEl != null && IsParallelGateway(targetEl))
        {
            var incoming = FindIncomingFlows(xproc, targetId).Count;
            if (incoming > 1)
            {
                // We're flowing into a parallel join; increment arrival counter
                instance.ParallelJoinWaits[targetId] = (instance.ParallelJoinWaits.TryGetValue(targetId, out var cnt) ? cnt : 0) + 1;
            }
        }
        newTokens.Add(targetId);
    }

    private static void ReplaceActiveTokens(ProcessInstance instance, HashSet<string> newTokens)
    {
        instance.ActiveTokens.Clear();
        foreach (var t in newTokens)
        {
            instance.ActiveTokens.Add(t);
        }
    }

    private static void UpdateCompletion(ProcessInstance instance, ProcessDefinition def)
    {
        if (instance.ActiveTokens.Count == 0 ||
            instance.ActiveTokens.All(id => def.Elements.TryGetValue(id, out var el) && el is EndEvent))
        {
            instance.IsCompleted = true;
        }
    }
}
