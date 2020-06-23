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
        public void Check_SupportedStandards() => NativeContract.GAS.SupportedStandards().Should().BeEquivalentTo(new string[] { "NEP-5", "NEP-10" });

        [TestMethod]
        public void Check_BalanceOfTransferAndBurn()
        {
            var snapshot = Blockchain.Singleton.GetSnapshot();
            snapshot.PersistingBlock = new Block() { Index = 100 };

            byte[] from = Blockchain.GetConsensusAddress(Blockchain.StandbyValidators).ToArray();

            byte[] to = new byte[20];

            var keyCount = snapshot.Storages.GetChangeSet().Count();

            var supply = NativeContract.GAS.TotalSupply(snapshot);
            supply.Should().Be(3000000000000000);

            // Check unclaim

            snapshot.Storages.Add(CreateStorageKey(57, uint.MaxValue - 100 - 1), new StorageItem() { Value = new BigInteger(100).ToByteArray() });
            snapshot.Storages.Add(CreateStorageKey(57, uint.MaxValue - 0 - 1), new StorageItem() { Value = new BigInteger(0).ToByteArray() });
            var unclaim = UT_NeoToken.Check_UnclaimedGas(snapshot, from);
            unclaim.Value.Should().Be(new BigInteger(5000000800));
            unclaim.State.Should().BeTrue();

            // Transfer

            NativeContract.NEO.Transfer(snapshot, from, to, BigInteger.Zero, true).Should().BeTrue();
            NativeContract.NEO.BalanceOf(snapshot, from).Should().Be(50000008);
            NativeContract.NEO.BalanceOf(snapshot, to).Should().Be(0);

            NativeContract.GAS.BalanceOf(snapshot, from).Should().Be(3000005000000800);
            NativeContract.GAS.BalanceOf(snapshot, to).Should().Be(0);

            // Check unclaim

            unclaim = UT_NeoToken.Check_UnclaimedGas(snapshot, from);
            unclaim.Value.Should().Be(new BigInteger(0));
            unclaim.State.Should().BeTrue();

            supply = NativeContract.GAS.TotalSupply(snapshot);
            supply.Should().Be(3000005000000800);

            snapshot.Storages.GetChangeSet().Count().Should().Be(keyCount + 5); // Gas

            // Transfer

            keyCount = snapshot.Storages.GetChangeSet().Count();

            NativeContract.GAS.Transfer(snapshot, from, to, 3000005000000800, false).Should().BeFalse(); // Not signed
            NativeContract.GAS.Transfer(snapshot, from, to, 3000005000000801, true).Should().BeFalse(); // More than balance
            NativeContract.GAS.Transfer(snapshot, from, to, 3000005000000800, true).Should().BeTrue(); // All balance

            // Balance of

            NativeContract.GAS.BalanceOf(snapshot, to).Should().Be(3000005000000800);
            NativeContract.GAS.BalanceOf(snapshot, from).Should().Be(0);

            snapshot.Storages.GetChangeSet().Count().Should().Be(keyCount + 1); // All

            // Burn

            var engine = new ApplicationEngine(TriggerType.Application, null, snapshot, 0);
            keyCount = snapshot.Storages.GetChangeSet().Count();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                NativeContract.GAS.Burn(engine, new UInt160(to), BigInteger.MinusOne));

            // Burn more than expected

            Assert.ThrowsException<InvalidOperationException>(() =>
                NativeContract.GAS.Burn(engine, new UInt160(to), new BigInteger(3000005000000801)));

            // Real burn

            NativeContract.GAS.Burn(engine, new UInt160(to), new BigInteger(1));

            NativeContract.GAS.BalanceOf(snapshot, to).Should().Be(3000005000000799);

            keyCount.Should().Be(snapshot.Storages.GetChangeSet().Count());

            // Burn all

            NativeContract.GAS.Burn(engine, new UInt160(to), new BigInteger(3000005000000799));

            (keyCount - 1).Should().Be(snapshot.Storages.GetChangeSet().Count());

            // Bad inputs

            NativeContract.GAS.Transfer(snapshot, from, to, BigInteger.MinusOne, true).Should().BeFalse();
            NativeContract.GAS.Transfer(snapshot, new byte[19], to, BigInteger.One, false).Should().BeFalse();
            NativeContract.GAS.Transfer(snapshot, from, new byte[19], BigInteger.One, false).Should().BeFalse();
        }

        [TestMethod]
        public void Check_BadScript()
        {
            var engine = new ApplicationEngine(TriggerType.Application, null, Blockchain.Singleton.GetSnapshot(), 0);

            var script = new ScriptBuilder();
            script.Emit(OpCode.NOP);
            engine.LoadScript(script.ToArray());

            Assert.ThrowsException<InvalidOperationException>(() => NativeContract.GAS.Invoke(engine));
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

        internal static StorageKey CreateStorageKey(byte prefix, uint key)
        {
            return CreateStorageKey(prefix, BitConverter.GetBytes(key));
        }
    }
}
