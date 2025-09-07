# ProtonFlow - BPMN Engine 

ProtonFlow allows the execution of BPMN 2.0 processes in .NET applications. It supports a subset of BPMN elements and provides an easy-to-use API for loading, starting, and simulating processes.

It is In-Memory only currently, but an interface is available for other stores implementations.

Please beware, that large parts of this package have been written by AI, with some manual refactoring to ensure architectural quality and making sure that tests were written for several use-cases.

## Quick start:

var engine = BpmnEngine.Engine.BpmnEngineBuilder.Create()
    .UseInMemory()
    .AddTaskHandler("rest-call", async ctx => {
        var url = ctx.GetVariable<string>("url");
        // call REST API...
        await Task.CompletedTask;
    })
    .Build();

var xml = File.ReadAllText("invoice-process.bpmn");
var process = await engine.LoadBpmnXml(xml);
var instance = await engine.StartProcessAsync(process.Key, new { amount = 500 });

var simulator = engine.Simulate(process.Key);
while (simulator.CanStep)
    await simulator.StepAsync();

var positions = await engine.GetCurrentTokenPositions(instance.Id);

## Supported BPMN Elements

The engine currently supports the following BPMN 2.0 elements:

- **Events**: `startEvent`, `endEvent`
- **Tasks**: `serviceTask`, `scriptTask` (as a no-op placeholder)
- **Gateways**: 
  - `exclusiveGateway` (with conditional and default flows)
  - `parallelGateway` (enables parallel execution paths)
- **Flows**: `sequenceFlow`