﻿using System.Linq;
using Neo.Ledger;
using Neo.Models;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;

namespace Neo.Network.P2P.Payloads
{
    public class VerifiableTransaction : Transaction, IVerifiable
    {
        public VerifiableTransaction() : base(ProtocolSettings.Default.Magic)
        {
        }

        public InventoryType InventoryType => InventoryType.TX;

        public UInt160[] GetScriptHashesForVerifying(StoreView snapshot)
        {
            return Signers.Select(p => p.Account).ToArray();
        }

        public bool Verify(StoreView snapshot)
        {
            return Verify(snapshot, null) == VerifyResult.Succeed;
        }

        public virtual VerifyResult VerifyStateDependent(StoreView snapshot, TransactionVerificationContext context)
        {
            if (ValidUntilBlock <= snapshot.Height || ValidUntilBlock > snapshot.Height + MaxValidUntilBlockIncrement)
                return VerifyResult.Expired;
            UInt160[] hashes = GetScriptHashesForVerifying(snapshot);
            if (NativeContract.Policy.IsAnyAccountBlocked(snapshot, hashes))
                return VerifyResult.PolicyFail;
            if (NativeContract.Policy.GetMaxBlockSystemFee(snapshot) < SystemFee)
                return VerifyResult.PolicyFail;
            if (!(context?.CheckTransaction(this, snapshot) ?? true)) return VerifyResult.InsufficientFunds;
            foreach (TransactionAttribute attribute in Attributes)
                if (!attribute.Verify(snapshot, this))
                    return VerifyResult.Invalid;
            long net_fee = NetworkFee - Size * NativeContract.Policy.GetFeePerByte(snapshot);
            if (!this.VerifyWitnesses(snapshot, net_fee, WitnessFlag.StateDependent))
                return VerifyResult.Invalid;
            return VerifyResult.Succeed;
        }

        public virtual VerifyResult VerifyStateIndependent()
        {
            if (Size > Transaction.MaxTransactionSize)
                return VerifyResult.Invalid;
            if (!this.VerifyWitnesses(null, NetworkFee, WitnessFlag.StateIndependent))
                return VerifyResult.Invalid;
            return VerifyResult.Succeed;
        }

        public virtual VerifyResult Verify(StoreView snapshot, TransactionVerificationContext context)
        {
            VerifyResult result = VerifyStateIndependent();
            if (result != VerifyResult.Succeed) return result;
            result = VerifyStateDependent(snapshot, context);
            return result;
        }

        // public IWitnessed AsWitnessed()
        // {
        //     return transaction;
        // }
    }
}