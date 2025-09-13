using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProtonFlow.Persistence.EfCore.Adapters;
using ProtonFlow.Persistence.EfCore.Storage;
using ProtonFlow.Persistence.EfCore.Storage.Models;

namespace ProtonFlow.Persistence.EfCore.Tests;

[TestClass]
public class EfCoreJobAndConcurrencyTests
{
    private static ProtonFlowDbContext CreateSqliteContext(out SqliteConnection conn)
    {
        conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ProtonFlowDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new ProtonFlowDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [TestMethod]
    public async Task StoredProcessInstance_RowVersion_conflict_throws()
    {
#pragma warning disable CS0612 // Type or member is obsolete
        // Setup initial state with one context
        using var setupCtx = CreateSqliteContext(out var conn);
        var setupStorage = new EfBpmnStorage(setupCtx);
        var def = await setupStorage.SaveProcessDefinitionAsync(new StoredProcessDefinition
        {
            Key = "test",
            Name = "Test",
            Xml = "<xml />",
            ContentHash = EfBpmnStorage.ComputeHash("<xml />")
        });
        var created = await setupStorage.CreateProcessInstanceAsync(new StoredProcessInstance
        {
            ProcessDefinitionId = def.Id,
            ProcessKey = def.Key
        });
#pragma warning restore CS0612 // Type or member is obsolete

        // Simulate two concurrent workers, each with their own context
        using var ctx1 = new ProtonFlowDbContext(new DbContextOptionsBuilder<ProtonFlowDbContext>().UseSqlite(conn).Options);
        using var ctx2 = new ProtonFlowDbContext(new DbContextOptionsBuilder<ProtonFlowDbContext>().UseSqlite(conn).Options);
        var storage1 = new EfBpmnStorage(ctx1);
        var storage2 = new EfBpmnStorage(ctx2);

        // Load same instance into both contexts (AsNoTracking is used in GetById)
        var copy1 = await storage1.GetProcessInstanceByIdAsync(created.Id);
        var copy2 = await storage2.GetProcessInstanceByIdAsync(created.Id);
        Assert.IsNotNull(copy1);
        Assert.IsNotNull(copy2);

        // Worker 1: Modify and save
        copy1!.VariablesJson = "{\"a\":1}";
        await storage1.UpdateProcessInstanceAsync(copy1!);

        // Worker 2: Modify and attempt to save the stale copy
        copy2!.VariablesJson = "{\"a\":2}";
        await Assert.ThrowsExceptionAsync<DbUpdateConcurrencyException>(async () =>
        {
            await storage2.UpdateProcessInstanceAsync(copy2!);
        });
    }

    [TestMethod]
    public async Task EfJobStore_only_one_claim_then_reclaim_after_expiry()
    {
        using var ctx = CreateSqliteContext(out var conn);
        var store1 = new EfJobStore(ctx);
        // Separate context to simulate another worker
        using var ctxOther = new ProtonFlowDbContext(new DbContextOptionsBuilder<ProtonFlowDbContext>().UseSqlite(conn).Options);
        var store2 = new EfJobStore(ctxOther);

        var job = new BpmnEngine.Interfaces.Job
        {
            Type = "continue-instance",
            ProcessInstanceId = "pi-1",
            RunAt = DateTimeOffset.UtcNow
        };
        await store1.EnqueueJobAsync(job);

        var claimed1 = await store1.ClaimNextJobAsync("nodeA", TimeSpan.FromSeconds(1));
        var claimed2 = await store2.ClaimNextJobAsync("nodeB", TimeSpan.FromSeconds(1));

        Assert.IsNotNull(claimed1);
        Assert.IsNull(claimed2, "Second claim should not succeed while locked");

        // Simulate lease expiry
        var row = await ctx.Jobs.FirstAsync(j => j.Id == claimed1!.Id);
        row.LockedUntil = DateTimeOffset.UtcNow.AddMilliseconds(-10);
        await ctx.SaveChangesAsync();

        var reclaimed = await store2.ClaimNextJobAsync("nodeB", TimeSpan.FromSeconds(1));
        Assert.IsNotNull(reclaimed);

        await store2.CompleteJobAsync(reclaimed!.Id, "nodeB");
        var none = await store1.ClaimNextJobAsync("nodeA", TimeSpan.FromSeconds(1));
        Assert.IsNull(none);
    }
}
