using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.UnitTests.Extensions;
using Neo.VM;
using System;
using System.Linq;
using System.Numerics;

namespace Neo.UnitTests.SmartContract.Native.Tokens
{
    [TestClass]
    public class UT_GasToken
    {
        [TestInitialize]
        public void TestSetup()
        {
            TestBlockchain.InitializeMockNeoSystem();
        }

        [TestMethod]
        public void Check_Name() => NativeContract.GAS.Name().Should().Be("GAS");

        [TestMethod]
        public void Check_Symbol() => NativeContract.GAS.Symbol().Should().Be("gas");

        [TestMethod]
        public void Check_Decimals() => NativeContract.GAS.Decimals().Should().Be(8);

        [TestMethod]
        public void Check_BalanceOfTransferAndBurn()
        {
            var snapshot = Blockchain.Singleton.GetSnapshot();
            snapshot.PersistingBlock = new Block() { Index = 1000 };

            byte[] from = Blockchain.GetConsensusAddress(Blockchain.StandbyValidators).ToArray();

            byte[] to = new byte[20];

            var keyCount = snapshot.Storages.GetChangeSet().Count();

            var supply = NativeContract.GAS.TotalSupply(snapshot);
            supply.Should().Be(7000000025000000); // 7000000000000000 + 25000000 (neo holder reward)

            // Check unclaim

            var unclaim = UT_NeoToken.Check_UnclaimedGas(snapshot, from);
            unclaim.Value.Should().Be(new BigInteger(0.5 * 1000 * 100000000L));
            unclaim.State.Should().BeTrue();

            // Transfer

            NativeContract.NEO.Transfer(snapshot, from, to, BigInteger.Zero, true).Should().BeTrue();
            NativeContract.NEO.BalanceOf(snapshot, from).Should().Be(100000000);
            NativeContract.NEO.BalanceOf(snapshot, to).Should().Be(0);

            NativeContract.GAS.BalanceOf(snapshot, from).Should().Be(7000050000000000);
            NativeContract.GAS.BalanceOf(snapshot, to).Should().Be(0);

            // Check unclaim

            unclaim = UT_NeoToken.Check_UnclaimedGas(snapshot, from);
            unclaim.Value.Should().Be(new BigInteger(0));
            unclaim.State.Should().BeTrue();

            supply = NativeContract.GAS.TotalSupply(snapshot);
            supply.Should().Be(7000050025000000);

            snapshot.Storages.GetChangeSet().Count().Should().Be(keyCount + 3); // Gas

            // Transfer

            keyCount = snapshot.Storages.GetChangeSet().Count();

            NativeContract.GAS.Transfer(snapshot, from, to, 7000050000000000, false).Should().BeFalse(); // Not signed
            NativeContract.GAS.Transfer(snapshot, from, to, 7000050000000001, true).Should().BeFalse(); // More than balance
            NativeContract.GAS.Transfer(snapshot, from, to, 7000050000000000, true).Should().BeTrue(); // All balance

            // Balance of

            NativeContract.GAS.BalanceOf(snapshot, to).Should().Be(7000050000000000);
            NativeContract.GAS.BalanceOf(snapshot, from).Should().Be(0);

            snapshot.Storages.GetChangeSet().Count().Should().Be(keyCount + 1); // All

            // Burn

            var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, 0);
            keyCount = snapshot.Storages.GetChangeSet().Count();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                NativeContract.GAS.Burn(engine, new UInt160(to), BigInteger.MinusOne));

            // Burn more than expected

            Assert.ThrowsException<InvalidOperationException>(() =>
                NativeContract.GAS.Burn(engine, new UInt160(to), new BigInteger(70000500_00000001)));

            // Real burn

            NativeContract.GAS.Burn(engine, new UInt160(to), new BigInteger(1));

            NativeContract.GAS.BalanceOf(snapshot, to).Should().Be(7000049999999999);

            keyCount.Should().Be(snapshot.Storages.GetChangeSet().Count());

            // Burn all

            NativeContract.GAS.Burn(engine, new UInt160(to), new BigInteger(7000049999999999));

            (keyCount - 1).Should().Be(snapshot.Storages.GetChangeSet().Count());

            // Bad inputs

            NativeContract.GAS.Transfer(snapshot, from, to, BigInteger.MinusOne, true).Should().BeFalse();
            NativeContract.GAS.Transfer(snapshot, new byte[19], to, BigInteger.One, false).Should().BeFalse();
            NativeContract.GAS.Transfer(snapshot, from, new byte[19], BigInteger.One, false).Should().BeFalse();
        }

        [TestMethod]
        public void Check_BadScript()
        {
            var engine = ApplicationEngine.Create(TriggerType.Application, null, Blockchain.Singleton.GetSnapshot(), 0);

            var script = new ScriptBuilder();
            script.Emit(OpCode.NOP);
            engine.LoadScript(script.ToArray());

            Assert.ThrowsException<InvalidOperationException>(() => NativeContract.GAS.Invoke(engine));
        }

        internal static StorageKey CreateStorageKey(byte prefix, uint key)
        {
            return CreateStorageKey(prefix, BitConverter.GetBytes(key));
        }

        internal static StorageKey CreateStorageKey(byte prefix, byte[] key = null)
        {
            StorageKey storageKey = new StorageKey
            {
                Id = NativeContract.NEO.Id,
                Key = new byte[sizeof(byte) + (key?.Length ?? 0)]
            };
            storageKey.Key[0] = prefix;
            key?.CopyTo(storageKey.Key.AsSpan(1));
            return storageKey;
        }
    }
}
