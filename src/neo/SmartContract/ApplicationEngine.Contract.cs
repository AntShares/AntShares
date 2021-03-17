using Neo.Cryptography.ECC;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM.Types;
using System;
using Array = Neo.VM.Types.Array;

namespace Neo.SmartContract
{
    partial class ApplicationEngine
    {
        public static readonly InteropDescriptor System_Contract_Call = Register("System.Contract.Call", nameof(CallContract), 1 << 15, CallFlags.ReadStates | CallFlags.AllowCall);
        public static readonly InteropDescriptor System_Contract_CallNative = Register("System.Contract.CallNative", nameof(CallNativeContract), 0, CallFlags.None);
        public static readonly InteropDescriptor System_Contract_GetCallFlags = Register("System.Contract.GetCallFlags", nameof(GetCallFlags), 1 << 10, CallFlags.None);
        /// <summary>
        /// Calculate corresponding account scripthash for given public key
        /// Warning: check first that input public key is valid, before creating the script.
        /// </summary>
        public static readonly InteropDescriptor System_Contract_CreateStandardAccount = Register("System.Contract.CreateStandardAccount", nameof(CreateStandardAccount), 1 << 8, CallFlags.None);
        public static readonly InteropDescriptor System_Contract_CreateMultisigAccount = Register("System.Contract.CreateMultisigAccount", nameof(CreateMultisigAccount), 1 << 8, CallFlags.None);
        public static readonly InteropDescriptor System_Contract_NativeOnPersist = Register("System.Contract.NativeOnPersist", nameof(NativeOnPersist), 0, CallFlags.States);
        public static readonly InteropDescriptor System_Contract_NativePostPersist = Register("System.Contract.NativePostPersist", nameof(NativePostPersist), 0, CallFlags.States);

        protected internal void CallContract(UInt160 contractHash, string method, CallFlags callFlags, Array args)
        {
            if (method.StartsWith('_')) throw new ArgumentException($"Invalid Method Name: {method}");
            if ((callFlags & ~CallFlags.All) != 0)
                throw new ArgumentOutOfRangeException(nameof(callFlags));

            ContractState contract = NativeContract.ContractManagement.GetContract(Snapshot, contractHash);
            if (contract is null) throw new InvalidOperationException($"Called Contract Does Not Exist: {contractHash}");
            ContractMethodDescriptor md = contract.Manifest.Abi.GetMethod(method, args.Count);
            if (md is null) throw new InvalidOperationException($"Method \"{method}\" with {args.Count} parameter(s) doesn't exist in the contract {contractHash}.");
            bool hasReturnValue = md.ReturnType != ContractParameterType.Void;

            if (!hasReturnValue) CurrentContext.EvaluationStack.Push(StackItem.Null);
            CallContractInternal(contract, md, callFlags, hasReturnValue, args);
        }

        protected internal void CallNativeContract(byte version)
        {
            NativeContract contract = NativeContract.GetContract(CurrentScriptHash);
            if (contract is null)
                throw new InvalidOperationException("It is not allowed to use \"System.Contract.CallNative\" directly.");
            uint[] updates = ProtocolSettings.NativeUpdateHistory[contract.Name];
            if (updates.Length == 0)
                throw new InvalidOperationException($"The native contract {contract.Name} is not active.");
            if (updates[0] > NativeContract.Ledger.CurrentIndex(Snapshot))
                throw new InvalidOperationException($"The native contract {contract.Name} is not active.");

            if (invocationCounter.TryGetValue(contract.Hash, out var counter))
            {
                invocationCounter[contract.Hash] = counter + 1;
            }
            else
            {
                invocationCounter[contract.Hash] = 1;
            }

            contract.Invoke(this, version);
        }

        protected internal CallFlags GetCallFlags()
        {
            var state = CurrentContext.GetState<ExecutionContextState>();
            return state.CallFlags;
        }

        protected internal UInt160 CreateStandardAccount(ECPoint pubKey)
        {
            return Contract.CreateSignatureRedeemScript(pubKey).ToScriptHash();
        }

        protected internal UInt160 CreateMultisigAccount(int m, ECPoint[] pubKeys)
        {
            return Contract.CreateMultiSigRedeemScript(m, pubKeys).ToScriptHash();
        }

        protected internal async void NativeOnPersist()
        {
            try
            {
                if (Trigger != TriggerType.OnPersist)
                    throw new InvalidOperationException();
                foreach (NativeContract contract in NativeContract.Contracts)
                {
                    uint[] updates = ProtocolSettings.NativeUpdateHistory[contract.Name];
                    if (updates.Length == 0) continue;
                    if (updates[0] <= PersistingBlock.Index)
                        await contract.OnPersist(this);
                }
            }
            catch (Exception ex)
            {
                Throw(ex);
            }
        }

        protected internal async void NativePostPersist()
        {
            try
            {
                if (Trigger != TriggerType.PostPersist)
                    throw new InvalidOperationException();
                foreach (NativeContract contract in NativeContract.Contracts)
                {
                    uint[] updates = ProtocolSettings.NativeUpdateHistory[contract.Name];
                    if (updates.Length == 0) continue;
                    if (updates[0] <= PersistingBlock.Index)
                        await contract.PostPersist(this);
                }
            }
            catch (Exception ex)
            {
                Throw(ex);
            }
        }
    }
}
