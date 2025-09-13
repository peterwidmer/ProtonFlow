using BpmnEngine.Interfaces;
using BpmnEngine.Stores;

namespace ProtonFlow.BpmnEngine.Tests;

[TestClass]
public class InMemoryJobStoreTests
{
    [TestMethod]
    public async Task Enqueue_Claim_Complete_with_single_claim_semantics()
    {
        var store = new InMemoryJobStore();
        var job = new Job { Type = "continue-instance", ProcessInstanceId = "pi-1", RunAt = DateTimeOffset.UtcNow };
        await store.EnqueueJobAsync(job);

        Job? c1 = null, c2 = null;
        await Task.WhenAll(
            Task.Run(async () => c1 = await store.ClaimNextJobAsync("nodeA", TimeSpan.FromSeconds(1))),
            Task.Run(async () => c2 = await store.ClaimNextJobAsync("nodeB", TimeSpan.FromSeconds(1)))
        );

        Assert.IsTrue((c1 != null) ^ (c2 != null), "Exactly one claim should succeed");
        var claimed = c1 ?? c2!;
        await store.CompleteJobAsync(claimed.Id, claimed.OwnerId!);

        var none = await store.ClaimNextJobAsync("nodeC", TimeSpan.FromSeconds(1));
        Assert.IsNull(none);
    }
}
