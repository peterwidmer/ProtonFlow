using BpmnEngine.Engine;
using BpmnEngine.Models;

namespace BpmnEngine.Tests.Gateways
{
    [TestClass]
    public class ExclusiveGatewayTests
    {
        private const string ExclusiveGatewayXml_With_Default = """
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <process id="gw-process" name="Gateway Process">
    <startEvent id="start" />
    <sequenceFlow id="toGw" sourceRef="start" targetRef="gw" />
    <exclusiveGateway id="gw" default="fLow" />
    <sequenceFlow id="fHigh" sourceRef="gw" targetRef="endHigh">
      <conditionExpression xsi:type="tFormalExpression"><![CDATA[${amount > 100}]]></conditionExpression>
    </sequenceFlow>
    <sequenceFlow id="fLow" sourceRef="gw" targetRef="endLow" />
    <endEvent id="endHigh" />
    <endEvent id="endLow" />
  </process>
</definitions>
""";

        private const string ExclusiveGatewayXml_No_Default = """
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <process id="gw-process-2" name="Gateway Process 2">
    <startEvent id="start" />
    <sequenceFlow id="toGw" sourceRef="start" targetRef="gw" />
    <exclusiveGateway id="gw" />
    <sequenceFlow id="fA" sourceRef="gw" targetRef="endA">
      <conditionExpression xsi:type="tFormalExpression"><![CDATA[${amount >= 200}]]></conditionExpression>
    </sequenceFlow>
    <sequenceFlow id="fB" sourceRef="gw" targetRef="endB">
      <conditionExpression xsi:type="tFormalExpression"><![CDATA[${amount < 200}]]></conditionExpression>
    </sequenceFlow>
    <endEvent id="endA" />
    <endEvent id="endB" />
  </process>
</definitions>
""";

        [TestMethod]
        public async Task ExclusiveGateway_Takes_Conditional_When_True_Otherwise_Default()
        {
            var engine = BpmnEngineBuilder.Create().UseInMemory().Build();

            var def = await engine.LoadBpmnXml(ExclusiveGatewayXml_With_Default);
            Assert.IsTrue(def.Elements.TryGetValue("gw", out var el) && el is ExclusiveGateway);

            // amount 150 -> take fHigh -> endHigh
            var instanceHigh = await engine.StartProcessAsync(def.Key, new { amount = 150 });
            await engine.StepAsync(instanceHigh.Id); // start -> gw
            await engine.StepAsync(instanceHigh.Id); // gw -> pick path
            Assert.Contains("endHigh", instanceHigh.ActiveTokens);
            await engine.StepAsync(instanceHigh.Id); // complete
            Assert.IsTrue(instanceHigh.IsCompleted);

            // amount 50 -> no condition true => take default fLow -> endLow
            var instanceLow = await engine.StartProcessAsync(def.Key, new { amount = 50 });
            await engine.StepAsync(instanceLow.Id); // start -> gw
            await engine.StepAsync(instanceLow.Id); // gw -> pick default
            Assert.Contains("endLow", instanceLow.ActiveTokens);
            await engine.StepAsync(instanceLow.Id); // complete
            Assert.IsTrue(instanceLow.IsCompleted);
        }

        [TestMethod]
        public async Task ExclusiveGateway_Without_Default_Picks_First_True_Condition()
        {
            var engine = BpmnEngineBuilder.Create().UseInMemory().Build();
            var def = await engine.LoadBpmnXml(ExclusiveGatewayXml_No_Default);
            Assert.IsTrue(def.Elements.TryGetValue("gw", out var el) && el is ExclusiveGateway);

            // amount 250 -> should go to endA
            var instA = await engine.StartProcessAsync(def.Key, new { amount = 250 });
            await engine.StepAsync(instA.Id); // start -> gw
            await engine.StepAsync(instA.Id); // gw -> evaluate conditions
            Assert.Contains("endA", instA.ActiveTokens);

            // amount 120 -> should go to endB
            var instB = await engine.StartProcessAsync(def.Key, new { amount = 120 });
            await engine.StepAsync(instB.Id); // start -> gw
            await engine.StepAsync(instB.Id); // gw -> evaluate conditions
            Assert.Contains("endB", instB.ActiveTokens);
        }
    }
}
