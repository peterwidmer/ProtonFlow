using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BpmnEngine.Engine;
using BpmnEngine.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtonFlow.Persistence.EfCore.Extensions;
using ProtonFlow.Persistence.EfCore.Storage;

namespace ProtonFlow.Persistence.EfCore.Tests;

[TestClass]
public class EfCoreEndToEndTests
{
    [TestMethod]
    public async Task Can_Execute_Parallel_Process_End_To_End_With_EfCore()
    {
        // 1. Define a BPMN process with a parallel gateway
        var bpmnXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<definitions xmlns=""http://www.omg.org/spec/BPMN/20100524/MODEL"" 
             xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
             targetNamespace=""http://protonflow.org/tests"">
  <process id=""parallel-test-e2e"" isExecutable=""true"">
    <startEvent id=""start"" />
    <sequenceFlow id=""flow1"" sourceRef=""start"" targetRef=""split"" />
    <parallelGateway id=""split"" />
    <sequenceFlow id=""flow2"" sourceRef=""split"" targetRef=""taskA"" />
    <sequenceFlow id=""flow3"" sourceRef=""split"" targetRef=""taskB"" />
    <serviceTask id=""taskA"" name=""Task A"" implementation=""task-handler"" />
    <serviceTask id=""taskB"" name=""Task B"" implementation=""task-handler"" />
    <sequenceFlow id=""flow4"" sourceRef=""taskA"" targetRef=""join"" />
    <sequenceFlow id=""flow5"" sourceRef=""taskB"" targetRef=""join"" />
    <parallelGateway id=""join"" />
    <sequenceFlow id=""flow6"" sourceRef=""join"" targetRef=""end"" />
    <endEvent id=""end"" />
  </process>
</definitions>";

        // 2. Configure the engine with EF Core persistence
        var services = new ServiceCollection();
        var taskACount = 0;
        var taskBCount = 0;

        // Use a shared in-memory SQLite connection for the test lifetime
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        var bpmnEngine = BpmnEngineBuilder.Create()
            .UseEntityFramework(options =>
            {
                options.UseSqlite(connection);

                // To test with PostgreSQL:
                // 1. Add the Npgsql.EntityFrameworkCore.PostgreSQL NuGet package.
                // 2. Comment out the UseSqlite line above.
                // 3. Uncomment the line below and provide your connection string.
                // options.UseNpgsql("Host=localhost;Database=protonflow;Username=postgres;Password=password");
            })
            .AddTaskHandler("task-handler", ctx =>
            {
                if (ctx.ElementId == "taskA") taskACount++;
                if (ctx.ElementId == "taskB") taskBCount++;
                return Task.CompletedTask;
            })
            .ConfigureServices(s => { }) // optional for dependency injection
            .Build();

        // Ensure the database is created
        using var scope = bpmnEngine.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProtonFlowDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Create a scope for the test execution
        using var testScope = bpmnEngine.Services.CreateScope();
        var engine = testScope.ServiceProvider.GetRequiredService<BpmnRuntimeEngine>();
        var jobStore = testScope.ServiceProvider.GetRequiredService<IJobStore>();
        var instanceStore = testScope.ServiceProvider.GetRequiredService<IInstanceStore>();

        // 3. Load and start the process
        var process = await engine.LoadBpmnXml(bpmnXml);
        var instance = await engine.StartProcessAsync(process.Key);

        // 4. Simulate a worker processing jobs until none are left
        const string workerId = "test-worker";
        while (true)
        {
            var job = await jobStore.ClaimNextJobAsync(workerId, TimeSpan.FromMinutes(1));
            if (job == null)
            {
                break; // No more jobs to process
            }

            await engine.StepAsync(job.ProcessInstanceId);
            await jobStore.CompleteJobAsync(job.Id, workerId);
        }

        // 5. Assert the final state
        var finalInstance = await instanceStore.GetByIdAsync(instance.Id);
        Assert.IsNotNull(finalInstance);
        Assert.IsTrue(finalInstance.IsCompleted, "Process instance should be completed.");
        Assert.AreEqual(1, taskACount, "Task A should have executed once.");
        Assert.AreEqual(1, taskBCount, "Task B should have executed once.");

        // Clean up the connection
        connection.Close();
    }
}
