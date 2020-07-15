#pragma warning disable IDE0051

using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.Ledger;
using Neo.Persistence;
using Neo.VM;
using Neo.VM.Types;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Neo.SmartContract.Native.Tokens
{
    public partial class NeoToken
    {
        private const byte Prefix_VotersCount = 1;
        private const byte Prefix_Candidate = 13;
        private const byte Prefix_NextValidators = 77;

        public const decimal EffectiveVoterTurnout = 0.2M;

        [ContractMethod(0_05000000, CallFlags.AllowModifyStates)]
        private bool RegisterCandidate(ApplicationEngine engine, ECPoint pubkey)
        {
            if (!engine.CheckWitnessInternal(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash()))
                return false;
            RegisterCandidateInternal(engine.Snapshot, pubkey);
            return true;
        }

        private void RegisterCandidateInternal(StoreView snapshot, ECPoint pubkey)
        {
            StorageKey key = CreateStorageKey(Prefix_Candidate).Add(pubkey);
            StorageItem item = snapshot.Storages.GetAndChange(key, () => new StorageItem(new CandidateState()));
            CandidateState state = item.GetInteroperable<CandidateState>();
            state.Registered = true;
        }

        [ContractMethod(0_05000000, CallFlags.AllowModifyStates)]
        private bool UnregisterCandidate(ApplicationEngine engine, ECPoint pubkey)
        {
            if (!engine.CheckWitnessInternal(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash()))
                return false;
            StorageKey key = CreateStorageKey(Prefix_Candidate).Add(pubkey);
            if (engine.Snapshot.Storages.TryGet(key) is null) return true;
            StorageItem item = engine.Snapshot.Storages.GetAndChange(key);
            CandidateState state = item.GetInteroperable<CandidateState>();
            if (state.Votes.IsZero)
                engine.Snapshot.Storages.Delete(key);
            else
                state.Registered = false;
            return true;
        }

        [ContractMethod(5_00000000, CallFlags.AllowModifyStates)]
        private bool Vote(ApplicationEngine engine, UInt160 account, ECPoint voteTo)
        {
            if (!engine.CheckWitnessInternal(account)) return false;
            StorageKey key_account = CreateStorageKey(Prefix_Account).Add(account);
            if (engine.Snapshot.Storages.TryGet(key_account) is null) return false;
            StorageItem storage_account = engine.Snapshot.Storages.GetAndChange(key_account);
            NeoAccountState state_account = storage_account.GetInteroperable<NeoAccountState>();
            if (state_account.VoteTo is null ^ voteTo is null)
            {
                StorageItem item = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_VotersCount));
                BigInteger votersCount = new BigInteger(item.Value);
                if (state_account.VoteTo is null)
                    votersCount += state_account.Balance;
                else
                    votersCount -= state_account.Balance;
                item.Value = votersCount.ToByteArray();
            }
            if (state_account.VoteTo != null)
            {
                StorageKey key = CreateStorageKey(Prefix_Candidate).Add(state_account.VoteTo);
                StorageItem storage_validator = engine.Snapshot.Storages.GetAndChange(key);
                CandidateState state_validator = storage_validator.GetInteroperable<CandidateState>();
                state_validator.Votes -= state_account.Balance;
                if (!state_validator.Registered && state_validator.Votes.IsZero)
                    engine.Snapshot.Storages.Delete(key);
            }
            state_account.VoteTo = voteTo;
            if (voteTo != null)
            {
                StorageKey key = CreateStorageKey(Prefix_Candidate).Add(voteTo);
                if (engine.Snapshot.Storages.TryGet(key) is null) return false;
                StorageItem storage_validator = engine.Snapshot.Storages.GetAndChange(key);
                CandidateState state_validator = storage_validator.GetInteroperable<CandidateState>();
                if (!state_validator.Registered) return false;
                state_validator.Votes += state_account.Balance;
            }
            return true;
        }

        [ContractMethod(1_00000000, CallFlags.AllowStates)]
        public (ECPoint PublicKey, BigInteger Votes)[] GetCandidates(StoreView snapshot)
        {
            byte[] prefix_key = CreateStorageKey(Prefix_Candidate).ToArray();
            return snapshot.Storages.Find(prefix_key).Select(p =>
            (
                p.Key.Key.AsSerializable<ECPoint>(1),
                p.Value.GetInteroperable<CandidateState>()
            )).Where(p => p.Item2.Registered).Select(p => (p.Item1, p.Item2.Votes)).ToArray();
        }

        [ContractMethod(1_00000000, CallFlags.AllowStates)]
        public ECPoint[] GetValidators(StoreView snapshot)
        {
            return GetCommitteeMembers(snapshot).Take(ProtocolSettings.Default.ValidatorsCount).OrderBy(p => p).ToArray();
        }

        [ContractMethod(1_00000000, CallFlags.AllowStates)]
        public ECPoint[] GetCommittee(StoreView snapshot)
        {
            return GetCommitteeMembers(snapshot).OrderBy(p => p).ToArray();
        }

        public UInt160 GetCommitteeAddress(StoreView snapshot)
        {
            ECPoint[] committees = GetCommittee(snapshot);
            return Contract.CreateMultiSigRedeemScript(committees.Length - (committees.Length - 1) / 2, committees).ToScriptHash();
        }

        private IEnumerable<ECPoint> GetCommitteeMembers(StoreView snapshot)
        {
            decimal votersCount = (decimal)new BigInteger(snapshot.Storages[CreateStorageKey(Prefix_VotersCount)].Value);
            decimal VoterTurnout = votersCount / (decimal)TotalAmount;
            if (VoterTurnout < EffectiveVoterTurnout)
                return Blockchain.StandbyCommittee;
            var candidates = GetCandidates(snapshot);
            if (candidates.Length < ProtocolSettings.Default.CommitteeMembersCount)
                return Blockchain.StandbyCommittee;
            return candidates.OrderByDescending(p => p.Votes).ThenBy(p => p.PublicKey).Select(p => p.PublicKey).Take(ProtocolSettings.Default.CommitteeMembersCount);
        }

        private (ECPoint PublicKey, BigInteger Votes)[] GetCommitteeVotes(StoreView snapshot)
        {
            (ECPoint PublicKey, BigInteger Votes)[] committeeVotes = new (ECPoint PublicKey, BigInteger Votes)[ProtocolSettings.Default.CommitteeMembersCount];
            var i = 0;
            foreach (var commiteePubKey in GetCommitteeMembers(snapshot))
            {
                var item = snapshot.Storages.TryGet(CreateStorageKey(Prefix_Candidate).Add(commiteePubKey));
                if (item is null)
                    committeeVotes[i] = (commiteePubKey, BigInteger.Zero);
                else
                    committeeVotes[i] = (commiteePubKey, item.GetInteroperable<CandidateState>().Votes);
                i++;
            }
            return committeeVotes;
        }

        [ContractMethod(1_00000000, CallFlags.AllowStates)]
        public ECPoint[] GetNextBlockValidators(StoreView snapshot)
        {
            StorageItem storage = snapshot.Storages.TryGet(CreateStorageKey(Prefix_NextValidators));
            if (storage is null) return Blockchain.StandbyValidators;
            return storage.Value.AsSerializableArray<ECPoint>();
        }

        public bool CheckCommittees(ApplicationEngine engine)
        {
            UInt160 committeeMultiSigAddr = NEO.GetCommitteeAddress(engine.Snapshot);
            return engine.CheckWitnessInternal(committeeMultiSigAddr);
        }

        public class NeoAccountState : AccountState
        {
            public uint BalanceHeight;
            public ECPoint VoteTo;

            public override void FromStackItem(StackItem stackItem)
            {
                base.FromStackItem(stackItem);
                Struct @struct = (Struct)stackItem;
                BalanceHeight = (uint)@struct[1].GetInteger();
                VoteTo = @struct[2].IsNull ? null : @struct[2].GetSpan().AsSerializable<ECPoint>();
            }

            public override StackItem ToStackItem(ReferenceCounter referenceCounter)
            {
                Struct @struct = (Struct)base.ToStackItem(referenceCounter);
                @struct.Add(BalanceHeight);
                @struct.Add(VoteTo?.ToArray() ?? StackItem.Null);
                return @struct;
            }
        }

        internal class CandidateState : IInteroperable
        {
            public bool Registered = true;
            public BigInteger Votes;

            public void FromStackItem(StackItem stackItem)
            {
                Struct @struct = (Struct)stackItem;
                Registered = @struct[0].GetBoolean();
                Votes = @struct[1].GetInteger();
            }

            public StackItem ToStackItem(ReferenceCounter referenceCounter)
            {
                return new Struct(referenceCounter) { Registered, Votes };
            }
        }
    }
}
