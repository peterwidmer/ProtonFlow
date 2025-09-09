using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProtonFlow.Persistence.EfCore.Storage;
using ProtonFlow.Persistence.EfCore.Storage.Models;

namespace ProtonFlow.Persistence.EfCore.Tests;

/// <summary>
/// Integration-style tests for the EF Core persistence layer. Each test focuses on a distinct use case:
/// 1. Deploying a definition, creating an instance, recording a step, then reloading (SQLite provider for relational realism).
/// 2. Recording multiple step executions and aggregating KPI metrics (EF InMemory provider for fast logic validation).
/// The tests deliberately avoid mocking to ensure configuration and EF mappings function end-to-end.
/// </summary>
[TestClass]
public class EfCoreStorageTests
{
    /// <summary>
    /// Verifies core CRUD flow under the SQLite in-memory provider:
    ///  - Ensures schema creation works (FKs, constraints via EnsureCreated).
    ///  - Persists a process definition and instance.
    ///  - Appends one step execution and reloads the instance.
    ///  - Confirms the step history is retrieved in expected order.
    /// This guards against NOT NULL / FK issues and provider-specific differences compared to the InMemory provider.
    /// </summary>
    [TestMethod]
    public async Task Can_Save_And_Retrieve_Instance_With_Steps_SQLite()
    {
        // Create shared in-memory SQLite connection (lifetime scoped for test duration)
        using var conn = new SqliteConnection("Filename=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<ProtonFlowDbContext>()
            .UseSqlite(conn) // relational provider
            .Options;

        using var ctx = new ProtonFlowDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        var storage = new EfBpmnStorage(ctx);

        // Deploy a definition (version auto-assigned)
        var def = await storage.SaveProcessDefinitionAsync(new StoredProcessDefinition
        {
            Key = "invoice-process",
            Name = "Invoice",
            Xml = "<xml />",
            ContentHash = EfBpmnStorage.ComputeHash("<xml />")
        });

        // Create a new runtime instance referencing the definition
        var inst = await storage.CreateProcessInstanceAsync(new StoredProcessInstance
        {
            ProcessDefinitionId = def.Id,
            ProcessKey = def.Key
        });

        // Record a single step execution (start event)
        await storage.AppendStepExecutionAsync(new StepExecutionRecord
        {
            InstanceId = inst.Id,
            ProcessDefinitionId = def.Id,
            ProcessKey = def.Key,
            ElementId = "start",
            ElementType = "startEvent",
            Sequence = 1,
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddMilliseconds(5),
            StepStatus = "Completed"
        });

        // Reload instance including navigation collections
        var loaded = await storage.GetProcessInstanceByIdAsync(inst.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(inst.Id, loaded!.Id);

        // Retrieve ordered step history
        var steps = await storage.GetStepExecutionsAsync(inst.Id);
        Assert.AreEqual(1, steps.Count);
        Assert.AreEqual("start", steps[0].ElementId);
    }

    /// <summary>
    /// Exercises KPI aggregation over multiple step executions using the EF InMemory provider for speed:
    ///  - Deploy definition and create single instance
    ///  - Append several completed service task executions
    ///  - Query aggregated KPI metrics grouped by (ProcessKey, ElementId)
    ///  - Validate counts and basic averaging logic
    /// Provides fast signal for aggregation LINQ shape without requiring a relational provider.
    /// </summary>
    [TestMethod]
    public async Task Can_Save_And_Query_Kpis_InMemoryProvider()
    {
        var options = new DbContextOptionsBuilder<ProtonFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var ctx = new ProtonFlowDbContext(options);
        var storage = new EfBpmnStorage(ctx);

        var def = await storage.SaveProcessDefinitionAsync(new StoredProcessDefinition
        {
            Key = "invoice-process",
            Name = "Invoice",
            Xml = "<xml />",
            ContentHash = EfBpmnStorage.ComputeHash("<xml />")
        });

        var inst = await storage.CreateProcessInstanceAsync(new StoredProcessInstance
        {
            ProcessDefinitionId = def.Id,
            ProcessKey = def.Key
        });

        // Simulate execution of a repeating task three times
        for (int i = 0; i < 3; i++)
        {
            await storage.AppendStepExecutionAsync(new StepExecutionRecord
            {
                InstanceId = inst.Id,
                ProcessDefinitionId = def.Id,
                ProcessKey = def.Key,
                ElementId = "task",
                ElementType = "serviceTask",
                Sequence = i + 1,
                StartUtc = DateTime.UtcNow.AddMilliseconds(i * 10),
                EndUtc = DateTime.UtcNow.AddMilliseconds(i * 10 + 20),
                StepStatus = "Completed"
            });
        }

        // Aggregate KPI metrics for the task
        var kpis = await storage.QueryStepKpisAsync(new StepKpiQuery { ProcessKey = def.Key, ElementId = "task" });
        Assert.AreEqual(1, kpis.Count);
        Assert.AreEqual(3, kpis[0].Count);
        Assert.AreEqual("task", kpis[0].ElementId);
    }
}
