#pragma warning disable IDE0051

using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.IO.Caching;
using Neo.Ledger;
using Neo.Persistence;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Neo.SmartContract.Native
{
    public sealed class NeoToken : FungibleToken<NeoToken.NeoAccountState>
    {
        public override string Symbol => "NEO";
        public override byte Decimals => 0;
        public BigInteger TotalAmount { get; }

        public const decimal EffectiveVoterTurnout = 0.2M;

        private const byte Prefix_VotersCount = 1;
        private const byte Prefix_Candidate = 33;
        private const byte Prefix_Committee = 14;
        private const byte Prefix_GasPerBlock = 29;
        private const byte Prefix_VoterRewardPerCommittee = 23;

        private const byte NeoHolderRewardRatio = 10;
        private const byte CommitteeRewardRatio = 10;
        private const byte VoterRewardRatio = 80;

        internal NeoToken()
        {
            this.TotalAmount = 100000000 * Factor;
        }

        public override BigInteger TotalSupply(StoreView snapshot)
        {
            return TotalAmount;
        }

        protected override void OnBalanceChanging(ApplicationEngine engine, UInt160 account, NeoAccountState state, BigInteger amount)
        {
            DistributeGas(engine, account, state);
            if (amount.IsZero) return;
            if (state.VoteTo is null) return;
            engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_VotersCount)).Add(amount);
            StorageKey key = CreateStorageKey(Prefix_Candidate).Add(state.VoteTo);
            CandidateState candidate = engine.Snapshot.Storages.GetAndChange(key).GetInteroperable<CandidateState>();
            candidate.Votes += amount;
            CheckCandidate(engine.Snapshot, state.VoteTo, candidate);
        }

        private void DistributeGas(ApplicationEngine engine, UInt160 account, NeoAccountState state)
        {
            // PersistingBlock is null when running under the debugger
            if (engine.PersistingBlock is null) return;

            BigInteger gas = CalculateBonus(engine.Snapshot, state.VoteTo, state.Balance, state.BalanceHeight, engine.PersistingBlock.Index);
            state.BalanceHeight = engine.PersistingBlock.Index;
            GAS.Mint(engine, account, gas, true);
        }

        private BigInteger CalculateBonus(StoreView snapshot, ECPoint vote, BigInteger value, uint start, uint end)
        {
            if (value.IsZero || start >= end) return BigInteger.Zero;
            if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));

            BigInteger neoHolderReward = CalculateNeoHolderReward(snapshot, value, start, end);
            if (vote is null) return neoHolderReward;

            byte[] border = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(vote).ToArray();
            byte[] keyStart = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(vote).AddBigEndian(start).ToArray();
            (_, var item) = snapshot.Storages.FindRange(keyStart, border, SeekDirection.Backward).FirstOrDefault();
            BigInteger startRewardPerNeo = item ?? BigInteger.Zero;

            byte[] keyEnd = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(vote).AddBigEndian(end).ToArray();
            (_, item) = snapshot.Storages.FindRange(keyEnd, border, SeekDirection.Backward).FirstOrDefault();
            BigInteger endRewardPerNeo = item ?? BigInteger.Zero;

            return neoHolderReward + value * (endRewardPerNeo - startRewardPerNeo) / 100000000L;
        }

        private BigInteger CalculateNeoHolderReward(StoreView snapshot, BigInteger value, uint start, uint end)
        {
            BigInteger sum = 0;
            foreach (var (index, gasPerBlock) in GetSortedGasRecords(snapshot, end - 1))
            {
                if (index > start)
                {
                    sum += gasPerBlock * (end - index);
                    end = index;
                }
                else
                {
                    sum += gasPerBlock * (end - start);
                    break;
                }
            }
            return value * sum * NeoHolderRewardRatio / 100 / TotalAmount;
        }

        private void CheckCandidate(StoreView snapshot, ECPoint pubkey, CandidateState candidate)
        {
            if (!candidate.Registered && candidate.Votes.IsZero)
            {
                foreach (var (rewardKey, _) in snapshot.Storages.Find(CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(pubkey).ToArray()).ToArray())
                    snapshot.Storages.Delete(rewardKey);
                snapshot.Storages.Delete(CreateStorageKey(Prefix_Candidate).Add(pubkey));
            }
        }

        public bool ShouldRefreshCommittee(uint height) => height % ProtocolSettings.Default.CommitteeMembersCount == 0;

        internal override void Initialize(ApplicationEngine engine)
        {
            var cachedCommittee = new CachedCommittee(Blockchain.StandbyCommittee.Select(p => (p, BigInteger.Zero)));
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_Committee), new StorageItem(cachedCommittee));
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_VotersCount), new StorageItem(new byte[0]));

            // Initialize economic parameters

            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_GasPerBlock).AddBigEndian(0u), new StorageItem(5 * GAS.Factor));
            Mint(engine, Blockchain.GetConsensusAddress(Blockchain.StandbyValidators), TotalAmount, false);
        }

        internal override void OnPersist(ApplicationEngine engine)
        {
            // Set next committee
            if (ShouldRefreshCommittee(engine.PersistingBlock.Index))
            {
                StorageItem storageItem = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_Committee));
                var cachedCommittee = storageItem.GetInteroperable<CachedCommittee>();
                cachedCommittee.Clear();
                cachedCommittee.AddRange(ComputeCommitteeMembers(engine.Snapshot));
            }
        }

        internal override void PostPersist(ApplicationEngine engine)
        {
            // Distribute GAS for committee

            int m = ProtocolSettings.Default.CommitteeMembersCount;
            int n = ProtocolSettings.Default.ValidatorsCount;
            int index = (int)(engine.PersistingBlock.Index % (uint)m);
            var gasPerBlock = GetGasPerBlock(engine.Snapshot);
            var committee = GetCommitteeFromCache(engine.Snapshot);
            var pubkey = committee.ElementAt(index).PublicKey;
            var account = Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();
            GAS.Mint(engine, account, gasPerBlock * CommitteeRewardRatio / 100, false);

            // Record the cumulative reward of the voters of committee

            if (ShouldRefreshCommittee(engine.PersistingBlock.Index))
            {
                BigInteger voterRewardOfEachCommittee = gasPerBlock * VoterRewardRatio * 100000000L * m / (m + n) / 100; // Zoom in 100000000 times, and the final calculation should be divided 100000000L
                for (index = 0; index < committee.Count; index++)
                {
                    var member = committee.ElementAt(index);
                    var factor = index < n ? 2 : 1; // The `voter` rewards of validator will double than other committee's
                    if (member.Votes > 0)
                    {
                        BigInteger voterSumRewardPerNEO = factor * voterRewardOfEachCommittee / member.Votes;
                        StorageKey voterRewardKey = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(member.PublicKey).AddBigEndian(engine.PersistingBlock.Index + 1);
                        byte[] border = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(member.PublicKey).ToArray();
                        (_, var item) = engine.Snapshot.Storages.FindRange(voterRewardKey.ToArray(), border, SeekDirection.Backward).FirstOrDefault();
                        voterSumRewardPerNEO += (item ?? BigInteger.Zero);
                        engine.Snapshot.Storages.Add(voterRewardKey, new StorageItem(voterSumRewardPerNEO));
                    }
                }
            }
        }

        [ContractMethod(0_05000000, CallFlags.WriteStates)]
        private bool SetGasPerBlock(ApplicationEngine engine, BigInteger gasPerBlock)
        {
            if (gasPerBlock < 0 || gasPerBlock > 10 * GAS.Factor)
                throw new ArgumentOutOfRangeException(nameof(gasPerBlock));
            if (!CheckCommittee(engine)) return false;

            uint index = engine.PersistingBlock.Index + 1;
            StorageItem entry = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_GasPerBlock).AddBigEndian(index), () => new StorageItem(gasPerBlock));
            entry.Set(gasPerBlock);
            return true;
        }

        [ContractMethod(0_01000000, CallFlags.ReadStates)]
        public BigInteger GetGasPerBlock(StoreView snapshot)
        {
            return GetSortedGasRecords(snapshot, snapshot.Height + 1).First().GasPerBlock;
        }

        private IEnumerable<(uint Index, BigInteger GasPerBlock)> GetSortedGasRecords(StoreView snapshot, uint end)
        {
            byte[] key = CreateStorageKey(Prefix_GasPerBlock).AddBigEndian(end).ToArray();
            byte[] boundary = CreateStorageKey(Prefix_GasPerBlock).ToArray();
            return snapshot.Storages.FindRange(key, boundary, SeekDirection.Backward)
                .Select(u => (BinaryPrimitives.ReadUInt32BigEndian(u.Key.Key.AsSpan(^sizeof(uint))), (BigInteger)u.Value));
        }

        [ContractMethod(0_03000000, CallFlags.ReadStates)]
        public BigInteger UnclaimedGas(StoreView snapshot, UInt160 account, uint end)
        {
            StorageItem storage = snapshot.Storages.TryGet(CreateStorageKey(Prefix_Account).Add(account));
            if (storage is null) return BigInteger.Zero;
            NeoAccountState state = storage.GetInteroperable<NeoAccountState>();
            return CalculateBonus(snapshot, state.VoteTo, state.Balance, state.BalanceHeight, end);
        }

        [ContractMethod(1000_00000000, CallFlags.WriteStates)]
        private bool RegisterCandidate(ApplicationEngine engine, ECPoint pubkey)
        {
            if (!engine.CheckWitnessInternal(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash()))
                return false;
            StorageKey key = CreateStorageKey(Prefix_Candidate).Add(pubkey);
            StorageItem item = engine.Snapshot.Storages.GetAndChange(key, () => new StorageItem(new CandidateState()));
            CandidateState state = item.GetInteroperable<CandidateState>();
            state.Registered = true;
            return true;
        }

        [ContractMethod(0_05000000, CallFlags.WriteStates)]
        private bool UnregisterCandidate(ApplicationEngine engine, ECPoint pubkey)
        {
            if (!engine.CheckWitnessInternal(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash()))
                return false;
            StorageKey key = CreateStorageKey(Prefix_Candidate).Add(pubkey);
            if (engine.Snapshot.Storages.TryGet(key) is null) return true;
            StorageItem item = engine.Snapshot.Storages.GetAndChange(key);
            CandidateState state = item.GetInteroperable<CandidateState>();
            state.Registered = false;
            CheckCandidate(engine.Snapshot, pubkey, state);
            return true;
        }

        [ContractMethod(0_05000000, CallFlags.WriteStates)]
        private bool Vote(ApplicationEngine engine, UInt160 account, ECPoint voteTo)
        {
            if (!engine.CheckWitnessInternal(account)) return false;
            NeoAccountState state_account = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_Account).Add(account))?.GetInteroperable<NeoAccountState>();
            if (state_account is null) return false;
            CandidateState validator_new = null;
            if (voteTo != null)
            {
                validator_new = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_Candidate).Add(voteTo))?.GetInteroperable<CandidateState>();
                if (validator_new is null) return false;
                if (!validator_new.Registered) return false;
            }
            if (state_account.VoteTo is null ^ voteTo is null)
            {
                StorageItem item = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_VotersCount));
                if (state_account.VoteTo is null)
                    item.Add(state_account.Balance);
                else
                    item.Add(-state_account.Balance);
            }
            DistributeGas(engine, account, state_account);
            if (state_account.VoteTo != null)
            {
                StorageKey key = CreateStorageKey(Prefix_Candidate).Add(state_account.VoteTo);
                StorageItem storage_validator = engine.Snapshot.Storages.GetAndChange(key);
                CandidateState state_validator = storage_validator.GetInteroperable<CandidateState>();
                state_validator.Votes -= state_account.Balance;
                CheckCandidate(engine.Snapshot, state_account.VoteTo, state_validator);
            }
            state_account.VoteTo = voteTo;
            if (validator_new != null)
            {
                validator_new.Votes += state_account.Balance;
            }
            return true;
        }

        [ContractMethod(1_00000000, CallFlags.ReadStates)]
        public (ECPoint PublicKey, BigInteger Votes)[] GetCandidates(StoreView snapshot)
        {
            byte[] prefix_key = CreateStorageKey(Prefix_Candidate).ToArray();
            return snapshot.Storages.Find(prefix_key).Select(p =>
            (
                p.Key.Key.AsSerializable<ECPoint>(1),
                p.Value.GetInteroperable<CandidateState>()
            )).Where(p => p.Item2.Registered).Select(p => (p.Item1, p.Item2.Votes)).ToArray();
        }

        [ContractMethod(1_00000000, CallFlags.ReadStates)]
        public ECPoint[] GetCommittee(StoreView snapshot)
        {
            return GetCommitteeFromCache(snapshot).Select(p => p.PublicKey).OrderBy(p => p).ToArray();
        }

        public UInt160 GetCommitteeAddress(StoreView snapshot)
        {
            ECPoint[] committees = GetCommittee(snapshot);
            return Contract.CreateMultiSigRedeemScript(committees.Length - (committees.Length - 1) / 2, committees).ToScriptHash();
        }

        private CachedCommittee GetCommitteeFromCache(StoreView snapshot)
        {
            if (ProtocolSettings.Default.EmergencyMode.Contains(snapshot.Height))
            {
                // Restore original committee because the block it's in emergency mode
                var candidates = GetCandidates(snapshot);
                return new CachedCommittee(Blockchain.StandbyCommittee.Select(p => (p, candidates.FirstOrDefault(k => k.PublicKey.Equals(p)).Votes)));
            }
            return snapshot.Storages[CreateStorageKey(Prefix_Committee)].GetInteroperable<CachedCommittee>();
        }

        public ECPoint[] ComputeNextBlockValidators(StoreView snapshot)
        {
            return ComputeCommitteeMembers(snapshot).Select(p => p.PublicKey).Take(ProtocolSettings.Default.ValidatorsCount).OrderBy(p => p).ToArray();
        }

        private IEnumerable<(ECPoint PublicKey, BigInteger Votes)> ComputeCommitteeMembers(StoreView snapshot)
        {
            decimal votersCount = (decimal)(BigInteger)snapshot.Storages[CreateStorageKey(Prefix_VotersCount)];
            decimal voterTurnout = votersCount / (decimal)TotalAmount;
            var candidates = GetCandidates(snapshot);
            if (voterTurnout < EffectiveVoterTurnout || candidates.Length < ProtocolSettings.Default.CommitteeMembersCount)
                return Blockchain.StandbyCommittee.Select(p => (p, candidates.FirstOrDefault(k => k.PublicKey.Equals(p)).Votes));
            return candidates.OrderByDescending(p => p.Votes).ThenBy(p => p.PublicKey).Take(ProtocolSettings.Default.CommitteeMembersCount);
        }

        [ContractMethod(1_00000000, CallFlags.ReadStates)]
        public ECPoint[] GetNextBlockValidators(StoreView snapshot)
        {
            return GetCommitteeFromCache(snapshot)
                .Take(ProtocolSettings.Default.ValidatorsCount)
                .Select(p => p.PublicKey)
                .OrderBy(p => p)
                .ToArray();
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

        public class CachedCommittee : List<(ECPoint PublicKey, BigInteger Votes)>, IInteroperable
        {
            public CachedCommittee()
            {
            }

            public CachedCommittee(IEnumerable<(ECPoint PublicKey, BigInteger Votes)> collection) : base(collection)
            {
            }

            public void FromStackItem(StackItem stackItem)
            {
                foreach (StackItem item in (VM.Types.Array)stackItem)
                {
                    Struct @struct = (Struct)item;
                    Add((@struct[0].GetSpan().AsSerializable<ECPoint>(), @struct[1].GetInteger()));
                }
            }

            public StackItem ToStackItem(ReferenceCounter referenceCounter)
            {
                return new VM.Types.Array(referenceCounter, this.Select(p => new Struct(referenceCounter, new StackItem[] { p.PublicKey.ToArray(), p.Votes })));
            }
        }
    }
}
