﻿using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.UnitTests.Extensions;
using System.Linq;

namespace Neo.UnitTests
{
    [TestClass]
    public class UT_Policy
    {
        Store Store;

        [TestInitialize]
        public void TestSetup()
        {
            TestBlockchain.InitializeMockNeoSystem();
            Store = TestBlockchain.GetStore();
        }

        [TestMethod]
        public void Check_SupportedStandards() => NativeContract.Policy.SupportedStandards().Should().BeEquivalentTo(new string[] { "NEP-10" });

        [TestMethod]
        public void Check_Initialize()
        {
            var snapshot = Store.GetSnapshot().Clone();
            var keyCount = snapshot.Storages.GetChangeSet().Count();

            NativeContract.Policy.Initialize(new ApplicationEngine(TriggerType.Application, null, snapshot, 0)).Should().BeTrue();

            (keyCount + 5).Should().Be(snapshot.Storages.GetChangeSet().Count());

            var ret = NativeContract.Policy.Call(snapshot, "getMaxTransactionsPerBlock");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(512);

            ret = NativeContract.Policy.Call(snapshot, "getMaxLowPriorityTransactionsPerBlock");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(20);

            ret = NativeContract.Policy.Call(snapshot, "getMaxLowPriorityTransactionSize");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(256);

            ret = NativeContract.Policy.Call(snapshot, "getFeePerByte");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(1000);

            ret = NativeContract.Policy.Call(snapshot, "getBlockedAccounts");
            ret.Should().BeOfType<VM.Types.Array>();
            ((VM.Types.Array)ret).Count.Should().Be(0);
        }

        [TestMethod]
        public void Check_SetMaxTransactionsPerBlock()
        {
            var snapshot = Store.GetSnapshot().Clone();

            // Fake blockchain

            snapshot.PersistingBlock = new Block() { Index = 1000, PrevHash = UInt256.Zero };
            snapshot.Blocks.Add(UInt256.Zero, new Ledger.TrimmedBlock() { NextConsensus = UInt160.Zero });

            NativeContract.Policy.Initialize(new ApplicationEngine(TriggerType.Application, null, snapshot, 0)).Should().BeTrue();

            // Without signature

            var ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { }),
                "setMaxTransactionsPerBlock", new ContractParameter(ContractParameterType.Integer) { Value = 1 });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeFalse();

            ret = NativeContract.Policy.Call(snapshot, "getMaxTransactionsPerBlock");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(512);

            // With signature

            ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { UInt160.Zero }),
                "setMaxTransactionsPerBlock", new ContractParameter(ContractParameterType.Integer) { Value = 1 });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeTrue();

            ret = NativeContract.Policy.Call(snapshot, "getMaxTransactionsPerBlock");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(1);
        }

        [TestMethod]
        public void Check_SetMaxLowPriorityTransactionsPerBlock()
        {
            var snapshot = Store.GetSnapshot().Clone();

            // Fake blockchain

            snapshot.PersistingBlock = new Block() { Index = 1000, PrevHash = UInt256.Zero };
            snapshot.Blocks.Add(UInt256.Zero, new Ledger.TrimmedBlock() { NextConsensus = UInt160.Zero });

            NativeContract.Policy.Initialize(new ApplicationEngine(TriggerType.Application, null, snapshot, 0)).Should().BeTrue();

            // Without signature

            var ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { }),
                "setMaxLowPriorityTransactionsPerBlock", new ContractParameter(ContractParameterType.Integer) { Value = 1 });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeFalse();

            ret = NativeContract.Policy.Call(snapshot, "getMaxLowPriorityTransactionsPerBlock");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(20);

            // With signature

            ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { UInt160.Zero }),
                "setMaxLowPriorityTransactionsPerBlock", new ContractParameter(ContractParameterType.Integer) { Value = 1 });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeTrue();

            ret = NativeContract.Policy.Call(snapshot, "getMaxLowPriorityTransactionsPerBlock");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(1);
        }

        [TestMethod]
        public void Check_SetMaxLowPriorityTransactionSize()
        {
            var snapshot = Store.GetSnapshot().Clone();

            // Fake blockchain

            snapshot.PersistingBlock = new Block() { Index = 1000, PrevHash = UInt256.Zero };
            snapshot.Blocks.Add(UInt256.Zero, new Ledger.TrimmedBlock() { NextConsensus = UInt160.Zero });

            NativeContract.Policy.Initialize(new ApplicationEngine(TriggerType.Application, null, snapshot, 0)).Should().BeTrue();

            // Without signature

            var ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { }),
                "setMaxLowPriorityTransactionSize", new ContractParameter(ContractParameterType.Integer) { Value = 1 });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeFalse();

            ret = NativeContract.Policy.Call(snapshot, "getMaxLowPriorityTransactionSize");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(256);

            // With signature

            ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { UInt160.Zero }),
                "setMaxLowPriorityTransactionSize", new ContractParameter(ContractParameterType.Integer) { Value = 1 });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeTrue();

            ret = NativeContract.Policy.Call(snapshot, "getMaxLowPriorityTransactionSize");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(1);
        }

        [TestMethod]
        public void Check_SetFeePerByte()
        {
            var snapshot = Store.GetSnapshot().Clone();

            // Fake blockchain

            snapshot.PersistingBlock = new Block() { Index = 1000, PrevHash = UInt256.Zero };
            snapshot.Blocks.Add(UInt256.Zero, new Ledger.TrimmedBlock() { NextConsensus = UInt160.Zero });

            NativeContract.Policy.Initialize(new ApplicationEngine(TriggerType.Application, null, snapshot, 0)).Should().BeTrue();

            // Without signature

            var ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { }),
                "setFeePerByte", new ContractParameter(ContractParameterType.Integer) { Value = 1 });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeFalse();

            ret = NativeContract.Policy.Call(snapshot, "getFeePerByte");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(1000);

            // With signature

            ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { UInt160.Zero }),
                "setFeePerByte", new ContractParameter(ContractParameterType.Integer) { Value = 1 });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeTrue();

            ret = NativeContract.Policy.Call(snapshot, "getFeePerByte");
            ret.Should().BeOfType<VM.Types.Integer>();
            ret.GetBigInteger().Should().Be(1);
        }

        [TestMethod]
        public void Check_BlockAccount()
        {
            var snapshot = Store.GetSnapshot().Clone();

            // Fake blockchain

            snapshot.PersistingBlock = new Block() { Index = 1000, PrevHash = UInt256.Zero };
            snapshot.Blocks.Add(UInt256.Zero, new Ledger.TrimmedBlock() { NextConsensus = UInt160.Zero });

            NativeContract.Policy.Initialize(new ApplicationEngine(TriggerType.Application, null, snapshot, 0)).Should().BeTrue();

            // Without signature

            var ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { }),
                "blockAccount", new ContractParameter(ContractParameterType.Hash160) { Value = UInt160.Zero });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeFalse();

            ret = NativeContract.Policy.Call(snapshot, "getBlockedAccounts");
            ret.Should().BeOfType<VM.Types.Array>();
            ((VM.Types.Array)ret).Count.Should().Be(0);

            // With signature

            ret = NativeContract.Policy.Call(snapshot, new Nep5NativeContractExtensions.ManualWitness(new UInt160[] { UInt160.Zero }),
                "blockAccount", new ContractParameter(ContractParameterType.Hash160) { Value = UInt160.Zero });
            ret.Should().BeOfType<VM.Types.Boolean>();
            ret.GetBoolean().Should().BeTrue();

            ret = NativeContract.Policy.Call(snapshot, "getBlockedAccounts");
            ret.Should().BeOfType<VM.Types.Array>();
            ((VM.Types.Array)ret).Count.Should().Be(1);
            ((VM.Types.Array)ret)[0].GetByteArray().ShouldBeEquivalentTo(UInt160.Zero.ToArray());
        }
    }
}