# ProtonFlow - BPMN Engine 

ProtonFlow is a lightweight BPMN 2.0 process engine for .NET applications that supports both in-memory and durable persistence via Entity Framework Core. It provides an easy-to-use API for loading, starting, and executing business processes with support for parallel execution, job scheduling, and process analytics.

The engine is designed to be storage-agnostic with a clean abstraction layer, making it suitable for both simple in-memory scenarios and production applications requiring durable persistence.

Please note that significant portions of this package have been developed with AI assistance, with careful manual refactoring to ensure architectural quality and comprehensive test coverage.

## Features

- **BPMN 2.0 Support**: Core process elements including events, tasks, gateways, and sequence flows
- **Dual Persistence**: Choose between in-memory (for testing/prototypes) or Entity Framework Core (for production)
- **Parallel Execution**: Full support for parallel gateways and concurrent token execution
- **Job Scheduling**: Background job processing for long-running or delayed operations
- **Process Analytics**: Built-in KPI aggregation and step execution tracking
- **Extensible Task Handlers**: Plugin architecture for custom task implementations
- **Simulation Mode**: Execute processes without side effects for testing and validation

## Quick Start

### Basic In-Memory Usage

```csharp
using BpmnEngine.Engine;

var engine = BpmnEngineBuilder.Create()
    .UseInMemory()
    .AddTaskHandler("rest-call", async ctx => {
        var url = ctx.GetVariable<string>("url");
        // Implement your REST API call logic here
        Console.WriteLine($"Calling REST API: {url}");
        await Task.CompletedTask;
    })
    .Build();

// Load a BPMN process definition
var xml = File.ReadAllText("invoice-process.bpmn");
var process = await engine.LoadBpmnXml(xml);

// Start a new process instance with variables
var instance = await engine.StartProcessAsync(process.Key, new { 
    amount = 500, 
    customerName = "Acme Corp" 
});

// Execute the process step by step
while (await engine.CanStepAsync(instance.Id))
{
    await engine.StepAsync(instance.Id);
}

// Check current token positions
var positions = await engine.GetCurrentTokenPositions(instance.Id);
```

### Simulation Mode

Use simulation mode to validate process logic without executing actual task handlers:

```csharp
var engine = BpmnEngineBuilder.Create()
    .UseInMemory()
    .AddTaskHandler("rest-call", async ctx => {
        // This handler won't be called in simulation mode
        await SomeExpensiveOperation();
    })
    .Build();

var process = await engine.LoadBpmnXml(xml);
var simulator = engine.Simulate(process.Key);

// Execute the entire process in simulation
while (simulator.CanStep)
    await simulator.StepAsync();
```

### Production Setup with Entity Framework Core

For production scenarios, use the EF Core persistence package:

```bash
dotnet add package ProtonFlow.BpmnEngine
dotnet add package ProtonFlow.Persistence.EfCore
```

```csharp
using BpmnEngine.Engine;
using ProtonFlow.Persistence.EfCore.Extensions;
using Microsoft.EntityFrameworkCore;

var engine = BpmnEngineBuilder.Create()
    .UseEntityFramework(options => 
        options.UseSqlServer("Server=.;Database=ProtonFlow;Trusted_Connection=true"))
    .AddTaskHandler("email-task", async ctx => {
        var recipient = ctx.GetVariable<string>("recipient");
        await SendEmailAsync(recipient);
    })
    .AddTaskHandler("approval-task", async ctx => {
        var amount = ctx.GetVariable<decimal>("amount");
        await RequestApprovalAsync(amount);
    })
    .Build();

// The engine now supports durable persistence, job scheduling,
// and process analytics out of the box
```

## Supported BPMN Elements

The engine currently supports the following BPMN 2.0 elements:

### Events
- **Start Event** (`startEvent`): Process entry points
- **End Event** (`endEvent`): Process completion points

### Tasks
- **Service Task** (`serviceTask`): Automated tasks with custom handlers
- **Script Task** (`scriptTask`): Currently implemented as no-op placeholder

### Gateways
- **Exclusive Gateway** (`exclusiveGateway`): Conditional routing with support for:
  - Conditional sequence flows with expressions
  - Default flows for fallback routing
- **Parallel Gateway** (`parallelGateway`): Enables concurrent execution paths
  - Fork: Creates multiple parallel tokens
  - Join: Waits for all tokens to arrive before proceeding

### Flow Elements
- **Sequence Flow** (`sequenceFlow`): Connects process elements with optional conditions

## Task Handler Development

Implement custom task handlers by providing a function that receives a `TaskContext`:

```csharp
.AddTaskHandler("my-task-type", async ctx => {
    // Access process variables
    var orderId = ctx.GetVariable<string>("orderId");
    var amount = ctx.GetVariable<decimal>("amount");
    
    // Access current element information
    var elementId = ctx.ElementId;
    var instance = ctx.Instance;
    
    // Perform your business logic
    await ProcessOrderAsync(orderId, amount);
    
    // Variables can be modified through the instance
    ctx.Instance.Variables["processedAt"] = DateTime.UtcNow;
})
```

## Process Analytics and Monitoring

When using Entity Framework Core persistence, the engine automatically tracks:

- **Process Definitions**: Versioned deployments with change tracking
- **Process Instances**: Complete lifecycle status and variables
- **Step Execution History**: Per-element timing and performance metrics
- **KPI Aggregation**: Dynamic queries for duration analysis and throughput
- **Instance Notes**: Lightweight audit trail and annotations

## Architecture

ProtonFlow follows a layered architecture:

- **Core Engine** (`ProtonFlow.BpmnEngine`): Storage-agnostic process execution
- **Persistence Layer** (`ProtonFlow.Persistence.EfCore`): Entity Framework Core integration
- **Extensibility Points**: Task handlers, event handlers, and custom storage providers

The engine uses dependency injection throughout, making it easy to integrate with existing .NET applications and test frameworks.

## Testing

The engine is designed with testing in mind:

```csharp
// Use in-memory persistence for unit tests
var engine = BpmnEngineBuilder.Create()
    .UseInMemory()
    .Build();

// Or use SQLite in-memory for integration tests with EF Core
var connection = new SqliteConnection("Filename=:memory:");
connection.Open();

var engine = BpmnEngineBuilder.Create()
    .UseEntityFramework(options => options.UseSqlite(connection))
    .Build();
```

## Example BPMN Process

```xml
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
  <process id="order-process" name="Order Processing">
    <startEvent id="start" />
    <sequenceFlow id="flow1" sourceRef="start" targetRef="validate" />
    
    <serviceTask id="validate" implementation="validate-order" />
    <sequenceFlow id="flow2" sourceRef="validate" targetRef="gateway" />
    
    <exclusiveGateway id="gateway" />
    <sequenceFlow id="flow3" sourceRef="gateway" targetRef="approve">
      <conditionExpression>amount > 1000</conditionExpression>
    </sequenceFlow>
    <sequenceFlow id="flow4" sourceRef="gateway" targetRef="process" />
    
    <serviceTask id="approve" implementation="approval-task" />
    <sequenceFlow id="flow5" sourceRef="approve" targetRef="process" />
    
    <serviceTask id="process" implementation="process-order" />
    <sequenceFlow id="flow6" sourceRef="process" targetRef="end" />
    
    <endEvent id="end" />
  </process>
</definitions>
```

## Roadmap

Future enhancements being considered:

- **Timer Events**: Scheduled and timeout-based execution
- **Message Events**: Inter-process communication and correlation
- **User Tasks**: Human workflow integration
- **Error Handling**: Boundary events and compensation
- **Multi-tenancy**: Tenant isolation and security
- **Advanced Analytics**: Real-time dashboards and reporting

## License

MIT

## Repository

https://github.com/peterwidmer/ProtonFlow