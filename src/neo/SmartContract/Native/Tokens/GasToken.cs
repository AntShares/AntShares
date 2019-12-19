#pragma warning disable IDE0051

using Neo.Cryptography.ECC;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Linq;
using System.Numerics;
using VMArray = Neo.VM.Types.Array;

namespace Neo.SmartContract.Native.Tokens
{
    public sealed class GasToken : Nep5Token<Nep5AccountState>
    {
        public override string ServiceName => "Neo.Native.Tokens.GAS";
        public override string Name => "GAS";
        public override string Symbol => "gas";
        public override byte Decimals => 8;

        private const byte Prefix_SystemFeeAmount = 15;

        internal GasToken()
        {
        }

        internal override bool Initialize(ApplicationEngine engine)
        {
            if (!base.Initialize(engine)) return false;
            if (TotalSupply(engine.Snapshot) != BigInteger.Zero) return false;
            UInt160 account = Contract.CreateMultiSigRedeemScript(Blockchain.StandbyValidators.Length / 2 + 1, Blockchain.StandbyValidators).ToScriptHash();
            Mint(engine, account, 30_000_000 * Factor);
            return true;
        }

        /// <summary>
        /// The paid final paid amount is calculated based based on the sysfee and sysfeeCredit
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        private long CalculateSystemFeeWithPayback(Transaction tx)
        {
            long factor = (long)GAS.Factor;
            long finalFee = Math.Max(tx.SystemFee + tx.SysFeeCredit, 0);
            long remainder = finalFee % factor;
            if (remainder > 0)
                finalFee += factor - remainder;

            return finalFee;
        }

        protected override bool OnPersist(ApplicationEngine engine)
        {
            if (!base.OnPersist(engine)) return false;
            long userPaidFees = 0;
            foreach (Transaction tx in engine.Snapshot.PersistingBlock.Transactions)
            {
                var sysFee = CalculateSystemFeeWithPayback(tx);
                Burn(engine, tx.Sender, userPaidFees + tx.NetworkFee);
                userPaidFees += userPaidFees;
            }
            ECPoint[] validators = NEO.GetNextBlockValidators(engine.Snapshot);
            UInt160 primary = Contract.CreateSignatureRedeemScript(validators[engine.Snapshot.PersistingBlock.ConsensusData.PrimaryIndex]).ToScriptHash();
            Mint(engine, primary, engine.Snapshot.PersistingBlock.Transactions.Sum(p => p.NetworkFee));
            BigInteger sys_fee = GetSysFeeAmount(engine.Snapshot, engine.Snapshot.PersistingBlock.Index - 1) + userPaidFees;
            StorageKey key = CreateStorageKey(Prefix_SystemFeeAmount, BitConverter.GetBytes(engine.Snapshot.PersistingBlock.Index));
            engine.Snapshot.Storages.Add(key, new StorageItem
            {
                Value = sys_fee.ToByteArrayStandard(),
                IsConstant = true
            });
            return true;
        }

        [ContractMethod(0_01000000, ContractParameterType.Integer, ParameterTypes = new[] { ContractParameterType.Integer }, ParameterNames = new[] { "index" }, SafeMethod = true)]
        private StackItem GetSysFeeAmount(ApplicationEngine engine, VMArray args)
        {
            uint index = (uint)args[0].GetBigInteger();
            return GetSysFeeAmount(engine.Snapshot, index);
        }

        public BigInteger GetSysFeeAmount(StoreView snapshot, uint index)
        {
            if (index == 0) return Blockchain.GenesisBlock.Transactions.Sum(p => p.SystemFee);
            StorageKey key = CreateStorageKey(Prefix_SystemFeeAmount, BitConverter.GetBytes(index));
            StorageItem storage = snapshot.Storages.TryGet(key);
            if (storage is null) return BigInteger.Zero;
            return new BigInteger(storage.Value);
        }
    }
}
