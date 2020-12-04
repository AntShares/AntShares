using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using System;

namespace Neo.UnitTests.Extensions
{
    public static class NativeContractExtensions
    {
        public static void AddContract(this StoreView snapshot, UInt160 hash, ContractState state)
        {
            var key = new KeyBuilder(NativeContract.Management.Id, 8).Add(hash);
            snapshot.Storages.Add(key, new Neo.Ledger.StorageItem(state, false));
        }

        public static void DeleteContract(this StoreView snapshot, UInt160 hash)
        {
            var key = new KeyBuilder(NativeContract.Management.Id, 8).Add(hash);
            snapshot.Storages.Delete(key);
        }

        public static StackItem Call(this NativeContract contract, StoreView snapshot, string method, params ContractParameter[] args)
        {
            return Call(contract, snapshot, null, method, args);
        }

        public static StackItem Call(this NativeContract contract, StoreView snapshot, IVerifiable container, string method, params ContractParameter[] args)
        {
            var engine = ApplicationEngine.Create(TriggerType.Application, container, snapshot);
            var contractState = NativeContract.Management.GetContract(snapshot, contract.Hash);
            if (contractState == null) throw new InvalidOperationException();

            engine.LoadContract(contractState, method, CallFlags.All, true);

            var script = new ScriptBuilder();

            for (var i = args.Length - 1; i >= 0; i--)
                script.EmitPush(args[i]);

            engine.LoadScript(script.ToArray());

            if (engine.Execute() != VMState.HALT)
            {
                Exception exception = engine.FaultException;
                while (exception?.InnerException != null) exception = exception.InnerException;
                throw exception ?? new InvalidOperationException();
            }

            if (0 < engine.ResultStack.Count)
                return engine.ResultStack.Pop();
            return null;
        }
    }
}
