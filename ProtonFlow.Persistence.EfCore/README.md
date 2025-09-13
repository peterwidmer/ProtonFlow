# ProtonFlow.Persistence.EfCore

Entity Framework Core based persistence provider for the **ProtonFlow BPMN Engine**.

This package provides durable storage capabilities that extend the core `ProtonFlow.BpmnEngine` with:

- **Process Definitions**: Versioned deployments with change tracking and content hashing
- **Process Instances**: Complete lifecycle management with variables, tokens, and status tracking
- **Step Execution History**: Detailed per-element execution metrics and timing data
- **KPI Aggregation**: Dynamic analytics queries for performance monitoring and reporting
- **Instance Notes**: Lightweight audit trail and annotation system
- **Job Scheduling**: Background job processing with worker coordination and retry logic

The core `ProtonFlow.BpmnEngine` remains storage-agnostic, allowing you to use the in-memory provider for development and testing while seamlessly upgrading to Entity Framework Core for production deployments.

## Installation

```bash
dotnet add package ProtonFlow.BpmnEngine
dotnet add package ProtonFlow.Persistence.EfCore
```

## Quick Start

### Basic Configuration

Configure the engine with Entity Framework Core persistence:

```csharp
using BpmnEngine.Engine;
using Microsoft.EntityFrameworkCore;
using ProtonFlow.Persistence.EfCore.Extensions;

var engine = BpmnEngineBuilder.Create()
    .UseEntityFramework(options => 
        options.UseSqlServer("Server=.;Database=ProtonFlow;Trusted_Connection=true"))
    .AddTaskHandler("rest-call", async ctx => {
        var url = ctx.GetVariable<string>("url");
        await CallRestApiAsync(url);
    })
    .AddTaskHandler("email-notification", async ctx => {
        var recipient = ctx.GetVariable<string>("email");
        await SendEmailAsync(recipient);
    })
    .Build();

// Ensure database is created (in production, use migrations)
using var scope = engine.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<ProtonFlowDbContext>();
await dbContext.Database.EnsureCreatedAsync();
```

### Supported Database Providers

The package supports any Entity Framework Core provider:

```csharp
// SQL Server
.UseEntityFramework(opt => 
    opt.UseSqlServer("Server=.;Database=ProtonFlow;Trusted_Connection=true"))

// PostgreSQL
.UseEntityFramework(opt => 
    opt.UseNpgsql("Host=localhost;Database=protonflow;Username=user;Password=pass"))

// SQLite (development/testing)
.UseEntityFramework(opt => 
    opt.UseSqlite("Data Source=protonflow.db"))

// In-Memory (unit testing)
.UseEntityFramework(opt => 
    opt.UseInMemoryDatabase("ProtonFlowTest"))
```

### Process Execution with Persistence

```csharp
// Define a BPMN process
var bpmnXml = """
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
  <process id="order-fulfillment" name="Order Fulfillment Process">
    <startEvent id="start" />
    <sequenceFlow id="f1" sourceRef="start" targetRef="validate" />
    
    <serviceTask id="validate" implementation="validate-order" />
    <sequenceFlow id="f2" sourceRef="validate" targetRef="split" />
    
    <parallelGateway id="split" />
    <sequenceFlow id="f3" sourceRef="split" targetRef="inventory" />
    <sequenceFlow id="f4" sourceRef="split" targetRef="payment" />
    
    <serviceTask id="inventory" implementation="reserve-inventory" />
    <serviceTask id="payment" implementation="process-payment" />
    
    <sequenceFlow id="f5" sourceRef="inventory" targetRef="join" />
    <sequenceFlow id="f6" sourceRef="payment" targetRef="join" />
    
    <parallelGateway id="join" />
    <sequenceFlow id="f7" sourceRef="join" targetRef="fulfill" />
    
    <serviceTask id="fulfill" implementation="fulfill-order" />
    <sequenceFlow id="f8" sourceRef="fulfill" targetRef="end" />
    
    <endEvent id="end" />
  </process>
</definitions>
""";

// Load and deploy the process
var definition = await engine.LoadBpmnXml(bpmnXml);

// Start a new process instance with business data
var instance = await engine.StartProcessAsync(definition.Key, new {
    orderId = "ORD-12345",
    customerId = "CUST-789",
    amount = 299.99m,
    items = new[] { "ITEM-001", "ITEM-002" }
});

// Process execution continues automatically via background jobs
// or can be stepped manually for testing
while (await engine.CanStepAsync(instance.Id))
{
    await engine.StepAsync(instance.Id);
}
```

## Switching Between Storage Providers

The engine supports seamless switching between storage providers:

### In-Memory (Development/Testing)
```csharp
var engine = BpmnEngineBuilder.Create()
    .UseInMemory()
    .AddTaskHandler("test-task", async ctx => { /* handler logic */ })
    .Build();
```

### Entity Framework Core (Production)
```csharp
var engine = BpmnEngineBuilder.Create()
    .UseEntityFramework(options => 
        options.UseSqlServer(connectionString))
    .AddTaskHandler("production-task", async ctx => { /* handler logic */ })
    .Build();
```

The same process definitions and handler code work with both providers, making it easy to develop locally and deploy to production.

## Advanced Storage Operations

The EF Core implementation provides the unified `IBpmnStorage` interface for advanced operations beyond the basic engine contracts:

```csharp
using var scope = engine.Services.CreateScope();
var storage = scope.ServiceProvider.GetRequiredService<IBpmnStorage>();

// Query running process instances
var runningInstances = await storage.QueryProcessInstancesAsync(new ProcessInstanceQuery
{
    ProcessKey = "order-fulfillment",
    Statuses = new[] { ProcessInstanceStatus.Running },
    StartedFromUtc = DateTime.UtcNow.AddDays(-7),
    Take = 50
});

// Get process execution history
var stepHistory = await storage.GetStepExecutionsAsync(instance.Id);

// Query performance metrics
var performanceKpis = await storage.QueryStepKpisAsync(new StepKpiQuery
{
    ProcessKey = "order-fulfillment",
    ElementId = "payment",
    FromUtc = DateTime.UtcNow.AddDays(-30),
    GroupBy = KpiGroupBy.ProcessAndElement
});

Console.WriteLine($"Average payment processing time: {performanceKpis[0].AvgDurationMs}ms");
```

## Job-Based Execution

The EF Core provider includes a job store for background processing:

```csharp
using var scope = engine.Services.CreateScope();
var jobStore = scope.ServiceProvider.GetRequiredService<IJobStore>();

// Simulate a background worker
const string workerId = "worker-001";
while (true)
{
    var job = await jobStore.ClaimNextJobAsync(workerId, TimeSpan.FromMinutes(5));
    if (job == null)
    {
        await Task.Delay(TimeSpan.FromSeconds(10)); // Poll interval
        continue;
    }

    try
    {
        // Process the job
        await engine.StepAsync(job.ProcessInstanceId);
        await jobStore.CompleteJobAsync(job.Id, workerId);
    }
    catch (Exception ex)
    {
        // Job will automatically retry based on lease expiration
        Console.WriteLine($"Job {job.Id} failed: {ex.Message}");
    }
}
```

## Performance Analytics and KPIs

### Dynamic KPI Aggregation

Query performance metrics across different dimensions:

```csharp
// Overall process performance
var processKpis = await storage.QueryStepKpisAsync(new StepKpiQuery
{
    ProcessKey = "order-fulfillment",
    GroupBy = KpiGroupBy.ProcessOnly,
    FromUtc = DateTime.UtcNow.AddDays(-30)
});

// Per-element performance
var elementKpis = await storage.QueryStepKpisAsync(new StepKpiQuery
{
    ProcessKey = "order-fulfillment",
    GroupBy = KpiGroupBy.ProcessAndElement,
    FromUtc = DateTime.UtcNow.AddDays(-7)
});

// Cross-process element comparison
var taskKpis = await storage.QueryStepKpisAsync(new StepKpiQuery
{
    ElementId = "payment-processing",
    GroupBy = KpiGroupBy.ElementOnly // Compare across all processes
});
```

### Instance Lifecycle Management

Track and manage process instance lifecycles:

```csharp
// Mark instance as cancelled with reason
await storage.MarkInstanceCancelledAsync(
    instanceId, 
    "Customer requested cancellation", 
    DateTime.UtcNow);

// Mark instance as failed with error details
await storage.MarkInstanceFailedAsync(
    instanceId, 
    "Payment gateway timeout", 
    DateTime.UtcNow);

// Add audit notes
await storage.AddInstanceNoteAsync(new InstanceNote
{
    InstanceId = instanceId,
    Type = "Audit",
    Message = "Manual intervention required - inventory shortage",
    CreatedUtc = DateTime.UtcNow
});
```

## Testing Strategies

### Unit Testing with In-Memory Database

```csharp
[TestMethod]
public async Task Should_Process_Order_Successfully()
{
    // Use EF Core in-memory database for isolated tests
    var engine = BpmnEngineBuilder.Create()
        .UseEntityFramework(options => 
            options.UseInMemoryDatabase($"test-{Guid.NewGuid()}"))
        .AddTaskHandler("validate-order", async ctx => {
            // Test implementation
            ctx.Instance.Variables["validated"] = true;
        })
        .Build();

    // Test process execution
    var definition = await engine.LoadBpmnXml(testBpmnXml);
    var instance = await engine.StartProcessAsync(definition.Key, testData);
    
    // Assert outcomes
    Assert.IsTrue(instance.Variables.ContainsKey("validated"));
}
```

### Integration Testing with SQLite

```csharp
[TestMethod]
public async Task Should_Persist_Process_State_Correctly()
{
    // Use SQLite in-memory for full EF Core integration testing
    var connection = new SqliteConnection("Filename=:memory:");
    await connection.OpenAsync();

    var engine = BpmnEngineBuilder.Create()
        .UseEntityFramework(options => options.UseSqlite(connection))
        .AddTaskHandler("test-task", async ctx => { /* implementation */ })
        .Build();

    // Ensure database schema is created
    using var scope = engine.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ProtonFlowDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    // Test with actual database operations
    var definition = await engine.LoadBpmnXml(bpmnXml);
    var instance = await engine.StartProcessAsync(definition.Key);
    
    // Verify persistence
    var storage = scope.ServiceProvider.GetRequiredService<IBpmnStorage>();
    var retrievedInstance = await storage.GetProcessInstanceByIdAsync(instance.Id);
    Assert.IsNotNull(retrievedInstance);
}
```

## Database Schema

The package creates the following tables:

- **ProcessDefinitions**: Versioned BPMN definitions with content hashing
- **ProcessInstances**: Runtime instance state with optimistic concurrency
- **StepExecutions**: Detailed execution history for analytics
- **InstanceNotes**: Audit trail and annotations
- **Jobs**: Background job queue with worker coordination

All tables include appropriate indexes for performance and support for both read and write operations.

## Migration Strategy

For production deployments, use Entity Framework migrations:

```bash
# Add migration
dotnet ef migrations add InitialCreate --project YourProject --context ProtonFlowDbContext

# Update database
dotnet ef database update --project YourProject --context ProtonFlowDbContext
```

## Extensibility and Future Roadmap

The current persistence model is designed to support future enhancements:

- **Timer Events**: Add TimerEventRecord table for scheduled execution
- **Message Subscriptions**: Support for inter-process communication
- **Audit Logging**: Structured event logging for compliance
- **Aggregated KPI Tables**: Pre-computed daily/hourly metrics for dashboards
- **Multi-tenancy**: Tenant isolation with composite indexes
- **External Correlations**: Enhanced business key correlation support

The schema and interfaces are intentionally extensible to accommodate these features without breaking changes.

## Performance Considerations

- **Indexing**: All query paths are covered by database indexes
- **Pagination**: Query operations support skip/take patterns
- **Concurrency**: Optimistic concurrency control prevents data conflicts
- **Connection Pooling**: Uses standard EF Core connection management
- **Bulk Operations**: Efficient batch processing for high-throughput scenarios

## License

MIT

## Repository

https://github.com/peterwidmer/ProtonFlow
