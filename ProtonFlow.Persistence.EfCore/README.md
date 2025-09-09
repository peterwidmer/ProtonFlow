# ProtonFlow.Persistence.EfCore

Entity Framework Core based persistence provider for the **ProtonFlow BPMN Engine**.

This package adds durable storage for:
- Process Definitions (versioned deployments)
- Process Instances (variables, tokens, lifecycle status)
- Step Execution History (per element metrics)
- KPI Aggregation (dynamic query of durations, counts)
- Instance Notes (lightweight audit / annotations)

The core `ProtonFlow.BpmnEngine` remains storage-agnostic; you can still use the in?memory provider for tests or prototypes. Add this package when you need durability or analytics.

## Quick Start

1. Install packages:
```
dotnet add package ProtonFlow.BpmnEngine

dotnet add package ProtonFlow.Persistence.EfCore
```

2. Configure the engine with EF Core:
```csharp
using BpmnEngine.Engine;
using Microsoft.EntityFrameworkCore;
using ProtonFlow.Persistence.EfCore.Extensions; // UseEntityFramework

var engine = BpmnEngineBuilder.Create()
    .UseEntityFramework(opt => opt.UseSqlite("Data Source=protonflow.db"))
    .AddTaskHandler("rest-call", async ctx => { /* your handler */ })
    .Build();
```

3. Deploy and run a process:
```csharp
var xml = """
<?xml version="1.0"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
  <process id="demo" name="Demo Process">
    <startEvent id="start" />
    <sequenceFlow id="f1" sourceRef="start" targetRef="task" />
    <serviceTask id="task" implementation="rest-call" />
    <sequenceFlow id="f2" sourceRef="task" targetRef="end" />
    <endEvent id="end" />
  </process>
</definitions>
""";

var def = await engine.LoadBpmnXml(xml);
var instance = await engine.StartProcessAsync(def.Key, new { amount = 500 });
while (await engine.CanStepAsync(instance.Id))
{
    await engine.StepAsync(instance.Id);
}
```

4. Query active token positions:
```csharp
var positions = await engine.GetCurrentTokenPositions(instance.Id);
```

## Switching Between InMemory and EF Core
- InMemory (no persistence):
```csharp
var engine = BpmnEngineBuilder.Create()
    .UseInMemory()
    .Build();
```
- EF Core (durable):
```csharp
var engine = BpmnEngineBuilder.Create()
    .UseEntityFramework(o => o.UseSqlite("Data Source=protonflow.db"))
    .Build();
```
You can register different providers (SqlServer, PostgreSQL, InMemory) by changing the `UseXxx` call.

## Unified Storage Abstraction
The EF implementation provides `IBpmnStorage` for richer operations (definitions, instances, steps, KPIs, status changes, notes). Engine compatibility is maintained through adapters implementing existing `IProcessStore` and `IInstanceStore`.

Key query objects:
```csharp
var instances = await storage.QueryProcessInstancesAsync(new ProcessInstanceQuery
{
    ProcessKey = "demo",
    Statuses = new[] { ProcessInstanceStatus.Running },
    Take = 50
});

var kpis = await storage.QueryStepKpisAsync(new StepKpiQuery
{
    ProcessKey = "demo",
    GroupBy = KpiGroupBy.ProcessAndElement
});
```

## KPI Aggregation
`QueryStepKpisAsync` dynamically aggregates over completed step executions (count, min, max, average duration) grouped by process, element, or both.

## Status Transitions
Use:
```csharp
await storage.MarkInstanceCancelledAsync(instanceId, "User cancelled", DateTime.UtcNow);
await storage.MarkInstanceFailedAsync(instanceId, "Timeout", DateTime.UtcNow);
```
Lifecycle timestamps (Started / Completed / Cancelled / Failed) are tracked for audit and SLA metrics.

## Instance Notes
Add ad-hoc annotations:
```csharp
await storage.AddInstanceNoteAsync(new InstanceNote
{
    InstanceId = instanceId,
    Type = "Audit",
    Message = "Manual approval granted"
});
```

## Testing
All components are designed for unit & integration tests:
- Use SQLite in-memory for relational fidelity:
```csharp
var conn = new SqliteConnection("Filename=:memory:");
await conn.OpenAsync();
var options = new DbContextOptionsBuilder<ProtonFlowDbContext>()
    .UseSqlite(conn)
    .Options;
```
- Use EF Core InMemory provider for lightweight logic tests.

## Extensibility Roadmap
Future additions can introduce:
- Timer events (add TimerEventRecord table)
- Message subscriptions / correlations
- Audit log table (structured events)
- Aggregated daily/hourly KPI snapshot tables
- Multi-tenancy (add TenantId + composite indexes)

The current schema & interfaces are intentionally open for these without breaking changes.

## License
MIT

## Repository
https://github.com/peterwidmer/ProtonFlow
