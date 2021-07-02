using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace Neo.UnitTests.Ledger
{
    [TestClass]
    public class UT_TransactionVerificationContext
    {
        [ClassInitialize]
        public static void TestSetup(TestContext ctx)
        {
            _ = TestBlockchain.TheNeoSystem;
        }

        private Transaction CreateTransactionWithFee(long networkFee, long systemFee)
        {
            Random random = new();
            var randomBytes = new byte[16];
            random.NextBytes(randomBytes);
            Mock<Transaction> mock = new();
            mock.Setup(p => p.VerifyStateDependent(It.IsAny<ProtocolSettings>(), It.IsAny<ClonedCache>(), It.IsAny<TransactionVerificationContext>(), null)).Returns(VerifyResult.Succeed);
            mock.Setup(p => p.VerifyStateIndependent(It.IsAny<ProtocolSettings>())).Returns(VerifyResult.Succeed);
            mock.Object.Script = randomBytes;
            mock.Object.NetworkFee = networkFee;
            mock.Object.SystemFee = systemFee;
            mock.Object.Signers = new[] { new Signer { Account = UInt160.Zero } };
            mock.Object.Attributes = Array.Empty<TransactionAttribute>();
            mock.Object.Witnesses = new[]
            {
                new Witness
                {
                    InvocationScript = Array.Empty<byte>(),
                    VerificationScript = Array.Empty<byte>()
                }
            };
            return mock.Object;
        }

        [TestMethod]
        public async Task TestDuplicateOracle()
        {
            // Fake balance
            var snapshot = TestBlockchain.GetTestSnapshot();

            ApplicationEngine engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestBlockchain.TheNeoSystem.Settings, gas: long.MaxValue);
            BigInteger balance = NativeContract.GAS.BalanceOf(snapshot, UInt160.Zero);
            await NativeContract.GAS.Burn(engine, UInt160.Zero, balance);
            _ = NativeContract.GAS.Mint(engine, UInt160.Zero, 8, false);

            // Test
            TransactionVerificationContext verificationContext = new();
            var tx = CreateTransactionWithFee(1, 2);
            tx.Attributes = new TransactionAttribute[] { new OracleResponse() { Code = OracleResponseCode.ConsensusUnreachable, Id = 1, Result = Array.Empty<byte>() } };
            verificationContext.CheckTransaction(tx, snapshot).Should().BeTrue();
            verificationContext.AddTransaction(tx);

            tx = CreateTransactionWithFee(2, 1);
            tx.Attributes = new TransactionAttribute[] { new OracleResponse() { Code = OracleResponseCode.ConsensusUnreachable, Id = 1, Result = Array.Empty<byte>() } };
            verificationContext.CheckTransaction(tx, snapshot).Should().BeFalse();
        }

        [TestMethod]
        public async Task TestTransactionSenderFee()
        {
            var snapshot = TestBlockchain.GetTestSnapshot();
            ApplicationEngine engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestBlockchain.TheNeoSystem.Settings, gas: long.MaxValue);
            BigInteger balance = NativeContract.GAS.BalanceOf(snapshot, UInt160.Zero);
            await NativeContract.GAS.Burn(engine, UInt160.Zero, balance);
            _ = NativeContract.GAS.Mint(engine, UInt160.Zero, 8, true);

            TransactionVerificationContext verificationContext = new();
            var tx = CreateTransactionWithFee(1, 2);
            verificationContext.CheckTransaction(tx, snapshot).Should().BeTrue();
            verificationContext.AddTransaction(tx);
            verificationContext.CheckTransaction(tx, snapshot).Should().BeTrue();
            verificationContext.AddTransaction(tx);
            verificationContext.CheckTransaction(tx, snapshot).Should().BeFalse();
            verificationContext.RemoveTransaction(tx);
            verificationContext.CheckTransaction(tx, snapshot).Should().BeTrue();
            verificationContext.AddTransaction(tx);
            verificationContext.CheckTransaction(tx, snapshot).Should().BeFalse();
        }
    }
}
