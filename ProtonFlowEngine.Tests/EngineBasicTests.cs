using BpmnEngine.Engine;
namespace BpmnEngine.Tests;

[TestClass]
public class EngineBasicTests
{
    private const string SimpleProcessXml = """
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
  <process id="invoice-process" name="Invoice Process">
    <startEvent id="start" />
    <sequenceFlow id="f1" sourceRef="start" targetRef="task" />
    <serviceTask id="task" implementation="rest-call" />
    <sequenceFlow id="f2" sourceRef="task" targetRef="end" />
    <endEvent id="end" />
  </process>
</definitions>
""";
        

    [TestMethod]
    public async Task Can_Load_Start_And_Step_To_End()
    {
        var called = false;
        var engine = BpmnEngineBuilder.Create()
            .UseInMemory()
            .AddTaskHandler("rest-call", async ctx => { called = true; await Task.CompletedTask; })
            .Build();

        var def = await engine.LoadBpmnXml(SimpleProcessXml);
        var instance = await engine.StartProcessAsync(def.Key, new { amount = 500 });

        // start token should be on start
        Assert.Contains("start", instance.ActiveTokens);

        // first step moves to task
        await engine.StepAsync(instance.Id);
        Assert.Contains("task", instance.ActiveTokens);

        // second step executes handler and moves to end
        await engine.StepAsync(instance.Id);
        Assert.IsTrue(called);
        Assert.Contains("end", instance.ActiveTokens);

        // third step completes
        await engine.StepAsync(instance.Id);
        Assert.IsTrue(instance.IsCompleted);
    }

    [TestMethod]
    public async Task Simulation_Mode_Does_Not_Invoke_Handlers()
    {
        var called = false;
        var engine = BpmnEngineBuilder.Create()
            .UseInMemory()
            .AddTaskHandler("rest-call", async ctx => { called = true; await Task.CompletedTask; })
            .Build();

        var def = await engine.LoadBpmnXml(SimpleProcessXml);
        var sim = engine.Simulate(def.Key);

        // step from start to task
        await sim.StepAsync();
        // step from task to end (no handler should run in simulation)
        await sim.StepAsync();
        // final step to complete
        await sim.StepAsync();

        Assert.IsFalse(called);
    }

    [TestMethod]
    public async Task GetCurrentTokenPositions_Returns_Active_ElementIds()
    {
        var engine = BpmnEngineBuilder.Create().UseInMemory().Build();
        var def = await engine.LoadBpmnXml(SimpleProcessXml);
        var instance = await engine.StartProcessAsync(def.Key);

        var pos1 = await engine.GetCurrentTokenPositions(instance.Id);
        Assert.Contains("start", pos1);

        await engine.StepAsync(instance.Id);
        var pos2 = await engine.GetCurrentTokenPositions(instance.Id);
        Assert.Contains("task", pos2);
    }
}