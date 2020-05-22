using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.SmartContract.Nns;
using Neo.UnitTests.Extensions;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Collections;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Neo.UnitTests.SmartContract.Native.Tokens
{
    [TestClass]
    public class UT_NnsContract
    {
        [TestInitialize]
        public void TestSetup()
        {
            TestBlockchain.InitializeMockNeoSystem();
            var snapshot = Blockchain.Singleton.GetSnapshot();
            snapshot.PersistingBlock = new Block() { Index = 1000 };
            NativeContract.NNS.Initialize(new ApplicationEngine(TriggerType.Application, null, snapshot, 0));
            NativeContract.NEO.Initialize(new ApplicationEngine(TriggerType.Application, null, snapshot, 0));
        }

        [TestMethod]
        public void Check_Name() => NativeContract.NNS.Name().Should().Be("NNS");

        [TestMethod]
        public void Check_Symbol() => NativeContract.NNS.Symbol().Should().Be("nns");

        [TestMethod]
        public void Check_Decimals() => NativeContract.NNS.Decimals().Should().Be(0);

        [TestMethod]
        public void Check_SupportedStandards() => NativeContract.NNS.SupportedStandards().Should().BeEquivalentTo(new string[] { "NEP-11", "NEP-10" });

        [TestMethod]
        public void Check_Register()
        {
            var snapshot = Blockchain.Singleton.GetSnapshot();
            UInt160 admin = NativeContract.NEO.GetCommitteeMultiSigAddress(snapshot);

            //Check_RegisterRun
            var ret_registerRootName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA"));
            ret_registerRootName.State.Should().BeTrue();
            ret_registerRootName.Result.Should().Be(true);

            //check_getRootName
            var ret_getRootName = Check_GetRootName(snapshot);
            VM.Types.Array roots = (VM.Types.Array)ret_getRootName.Result;
            ret_getRootName.State.Should().BeTrue();
            roots[roots.Count - 1].Should().Be((ByteString)"aa");

            //check_transfer_create_first-level_domain
            var ret_transfer = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"));
            ret_transfer.Result.Should().BeTrue();
            ret_transfer.State.Should().BeTrue();
        }

        [TestMethod]
        public void Check_Transfer()
        {
            var snapshot = Blockchain.Singleton.GetSnapshot();
            var factor = 1;
            UInt160 admin = NativeContract.NEO.GetCommitteeMultiSigAddress(snapshot);

            //Check_RegisterRun
            var ret_registerRootName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA"));
            ret_registerRootName.Result.Should().Be(true);
            ret_registerRootName.State.Should().BeTrue();

            //check_getRootName
            var ret_getRootName = Check_GetRootName(snapshot);
            VM.Types.Array roots = (VM.Types.Array)ret_getRootName.Result;
            ret_getRootName.State.Should().BeTrue();
            roots[roots.Count - 1].Should().Be((ByteString)"aa");

            //check_transfer wrong amount format
            var ret_transfer = Check_Transfer(snapshot, admin.ToArray(), admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"), factor + 1, true);
            ret_transfer.Result.Should().BeFalse();
            ret_transfer.State.Should().BeFalse();

            //check_transfer wrong domain level
            ret_transfer = Check_Transfer(snapshot, admin.ToArray(), admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA.AA.AA.AA"), factor, true);
            ret_transfer.Result.Should().BeFalse();
            ret_transfer.State.Should().BeTrue();

            //check_transfer transfer root domain wrong
            ret_transfer = Check_Transfer(snapshot, admin.ToArray(), admin.ToArray(), Encoding.UTF8.GetBytes("AA"), factor, true);
            ret_transfer.Result.Should().BeFalse();
            ret_transfer.State.Should().BeTrue();

            //check_transfer cross-level wrong
            ret_transfer = Check_Transfer(snapshot, admin.ToArray(), admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA.AA"), factor, true);
            ret_transfer.Result.Should().BeFalse();
            ret_transfer.State.Should().BeTrue();

            // register sub-domain
            var ret_registerName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"));
            ret_registerName.Result.Should().Be(true);
            ret_registerName.State.Should().BeTrue();

            //check_transfer_create_first-level_domain
            ret_transfer = Check_Transfer(snapshot, admin.ToArray(), admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"), factor, true);
            ret_transfer.Result.Should().BeTrue();
            ret_transfer.State.Should().BeTrue();

            snapshot.BlockHashIndex.GetAndChange().Index = 1;

            //check_transfer expired domain
            ret_transfer = Check_Transfer(snapshot, admin.ToArray(), admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"), factor, true);
            ret_transfer.Result.Should().BeTrue();
            ret_transfer.State.Should().BeTrue();

            // register subdomain

            //check_transfer_create_second&third-level_domain
            ret_transfer = Check_Transfer(snapshot, admin.ToArray(), admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA.AA"), factor, true);
            ret_transfer.Result.Should().BeFalse();
            ret_transfer.State.Should().BeTrue();
        }

        [TestMethod]
        public void Check_Renew()
        {
            var snapshot = Blockchain.Singleton.GetSnapshot();
            UInt160 admin = NativeContract.NEO.GetCommitteeMultiSigAddress(snapshot);

            //Check_RegisterRun
            var ret_registerRootName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA"));
            ret_registerRootName.State.Should().BeTrue();
            ret_registerRootName.Result.Should().Be(true);

            //check_getRootName
            var ret_getRootName = Check_GetRootName(snapshot);
            VM.Types.Array roots = (VM.Types.Array) ret_getRootName.Result;
            ret_getRootName.State.Should().BeTrue();
            roots[roots.Count - 1].Should().Be((ByteString)"aa");

            //check_transfer_create_first-level_domain
            var ret_transfer = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"));
            ret_transfer.Result.Should().BeTrue();
            ret_transfer.State.Should().BeTrue();

            //check_renewName
            var ret_renewName = Check_RenewName(snapshot, admin.ToArray(), UInt160.Zero, Encoding.UTF8.GetBytes("AA.AA"), 100000000L);
            ret_renewName.Result.Should().BeTrue();
            ret_renewName.State.Should().BeTrue();
        }

        internal static (bool State, bool Result) Check_RenewName(StoreView snapshot, byte[] from, UInt160 account, byte[] tokenId, BigInteger height)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(account), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(account);
            script.EmitPush(height);
            script.EmitPush(tokenId);
            script.EmitPush(3);
            script.Emit(OpCode.PACK);
            script.EmitPush("renewName");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.Boolean));

            return (true, result.ToBoolean());
        }

        [TestMethod]
        public void Check_Resolver()
        {
            var snapshot = Blockchain.Singleton.GetSnapshot();
            var factor = 1;
            UInt160 admin = NativeContract.NEO.GetCommitteeMultiSigAddress(snapshot);
            snapshot.BlockHashIndex.GetAndChange().Index = 0;

            //Check_RegisterRun
            var ret_registerRootName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA"));
            ret_registerRootName.Result.Should().Be(true);
            ret_registerRootName.State.Should().BeTrue();

            //check_getRootName
            var ret_getRootName = Check_GetRootName(snapshot);
            VM.Types.Array roots = (VM.Types.Array)ret_getRootName.Result;
            ret_getRootName.State.Should().BeTrue();
            roots[roots.Count - 1].Should().Be((ByteString)"aa");

            // register sub-domain
            var ret_registerName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"));
            ret_registerName.Result.Should().Be(true);
            ret_registerName.State.Should().BeTrue();

            //check_transfer_create_first-level_domain
            var ret_transfer = Check_Transfer(snapshot, admin.ToArray(), admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"), factor, true);
            ret_transfer.Result.Should().BeTrue();
            ret_transfer.State.Should().BeTrue();

            //check_transfer_create_first-level_domain
            ret_transfer = Check_Transfer(snapshot, admin.ToArray(), admin.ToArray(), Encoding.UTF8.GetBytes("BB.AA"), factor, true);
            ret_transfer.Result.Should().BeFalse();
            ret_transfer.State.Should().BeTrue();

            //check_ownerof
            var ret_OwnerOf = Check_OwnerOf(snapshot, Encoding.UTF8.GetBytes("AA.AA"), true);
            IEnumerator eumerator_OwnerOf = ((InteropInterface)ret_OwnerOf.Result).GetInterface<IEnumerator>();
            eumerator_OwnerOf.MoveNext().Should().BeTrue();
            eumerator_OwnerOf.Current.Should().Be(admin);
            ret_OwnerOf.State.Should().BeTrue();

            //check_tokensof
            var ret_TokensOf = Check_TokensOf(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"), true);
            IEnumerator eumerator_TokensOf = ((InteropInterface)ret_TokensOf.Result).GetInterface<IEnumerator>();
            eumerator_TokensOf.MoveNext().Should().BeTrue();
            ((DomainState)(eumerator_TokensOf.Current)).TokenId.Should().BeEquivalentTo(Encoding.ASCII.GetBytes("AA.AA"));
            ret_TokensOf.State.Should().BeTrue();

            //check_balanceOf
            var ret_BalanceOf = Check_BalanceOf(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"), true);
            ret_BalanceOf.Result.GetBigInteger().Should().Be(1);
            ret_BalanceOf.State.Should().BeTrue();

            //check_getProperties
            var ret_properties = Check_GetProperties(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"), true);
            ret_properties.Result.GetString().Should().Be("{}");
            ret_properties.State.Should().BeTrue();

            //check_totalSupply
            var ret_totalSupply = Check_TotalSupply(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"), true);
            ret_totalSupply.Result.GetBigInteger().Should().Be(1);
            ret_totalSupply.State.Should().BeTrue();

            //check_setText
            var ret_setText = Check_SetText(snapshot, admin, Encoding.UTF8.GetBytes("AA.AA"), "BBB", 0, true);
            ret_setText.Result.Should().Be(false);
            ret_setText.State.Should().BeTrue();

            // Register BB.AA
            ret_registerName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("BB.AA"));
            ret_registerName.Result.Should().Be(true);
            ret_registerName.State.Should().BeTrue();

            //check_setText
            ret_setText = Check_SetText(snapshot, admin, Encoding.UTF8.GetBytes("BB.AA"), "AA.AA", 1, true);
            ret_setText.Result.Should().Be(true);
            ret_setText.State.Should().BeTrue();

            // Register cc.aa
            ret_registerName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("CC.AA"));
            ret_registerName.Result.Should().Be(true);
            ret_registerName.State.Should().BeTrue();

            //check_setText
            ret_setText = Check_SetText(snapshot, admin, Encoding.UTF8.GetBytes("CC.AA"), "CC.AA", 3, true);
            ret_setText.Result.Should().Be(true);
            ret_setText.State.Should().BeTrue();

            //check_setText witness wrong
            ret_setText = Check_SetText(snapshot, UInt160.Zero, Encoding.UTF8.GetBytes("CC.AA"), "CC.AA", 3, true);
            ret_setText.Result.Should().Be(false);
            ret_setText.State.Should().BeTrue();

            //check_setText no token
            ret_setText = Check_SetText(snapshot, UInt160.Zero, Encoding.UTF8.GetBytes("EE.AA"), "CC.AA", 3, true);
            ret_setText.Result.Should().Be(false);
            ret_setText.State.Should().BeTrue();

            //check_setOperator
            var ret_setOperator = Check_SetOperator(snapshot, admin, Encoding.UTF8.GetBytes("AA.AA"), true);
            ret_setOperator.Result.Should().Be(true);
            ret_setOperator.State.Should().BeTrue();

            //check_resolve
            var ret_resolve = Check_Resolve(snapshot, Encoding.UTF8.GetBytes("AA.AA"), true);
            Struct @struct = (Struct)ret_resolve.Result;
            @struct[0].GetSpan().ToArray().Should().BeEquivalentTo(new byte[] { 0x04 });
            ret_resolve.State.Should().BeTrue();

            //check_resolve
            ret_resolve = Check_Resolve(snapshot, Encoding.UTF8.GetBytes("BB.AA"), true);
            @struct = (Struct)ret_resolve.Result;
            @struct[0].GetSpan().ToArray().Should().BeEquivalentTo(new byte[] { 0x04 });
            ret_resolve.State.Should().BeTrue();

            //check_resolve
            ret_resolve = Check_Resolve(snapshot, Encoding.UTF8.GetBytes("DD.AA"), true);
            @struct = (Struct)ret_resolve.Result;
            @struct[0].GetSpan().ToArray().Should().BeEquivalentTo(new byte[] { 0x04 });
            ret_resolve.State.Should().BeTrue();
        }

        [TestMethod]
        public void Check_Operator()
        {
            var snapshot = Blockchain.Singleton.GetSnapshot();
            UInt160 admin = NativeContract.NEO.GetCommitteeMultiSigAddress(snapshot);
            snapshot.BlockHashIndex.GetAndChange().Index = 0;

            //Check_RegisterRun
            var ret_registerRootName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA"));
            ret_registerRootName.Result.Should().Be(true);
            ret_registerRootName.State.Should().BeTrue();

            //check_getRootName
            var ret_getRootName = Check_GetRootName(snapshot);
            VM.Types.Array roots = (VM.Types.Array) ret_getRootName.Result;
            roots[roots.Count - 1].Should().Be((ByteString)"aa");

            //check_setOperator
            var ret_setOperator = Check_SetOperator(snapshot, admin, Encoding.UTF8.GetBytes("AA.AA"), true);
            ret_setOperator.Result.Should().Be(false);
            ret_setOperator.State.Should().BeTrue();

            // Register sub domain && and set operator
            ret_registerRootName = Check_RegisterRun(snapshot, admin.ToArray(), Encoding.UTF8.GetBytes("AA.AA"));
            ret_registerRootName.Result.Should().Be(true);
            ret_registerRootName.State.Should().BeTrue();

            ret_setOperator = Check_SetOperator(snapshot, admin, Encoding.UTF8.GetBytes("AA.AA"), true);
            ret_setOperator.Result.Should().Be(true);
            ret_setOperator.State.Should().BeTrue();

            //check_setOperator cross-level
            ret_setOperator = Check_SetOperator(snapshot, admin, Encoding.UTF8.GetBytes("AA.AA.AA.AA"), true);
            ret_setOperator.Result.Should().Be(false);
            ret_setOperator.State.Should().BeTrue();
        }

        [TestMethod()]
        public void IsDomainTest()
        {
            Assert.IsFalse(NativeContract.NNS.IsDomain(""));
            Assert.IsFalse(NativeContract.NNS.IsDomain(null));
            Assert.IsFalse(NativeContract.NNS.IsDomain("www,neo.org"));
            Assert.IsFalse(NativeContract.NNS.IsDomain("www.hello.world.neo.org"));
            Assert.IsTrue(NativeContract.NNS.IsDomain("www.hello.neo.org"));
            Assert.IsTrue(NativeContract.NNS.IsDomain("www.neo.org"));
            Assert.IsTrue(NativeContract.NNS.IsDomain("neo.org"));
            Assert.IsTrue(NativeContract.NNS.IsDomain("bb.aa123"));
        }

        internal static (bool State, StackItem Result) Check_Resolve(StoreView snapshot, byte[] tokenId, bool signAccount)
        {
            var address = NativeContract.NEO.GetCommitteeMultiSigAddress(snapshot);
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(tokenId);
            script.EmitPush(1);
            script.Emit(OpCode.PACK);
            script.EmitPush("resolve");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.Struct));

            return (true, result);
        }

        internal static (bool State, bool Result) Check_SetOperator(StoreView snapshot, UInt160 account, byte[] tokenId, bool signAccount)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(signAccount ? account : UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(account);
            script.EmitPush(tokenId);
            script.EmitPush(2);
            script.Emit(OpCode.PACK);
            script.EmitPush("setOperator");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.Boolean));

            return (true, result.ToBoolean());
        }

        internal static (bool State, bool Result) Check_SetText(StoreView snapshot, UInt160 account, byte[] tokenId, String text, int recordType, bool signAccount)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(signAccount ? account : UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(text);
            script.EmitPush(recordType);
            script.EmitPush(tokenId);
            script.EmitPush(3);
            script.Emit(OpCode.PACK);
            script.EmitPush("setText");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.Boolean));

            return (true, result.ToBoolean());
        }

        internal static (bool State, bool Result) Check_RegisterRun(StoreView snapshot, byte[] account, byte[] tokenId)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(new UInt160(account)), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(account);
            script.EmitPush(tokenId);
            script.EmitPush(2);
            script.Emit(OpCode.PACK);
            script.EmitPush("register");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.Boolean));

            return (true, result.ToBoolean());
        }

        internal static (bool State, StackItem Result) Check_OwnerOf(StoreView snapshot, byte[] tokenId, bool signAccount)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(tokenId);
            script.EmitPush(1);
            script.Emit(OpCode.PACK);
            script.EmitPush("ownerOf");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.InteropInterface));

            return (true, result);
        }

        internal static (bool State, StackItem Result) Check_TokensOf(StoreView snapshot, byte[] account, byte[] tokenId, bool signAccount)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(signAccount ? new UInt160(account) : UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(account);
            script.EmitPush(1);
            script.Emit(OpCode.PACK);
            script.EmitPush("tokensOf");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.InteropInterface));

            return (true, result);
        }

        internal static (bool State, StackItem Result) Check_BalanceOf(StoreView snapshot, byte[] account, byte[] tokenId, bool signAccount)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(signAccount ? new UInt160(account) : UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(tokenId);
            script.EmitPush(account);
            script.EmitPush(2);
            script.Emit(OpCode.PACK);
            script.EmitPush("balanceOf");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.Integer));

            return (true, result);
        }

        internal static (bool State, StackItem Result) Check_GetProperties(StoreView snapshot, byte[] account, byte[] tokenId, bool signAccount)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(signAccount ? new UInt160(account) : UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(account);
            script.EmitPush(1);
            script.Emit(OpCode.PACK);
            script.EmitPush("properties");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.ByteString));

            return (true, result);
        }

        internal static (bool State, StackItem Result) Check_TotalSupply(StoreView snapshot, byte[] account, byte[] tokenId, bool signAccount)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(signAccount ? new UInt160(account) : UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(0);
            script.Emit(OpCode.PACK);
            script.EmitPush("totalSupply");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.Integer));

            return (true, result);
        }


        internal static (bool State, StackItem Result) Check_GetRootName(StoreView snapshot)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(0);
            script.Emit(OpCode.PACK);
            script.EmitPush("getRootName");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.Array));

            return (true, result);
        }

        internal static (bool State, bool Result) Check_Transfer(StoreView snapshot, byte[] from, byte[] to, byte[] tokenId, BigInteger amount, bool signAccount)
        {
            var engine = new ApplicationEngine(TriggerType.Application,
                new Nep5NativeContractExtensions.ManualWitness(signAccount ? new UInt160(from) : UInt160.Zero), snapshot, 0, true);

            engine.LoadScript(NativeContract.NNS.Script);

            var script = new ScriptBuilder();

            script.EmitPush(tokenId);
            script.EmitPush(amount);
            script.EmitPush(to);
            script.EmitPush(from);
            script.EmitPush(4);
            script.Emit(OpCode.PACK);
            script.EmitPush("transfer");
            engine.LoadScript(script.ToArray());

            if (engine.Execute() == VMState.FAULT)
            {
                return (false, false);
            }

            var result = engine.ResultStack.Pop();
            result.Should().BeOfType(typeof(VM.Types.Boolean));

            return (true, result.ToBoolean());
        }
    }
}
