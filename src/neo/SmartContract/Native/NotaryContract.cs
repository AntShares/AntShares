using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Linq;
using System.Numerics;
using Array = Neo.VM.Types.Array;

namespace Neo.SmartContract.Native
{
    /// <summary>
    /// The native Notary service for NEO system.
    /// </summary>
    public sealed class NotaryContract : NativeContract
    {
        private const long DefaultNotaryServiceFeePerKey = 1000_0000;
        private const int DefaultDepositDeltaTill = 5760;
        private const int DefaultMaxNotValidBeforeDelta = 140;

        private const byte PrefixDeposit = 0x01;
        private const byte PreMaxNotValidBeforeDelta = 0x10;
        private const byte PreNotaryServiceFeePerKey = 0x05;

        internal NotaryContract()
        {
        }

        internal override ContractTask Initialize(ApplicationEngine engine)
        {
            engine.Snapshot.Add(CreateStorageKey(PreMaxNotValidBeforeDelta), new StorageItem(DefaultMaxNotValidBeforeDelta));
            engine.Snapshot.Add(CreateStorageKey(PreNotaryServiceFeePerKey), new StorageItem(DefaultNotaryServiceFeePerKey));
            return ContractTask.CompletedTask;
        }

        internal override async ContractTask OnPersist(ApplicationEngine engine)
        {
            long nFees = 0;
            ECPoint[] notaries = null;
            foreach (Transaction tx in engine.PersistingBlock.Transactions)
            {
                if (tx.GetAttribute<NotaryAssisted>() is not null)
                {
                    if (notaries is null) notaries = GetNotaryNodes(engine.Snapshot);
                }
                var nKeys = tx.GetAttributes<NotaryAssisted>().ToArray()[0].NKeys;
                nFees = (long)nKeys + 1;
                if (tx.Sender == Notary.Hash)
                {
                    var payer = tx.Signers[1];
                    var balance = GetDepositFor(engine.Snapshot, payer.Account);
                    balance.amount -= tx.SystemFee + tx.NetworkFee;
                    if (balance.amount.Sign == 0) RemoveDepositFor(engine.Snapshot, payer.Account);
                    else PutDepositFor(engine, payer.Account, balance);
                }
            }
            if (nFees == 0) return;
            var singleReward = CalculateNotaryReward(engine.Snapshot, nFees, notaries.Length);
            foreach (var notary in notaries) await GAS.Mint(engine, notary.EncodePoint(true).ToScriptHash(), singleReward, false);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.States)]
        private StackItem OnNEP17Payment(ApplicationEngine engine, UInt160 from, BigInteger amount, StackItem data)
        {
            if (engine.CallingScriptHash != GAS.Hash) throw new Exception(string.Format("only GAS can be accepted for deposit, got {0}", engine.CallingScriptHash.ToString()));
            var to = from;
            var additionalParams = (Array)data;
            if (additionalParams.Count != 2) throw new Exception("`data` parameter should be an array of 2 elements");
            if (!additionalParams[0].Equals(StackItem.Null)) to = additionalParams[0].GetSpan().AsSerializable<UInt160>();
            var tx = engine.GetScriptContainer().GetSpan().AsSerializable<Transaction>();
            var allowedChangeTill = tx.Sender == to;
            var currentHeight = Ledger.CurrentIndex(engine.Snapshot);
            Deposit deposit = GetDepositFor(engine.Snapshot, to);
            var till = (uint)additionalParams[1].GetInteger();
            if (till < currentHeight) throw new Exception(string.Format("`till` shouldn't be less then the chain's height {0}", currentHeight));
            if (deposit != null && till < deposit.till) throw new Exception(string.Format("`till` shouldn't be less then the previous value {0}", deposit.till));
            if (deposit is null)
            {
                if (amount.CompareTo(2 * GetNotaryServiceFeePerKey(engine.Snapshot)) < 0) throw new Exception(string.Format("first deposit can not be less then {0}, got {1}", 2 * GetNotaryServiceFeePerKey(engine.Snapshot), amount));
                deposit = new Deposit() { amount = 0, till = 0 };
                if (!allowedChangeTill) till = currentHeight + DefaultDepositDeltaTill;
            }
            else if (!allowedChangeTill) till = deposit.till;
            deposit.amount += amount;
            deposit.till = till;
            PutDepositFor(engine, to, deposit);
            return StackItem.Null;
        }

        /// <summary>
        /// Lock asset until the specified height is unlocked
        /// </summary>
        /// <param name="engine">ApplicationEngine</param>
        /// <param name="addr">Account</param>
        /// <param name="till">specified height</param>
        /// <returns>result</returns>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.States)]
        private bool LockDepositUntil(ApplicationEngine engine, UInt160 addr, uint till)
        {
            if (engine.CheckWitnessInternal(addr)) return false;
            if (till < Ledger.CurrentIndex(engine.Snapshot)) return false;
            Deposit deposit = GetDepositFor(engine.Snapshot, addr);
            if (deposit is null) return false;
            if (till < deposit.till) return false;
            deposit.till = till;
            PutDepositFor(engine, addr, deposit);
            return true;
        }

        /// <summary>
        /// Withdraw sends all deposited GAS for "from" address to "to" address.
        /// </summary>
        /// <param name="engine">ApplicationEngine</param>
        /// <param name="from">From Account</param>
        /// <param name="to">To Account</param>
        /// <returns>void</returns>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.States)]
        private async ContractTask Withdraw(ApplicationEngine engine, UInt160 from, UInt160 to)
        {
            if (engine.CheckWitnessInternal(from)) throw new InvalidOperationException(string.Format("Failed to check witness for {0}", from.ToString()));
            Deposit deposit = GetDepositFor(engine.Snapshot, from);
            if (deposit is null) throw new InvalidOperationException(string.Format("Deposit of {0} is null", from.ToString()));
            if (Ledger.CurrentIndex(engine.Snapshot) < deposit.till) throw new InvalidOperationException(string.Format("Can't withdraw before {0}", deposit.till));
            await GAS.Burn(engine, from, deposit.amount);
            await GAS.Mint(engine, to, deposit.amount, true);
            RemoveDepositFor(engine.Snapshot, from);
        }

        /// <summary>
        /// BalanceOf returns deposited GAS amount for specified address.
        /// </summary>
        /// <param name="snapshot">DataCache</param>
        /// <param name="acc">Account</param>
        /// <returns></returns>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public BigInteger BalanceOf(DataCache snapshot, UInt160 acc)
        {
            Deposit deposit = GetDepositFor(snapshot, acc);
            if (deposit is null) return 0;
            return deposit.amount;
        }

        /// <summary>
        /// ExpirationOf Returns deposit lock height for specified address.
        /// </summary>
        /// <param name="snapshot">DataCache</param>
        /// <param name="acc">Account</param>
        /// <returns></returns>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint ExpirationOf(DataCache snapshot, UInt160 acc)
        {
            Deposit deposit = GetDepositFor(snapshot, acc);
            if (deposit is null) return 0;
            return deposit.till;
        }

        /// <summary>
        /// Verify checks whether the transaction was signed by one of the notaries.
        /// </summary>
        /// <param name="engine">ApplicationEngine</param>
        /// <param name="sig">Signature</param>
        /// <returns></returns>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        private bool Verify(ApplicationEngine engine, byte[] sig)
        {
            Transaction tx = (Transaction)engine.ScriptContainer;
            if (tx.GetAttribute<NotaryAssisted>() is null) return false;
            foreach (var signer in tx.Signers)
            {
                if (signer.Account == Notary.Hash)
                {
                    if (signer.Scopes != WitnessScope.None) return false;
                    break;
                }
            }
            if (tx.Sender == Notary.Hash)
            {
                if (tx.Signers.Length != 2) return false;
                var payer = tx.Signers[1].Account;
                var balance = GetDepositFor(engine.Snapshot, payer);
                if (balance is null || balance.amount.CompareTo((tx.NetworkFee + tx.SystemFee)) < 0) return false;
            }
            ECPoint[] notaries = GetNotaryNodes(engine.Snapshot);
            var hash = tx.Hash.ToArray();
            var verified = false;
            foreach (var n in notaries)
            {
                if (Crypto.VerifySignature(hash, sig, n))
                {
                    verified = true;
                    break;
                }
            }
            return verified;
        }

        /// <summary>
        /// GetNotaryNodes returns public keys of notary nodes.
        /// </summary>
        /// <param name="snapshot">DataCache</param>
        /// <returns></returns>
        private ECPoint[] GetNotaryNodes(DataCache snapshot)
        {
            ECPoint[] nodes = RoleManagement.GetDesignatedByRole(snapshot, Role.Notary, uint.MaxValue);
            return nodes;
        }

        /// <summary>
        /// GetMaxNotValidBeforeDelta is Notary contract method and returns the maximum NotValidBefore delta.
        /// </summary>
        /// <param name="snapshot">DataCache</param>
        /// <returns>NotValidBefore</returns>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint GetMaxNotValidBeforeDelta(DataCache snapshot)
        {
            return (uint)(BigInteger)snapshot[CreateStorageKey(PreMaxNotValidBeforeDelta)];
        }

        /// <summary>
        /// SetMaxNotValidBeforeDelta is Notary contract method and sets the maximum NotValidBefore delta.
        /// </summary>
        /// <param name="engine">ApplicationEngine</param>
        /// <param name="value">Value</param>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.States)]
        private void SetMaxNotValidBeforeDelta(ApplicationEngine engine, uint value)
        {
            if (value > Transaction.MaxValidUntilBlockIncrement / 2 || value < ProtocolSettings.Default.ValidatorsCount) throw new FormatException(string.Format("MaxNotValidBeforeDelta cannot be more than {0} or less than {1}", Transaction.MaxValidUntilBlockIncrement / 2, ProtocolSettings.Default.ValidatorsCount));
            if (!CheckCommittee(engine)) throw new InvalidOperationException();
            engine.Snapshot.GetAndChange(CreateStorageKey(PreMaxNotValidBeforeDelta)).Set(value);
        }

        /// <summary>
        /// GetNotaryServiceFeePerKey is Notary contract method and returns the NotaryServiceFeePerKey delta.
        /// </summary>
        /// <param name="snapshot">DataCache</param>
        /// <returns>NotaryServiceFeePerKey</returns>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public long GetNotaryServiceFeePerKey(DataCache snapshot)
        {
            return (long)(BigInteger)snapshot[CreateStorageKey(PreNotaryServiceFeePerKey)];
        }

        /// <summary>
        /// SetNotaryServiceFeePerKey is Notary contract method and sets the NotaryServiceFeePerKey delta.
        /// </summary>
        /// <param name="engine">ApplicationEngine</param>
        /// <param name="value">value</param>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.States)]
        private void SetNotaryServiceFeePerKey(ApplicationEngine engine, long value)
        {
            if (value < 0) throw new FormatException("NotaryServiceFeePerKey cannot be less than 0");
            if (!CheckCommittee(engine)) throw new InvalidOperationException();
            engine.Snapshot.GetAndChange(CreateStorageKey(PreNotaryServiceFeePerKey)).Set(value);
        }

        /// <summary>
        /// GetDepositFor returns state.Deposit for the account specified. It returns nil in case if
        /// deposit is not found in storage and panics in case of any other error.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="acc"></param>
        /// <returns></returns>
        private Deposit GetDepositFor(DataCache snapshot, UInt160 acc)
        {
            Deposit deposit = snapshot.TryGet(CreateStorageKey(PrefixDeposit).Add(acc.ToArray()))?.GetInteroperable<Deposit>();
            if (deposit is null) throw new Exception(string.Format("failed to get deposit for {0} from storage", acc.ToString()));
            return deposit;
        }

        /// <summary>
        /// PutDepositFor puts deposit on the balance of the specified account in the storage.
        /// </summary>
        /// <param name="engine">ApplicationEngine</param>
        /// <param name="acc">Account</param>
        /// <param name="deposit">deposit</param>
        private void PutDepositFor(ApplicationEngine engine, UInt160 acc, Deposit deposit)
        {
            engine.Snapshot.Add(CreateStorageKey(PrefixDeposit).Add(acc.ToArray()), new StorageItem(deposit));
        }

        /// <summary>
        /// RemoveDepositFor removes deposit from the storage.
        /// </summary>
        /// <param name="snapshot">DataCache</param>
        /// <param name="acc">Account</param>
        private void RemoveDepositFor(DataCache snapshot, UInt160 acc)
        {
            snapshot.Delete(CreateStorageKey(PrefixDeposit).Add(acc.ToArray()));
        }

        /// <summary>
        /// CalculateNotaryReward calculates the reward for a single notary node based on FEE's count and Notary nodes count.
        /// </summary>
        /// <param name="snapshot">DataCache</param>
        /// <param name="nFees"></param>
        /// <param name="notariesCount"></param>
        /// <returns>result</returns>
        private long CalculateNotaryReward(DataCache snapshot, long nFees, int notariesCount)
        {
            return nFees * GetNotaryServiceFeePerKey(snapshot) / notariesCount;
        }

        public class Deposit : IInteroperable
        {
            public BigInteger amount;
            public uint till;

            public void FromStackItem(StackItem stackItem)
            {
                Struct @struct = (Struct)stackItem;
                amount = @struct[0].GetInteger();
                till = (uint)@struct[1].GetInteger();
            }

            public StackItem ToStackItem(ReferenceCounter referenceCounter)
            {
                return new Struct(referenceCounter) { amount, till };
            }
        }
    }
}
