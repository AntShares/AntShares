using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.SmartContract;
using Neo.VM.Types;


namespace Neo.UnitTests.SmartContract
{

    public partial class UT_ApplicationEngine
    {
        [TestMethod]
        public void TestGetRandom()
        {
            var settings = ProtocolSettings.Default;
            using var engine = ApplicationEngine.Create(TriggerType.Application, null, null, settings: TestBlockchain.TheNeoSystem.Settings, gas: 1100_00000000);

            uint rand_1 = engine.GetRandom();
            uint rand_2 = engine.GetRandom();
            uint rand_3 = engine.GetRandom();
            uint rand_4 = engine.GetRandom();
            uint rand_5 = engine.GetRandom();

            rand_1.Should().Be(176440129u);
            rand_2.Should().Be(3661770765u);
            rand_3.Should().Be(2257404069u);
            rand_4.Should().Be(3268448324u);
            rand_5.Should().Be(3091612587u);
        }
    }
}
