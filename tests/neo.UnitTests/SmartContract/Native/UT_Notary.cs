using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.UnitTests.Extensions;
using Neo.VM;
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Neo.UnitTests.SmartContract.Native
{
    [TestClass]
    public class UT_Notary
    {
        private DataCache _snapshot;

        [TestInitialize]
        public void TestSetup()
        {
            _snapshot = TestBlockchain.GetTestSnapshot();
        }

        [TestMethod]
        public void Check_BalanceOf()
        {
            Assert.ThrowsException<Exception>(() => NativeContract.Notary.BalanceOf(_snapshot, UInt160.Zero));
        }

        [TestMethod]
        public void Check_ExpirationOf()
        {
            Assert.ThrowsException<Exception>(() => NativeContract.Notary.ExpirationOf(_snapshot, UInt160.Zero));
        }

        [TestMethod]
        public void Check_GetMaxNotValidBeforeDelta()
        {
            NativeContract.Notary.GetMaxNotValidBeforeDelta(_snapshot).Should().Be(140);
        }

        [TestMethod]
        public void Check_SetMaxNotValidBeforeDelta()
        {
            var snapshot = _snapshot.CreateSnapshot();
            // Fake blockchain
            var persistingBlock = new Block { Header = new Header { Index = 1000 } };
            UInt160 committeeAddress = NativeContract.NEO.GetCommitteeAddress(snapshot);

            using var engine = ApplicationEngine.Create(TriggerType.Application, new Nep17NativeContractExtensions.ManualWitness(committeeAddress), snapshot, persistingBlock, settings: TestBlockchain.TheNeoSystem.Settings);
            using var script = new ScriptBuilder();
            script.EmitDynamicCall(NativeContract.Notary.Hash, "setMaxNotValidBeforeDelta", 100);
            engine.LoadScript(script.ToArray());
            VMState vMState = engine.Execute();
            vMState.Should().Be(VMState.HALT);
            NativeContract.Notary.GetMaxNotValidBeforeDelta(snapshot).Should().Be(100);
        }

        [TestMethod]
        public void Check_GetNotaryServiceFeePerKey()
        {
            NativeContract.Notary.GetNotaryServiceFeePerKey(_snapshot).Should().Be(10000000L);
        }

        [TestMethod]
        public void Check_SetNotaryServiceFeePerKey()
        {
            var snapshot = _snapshot.CreateSnapshot();
            // Fake blockchain
            var persistingBlock = new Block { Header = new Header { Index = 1000 } };
            UInt160 committeeAddress = NativeContract.NEO.GetCommitteeAddress(snapshot);

            using var engine = ApplicationEngine.Create(TriggerType.Application, new Nep17NativeContractExtensions.ManualWitness(committeeAddress), snapshot, persistingBlock, settings: TestBlockchain.TheNeoSystem.Settings);
            using var script = new ScriptBuilder();
            script.EmitDynamicCall(NativeContract.Notary.Hash, "setNotaryServiceFeePerKey", 100);
            engine.LoadScript(script.ToArray());
            VMState vMState = engine.Execute();
            vMState.Should().Be(VMState.HALT);
            NativeContract.Notary.GetNotaryServiceFeePerKey(snapshot).Should().Be(100);
        }

        internal static StorageKey CreateStorageKey(byte prefix, uint key)
        {
            return CreateStorageKey(prefix, BitConverter.GetBytes(key));
        }

        internal static StorageKey CreateStorageKey(byte prefix, byte[] key = null)
        {
            StorageKey storageKey = new()
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
