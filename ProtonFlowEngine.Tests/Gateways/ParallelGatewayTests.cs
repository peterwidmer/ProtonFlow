using BpmnEngine.Engine;

namespace BpmnEngine.Tests.Gateways
{
    [TestClass]
    public class ParallelGatewayTests
    {
        private const string ParallelSplitJoinXml = """
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
  <process id="parallel-process" name="Parallel Process">
    <startEvent id="start" />
    <sequenceFlow id="f0" sourceRef="start" targetRef="gwSplit" />

    <parallelGateway id="gwSplit" />
    <sequenceFlow id="f1" sourceRef="gwSplit" targetRef="taskA" />
    <sequenceFlow id="f2" sourceRef="gwSplit" targetRef="taskB" />

    <serviceTask id="taskA" implementation="work-A" />
    <serviceTask id="taskB" implementation="work-B" />

    <sequenceFlow id="f3" sourceRef="taskA" targetRef="gwJoin" />
    <sequenceFlow id="f4" sourceRef="taskB" targetRef="gwJoin" />

    <parallelGateway id="gwJoin" />
    <sequenceFlow id="f5" sourceRef="gwJoin" targetRef="end" />

    <endEvent id="end" />
  </process>
</definitions>
""";

        [TestMethod]
        public async Task ParallelGateway_Splits_And_Joins()
        {
            var aCalls = 0;
            var bCalls = 0;
            var engine = BpmnEngineBuilder.Create()
                .UseInMemory()
                .AddTaskHandler("work-A", async ctx => { aCalls++; await Task.CompletedTask; })
                .AddTaskHandler("work-B", async ctx => { bCalls++; await Task.CompletedTask; })
                .Build();

            var def = await engine.LoadBpmnXml(ParallelSplitJoinXml);
            var inst = await engine.StartProcessAsync(def.Key);

            // Step 1: start -> gwSplit
            await engine.StepAsync(inst.Id);
            Assert.AreEqual(1, inst.ActiveTokens.Count);
            Assert.Contains("gwSplit", inst.ActiveTokens);

            // Step 2: split to taskA and taskB
            await engine.StepAsync(inst.Id);
            Assert.AreEqual(2, inst.ActiveTokens.Count);
            Assert.IsTrue(inst.ActiveTokens.Contains("taskA") && inst.ActiveTokens.Contains("taskB"));

            // Step 3: execute both parallel tasks (A and B). Both move to the join gateway.
            // The join gateway will wait because only one token has arrived, but it expects two.
            // The engine processes all active tokens in one step, so both task handlers are called.
            await engine.StepAsync(inst.Id);
            Assert.AreEqual(1, aCalls);
            Assert.AreEqual(1, bCalls);
            Assert.AreEqual(1, inst.ActiveTokens.Count);
            Assert.IsTrue(inst.ActiveTokens.Contains("gwJoin"));
            Assert.AreEqual(2, inst.ParallelJoinWaits["gwJoin"]);

            // Step 4: The second token from the parallel branch arrives at the join.
            // The join condition is now met (2 of 2 arrived), so it moves forward.
            await engine.StepAsync(inst.Id);
            Assert.AreEqual(1, inst.ActiveTokens.Count);
            Assert.Contains("end", inst.ActiveTokens);

            // Step 5: complete
            await engine.StepAsync(inst.Id);
            Assert.IsTrue(inst.IsCompleted);
        }
    }
}
