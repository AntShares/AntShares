using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.SmartContract;

namespace Neo.UnitTests.SmartContract
{
    [TestClass]
    public class UT_InteropDescriptor
    {
        [TestMethod]
        public void TestGetMethod()
        {
            string method = @"System.ExecutionEngine.GetScriptContainer";
            long price = 0_00000250;
            TriggerType allowedTriggers = TriggerType.All;
            InteropDescriptor descriptor = new InteropDescriptor(method, TestHandler, price, allowedTriggers, CallFlags.None);
            descriptor.Name.Should().Be(method);
            descriptor.FixedPrice.Should().Be(price);
        }

        private bool TestHandler(ApplicationEngine engine)
        {
            return true;
        }
    }
}
