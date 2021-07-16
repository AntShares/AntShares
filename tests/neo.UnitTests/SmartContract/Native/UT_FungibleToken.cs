using Akka.TestKit.Xunit2;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.SmartContract.Native;

namespace Neo.UnitTests.SmartContract.Native
{
    [TestClass]
    public class UT_FungibleToken : TestKit
    {
        [TestMethod]
        public void TestTotalSupply()
        {
            var snapshot = TestBlockchain.GetTestSnapshot();
            NativeContract.GAS.TotalSupply(snapshot).Should().Be(5200000050000000);
        }
    }
}
