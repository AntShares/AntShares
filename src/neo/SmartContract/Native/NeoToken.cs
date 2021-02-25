#pragma warning disable IDE0051

using Neo.Cryptography.ECC;
using Neo.IO;
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
        private const byte Prefix_RegisterPrice = 13;
        private const byte Prefix_VoterRewardPerCommittee = 23;

        private const byte NeoHolderRewardRatio = 10;
        private const byte CommitteeRewardRatio = 10;
        private const byte VoterRewardRatio = 80;

        internal NeoToken()
        {
            this.TotalAmount = 100000000 * Factor;
        }

        public override BigInteger TotalSupply(DataCache snapshot)
        {
            return TotalAmount;
        }

        protected override void OnBalanceChanging(ApplicationEngine engine, UInt160 account, NeoAccountState state, BigInteger amount)
        {
            DistributeGas(engine, account, state);
            if (amount.IsZero) return;
            if (state.VoteTo is null) return;
            engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_VotersCount)).Add(amount);
            StorageKey key = CreateStorageKey(Prefix_Candidate).Add(state.VoteTo);
            CandidateState candidate = engine.Snapshot.GetAndChange(key).GetInteroperable<CandidateState>();
            candidate.Votes += amount;
            CheckCandidate(engine.Snapshot, state.VoteTo, candidate);
        }

        internal void DistributeGas(ApplicationEngine engine, UInt160 account)
        {
            StorageItem storage = engine.Snapshot.TryGet(CreateStorageKey(Prefix_Account).Add(account));
            if (storage is null) return;

            // PersistingBlock is null when running under the debugger
            if (engine.PersistingBlock is null) return;

            DistributeGas(engine, account, storage.GetInteroperable<NeoAccountState>());
        }

        private void DistributeGas(ApplicationEngine engine, UInt160 account, NeoAccountState state)
        {
            BigInteger gas = CalculateBonus(engine.Snapshot, state.VoteTo, state.Balance, state.BalanceHeight, engine.PersistingBlock.Index);
            state.BalanceHeight = engine.PersistingBlock.Index;
            GAS.Mint(engine, account, gas, true);
        }

        private BigInteger CalculateBonus(DataCache snapshot, ECPoint vote, BigInteger value, uint start, uint end)
        {
            if (value.IsZero || start >= end) return BigInteger.Zero;
            if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));

            BigInteger neoHolderReward = CalculateNeoHolderReward(snapshot, value, start, end);
            if (vote is null) return neoHolderReward;

            byte[] border = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(vote).ToArray();
            byte[] keyStart = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(vote).AddBigEndian(start).ToArray();
            (_, var item) = snapshot.FindRange(keyStart, border, SeekDirection.Backward).FirstOrDefault();
            BigInteger startRewardPerNeo = item ?? BigInteger.Zero;

            byte[] keyEnd = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(vote).AddBigEndian(end).ToArray();
            (_, item) = snapshot.FindRange(keyEnd, border, SeekDirection.Backward).FirstOrDefault();
            BigInteger endRewardPerNeo = item ?? BigInteger.Zero;

            return neoHolderReward + value * (endRewardPerNeo - startRewardPerNeo) / 100000000L;
        }

        private BigInteger CalculateNeoHolderReward(DataCache snapshot, BigInteger value, uint start, uint end)
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

        private void CheckCandidate(DataCache snapshot, ECPoint pubkey, CandidateState candidate)
        {
            if (!candidate.Registered && candidate.Votes.IsZero)
            {
                foreach (var (rewardKey, _) in snapshot.Find(CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(pubkey).ToArray()).ToArray())
                    snapshot.Delete(rewardKey);
                snapshot.Delete(CreateStorageKey(Prefix_Candidate).Add(pubkey));
            }
        }

        public static bool ShouldRefreshCommittee(uint height, int committeeMembersCount) => height % committeeMembersCount == 0;

        internal override void Initialize(ApplicationEngine engine)
        {
            var cachedCommittee = new CachedCommittee(engine.ProtocolSettings.StandbyCommittee.Select(p => (p, BigInteger.Zero)));
            engine.Snapshot.Add(CreateStorageKey(Prefix_Committee), new StorageItem(cachedCommittee));
            engine.Snapshot.Add(CreateStorageKey(Prefix_VotersCount), new StorageItem(new byte[0]));
            engine.Snapshot.Add(CreateStorageKey(Prefix_GasPerBlock).AddBigEndian(0u), new StorageItem(5 * GAS.Factor));
            engine.Snapshot.Add(CreateStorageKey(Prefix_RegisterPrice), new StorageItem(1000 * GAS.Factor));
            Mint(engine, Contract.GetBFTAddress(engine.ProtocolSettings.StandbyValidators), TotalAmount, false);
        }

        internal override void OnPersist(ApplicationEngine engine)
        {
            // Set next committee
            if (ShouldRefreshCommittee(engine.PersistingBlock.Index, engine.ProtocolSettings.CommitteeMembersCount))
            {
                StorageItem storageItem = engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_Committee));
                var cachedCommittee = storageItem.GetInteroperable<CachedCommittee>();
                cachedCommittee.Clear();
                cachedCommittee.AddRange(ComputeCommitteeMembers(engine.Snapshot, engine.ProtocolSettings));
            }
        }

        internal override void PostPersist(ApplicationEngine engine)
        {
            // Distribute GAS for committee

            int m = engine.ProtocolSettings.CommitteeMembersCount;
            int n = engine.ProtocolSettings.ValidatorsCount;
            int index = (int)(engine.PersistingBlock.Index % (uint)m);
            var gasPerBlock = GetGasPerBlock(engine.Snapshot);
            var committee = GetCommitteeFromCache(engine.Snapshot);
            var pubkey = committee.ElementAt(index).PublicKey;
            var account = Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();
            GAS.Mint(engine, account, gasPerBlock * CommitteeRewardRatio / 100, false);

            // Record the cumulative reward of the voters of committee

            if (ShouldRefreshCommittee(engine.PersistingBlock.Index, m))
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
                        (_, var item) = engine.Snapshot.FindRange(voterRewardKey.ToArray(), border, SeekDirection.Backward).FirstOrDefault();
                        voterSumRewardPerNEO += (item ?? BigInteger.Zero);
                        engine.Snapshot.Add(voterRewardKey, new StorageItem(voterSumRewardPerNEO));
                    }
                }
            }
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.States)]
        private void SetGasPerBlock(ApplicationEngine engine, BigInteger gasPerBlock)
        {
            if (gasPerBlock < 0 || gasPerBlock > 10 * GAS.Factor)
                throw new ArgumentOutOfRangeException(nameof(gasPerBlock));
            if (!CheckCommittee(engine)) throw new InvalidOperationException();

            uint index = engine.PersistingBlock.Index + 1;
            StorageItem entry = engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_GasPerBlock).AddBigEndian(index), () => new StorageItem(gasPerBlock));
            entry.Set(gasPerBlock);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public BigInteger GetGasPerBlock(DataCache snapshot)
        {
            return GetSortedGasRecords(snapshot, Ledger.CurrentIndex(snapshot) + 1).First().GasPerBlock;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.States)]
        private void SetRegisterPrice(ApplicationEngine engine, long registerPrice)
        {
            if (registerPrice <= 0)
                throw new ArgumentOutOfRangeException(nameof(registerPrice));
            if (!CheckCommittee(engine)) throw new InvalidOperationException();
            engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_RegisterPrice)).Set(registerPrice);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public long GetRegisterPrice(DataCache snapshot)
        {
            return (long)(BigInteger)snapshot[CreateStorageKey(Prefix_RegisterPrice)];
        }

        private IEnumerable<(uint Index, BigInteger GasPerBlock)> GetSortedGasRecords(DataCache snapshot, uint end)
        {
            byte[] key = CreateStorageKey(Prefix_GasPerBlock).AddBigEndian(end).ToArray();
            byte[] boundary = CreateStorageKey(Prefix_GasPerBlock).ToArray();
            return snapshot.FindRange(key, boundary, SeekDirection.Backward)
                .Select(u => (BinaryPrimitives.ReadUInt32BigEndian(u.Key.Key.AsSpan(^sizeof(uint))), (BigInteger)u.Value));
        }

        [ContractMethod(CpuFee = 1 << 17, RequiredCallFlags = CallFlags.ReadStates)]
        public BigInteger UnclaimedGas(DataCache snapshot, UInt160 account, uint end)
        {
            StorageItem storage = snapshot.TryGet(CreateStorageKey(Prefix_Account).Add(account));
            if (storage is null) return BigInteger.Zero;
            NeoAccountState state = storage.GetInteroperable<NeoAccountState>();
            return CalculateBonus(snapshot, state.VoteTo, state.Balance, state.BalanceHeight, end);
        }

        [ContractMethod(RequiredCallFlags = CallFlags.States)]
        private bool RegisterCandidate(ApplicationEngine engine, ECPoint pubkey)
        {
            if (!engine.CheckWitnessInternal(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash()))
                return false;
            engine.AddGas(GetRegisterPrice(engine.Snapshot));
            StorageKey key = CreateStorageKey(Prefix_Candidate).Add(pubkey);
            StorageItem item = engine.Snapshot.GetAndChange(key, () => new StorageItem(new CandidateState()));
            CandidateState state = item.GetInteroperable<CandidateState>();
            state.Registered = true;
            return true;
        }

        [ContractMethod(CpuFee = 1 << 16, RequiredCallFlags = CallFlags.States)]
        private bool UnregisterCandidate(ApplicationEngine engine, ECPoint pubkey)
        {
            if (!engine.CheckWitnessInternal(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash()))
                return false;
            StorageKey key = CreateStorageKey(Prefix_Candidate).Add(pubkey);
            if (engine.Snapshot.TryGet(key) is null) return true;
            StorageItem item = engine.Snapshot.GetAndChange(key);
            CandidateState state = item.GetInteroperable<CandidateState>();
            state.Registered = false;
            CheckCandidate(engine.Snapshot, pubkey, state);
            return true;
        }

        [ContractMethod(CpuFee = 1 << 16, RequiredCallFlags = CallFlags.States)]
        private bool Vote(ApplicationEngine engine, UInt160 account, ECPoint voteTo)
        {
            if (!engine.CheckWitnessInternal(account)) return false;
            NeoAccountState state_account = engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_Account).Add(account))?.GetInteroperable<NeoAccountState>();
            if (state_account is null) return false;
            CandidateState validator_new = null;
            if (voteTo != null)
            {
                validator_new = engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_Candidate).Add(voteTo))?.GetInteroperable<CandidateState>();
                if (validator_new is null) return false;
                if (!validator_new.Registered) return false;
            }
            if (state_account.VoteTo is null ^ voteTo is null)
            {
                StorageItem item = engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_VotersCount));
                if (state_account.VoteTo is null)
                    item.Add(state_account.Balance);
                else
                    item.Add(-state_account.Balance);
            }
            DistributeGas(engine, account, state_account);
            if (state_account.VoteTo != null)
            {
                StorageKey key = CreateStorageKey(Prefix_Candidate).Add(state_account.VoteTo);
                StorageItem storage_validator = engine.Snapshot.GetAndChange(key);
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

        [ContractMethod(CpuFee = 1 << 22, RequiredCallFlags = CallFlags.ReadStates)]
        public (ECPoint PublicKey, BigInteger Votes)[] GetCandidates(DataCache snapshot)
        {
            byte[] prefix_key = CreateStorageKey(Prefix_Candidate).ToArray();
            return snapshot.Find(prefix_key).Select(p =>
            (
                p.Key.Key.AsSerializable<ECPoint>(1),
                p.Value.GetInteroperable<CandidateState>()
            )).Where(p => p.Item2.Registered).Select(p => (p.Item1, p.Item2.Votes)).ToArray();
        }

        [ContractMethod(CpuFee = 1 << 22, RequiredCallFlags = CallFlags.ReadStates)]
        public ECPoint[] GetCommittee(DataCache snapshot)
        {
            return GetCommitteeFromCache(snapshot).Select(p => p.PublicKey).OrderBy(p => p).ToArray();
        }

        public UInt160 GetCommitteeAddress(DataCache snapshot)
        {
            ECPoint[] committees = GetCommittee(snapshot);
            return Contract.CreateMultiSigRedeemScript(committees.Length - (committees.Length - 1) / 2, committees).ToScriptHash();
        }

        private CachedCommittee GetCommitteeFromCache(DataCache snapshot)
        {
            return snapshot[CreateStorageKey(Prefix_Committee)].GetInteroperable<CachedCommittee>();
        }

        public ECPoint[] ComputeNextBlockValidators(DataCache snapshot, ProtocolSettings settings)
        {
            return ComputeCommitteeMembers(snapshot, settings).Select(p => p.PublicKey).Take(settings.ValidatorsCount).OrderBy(p => p).ToArray();
        }

        private IEnumerable<(ECPoint PublicKey, BigInteger Votes)> ComputeCommitteeMembers(DataCache snapshot, ProtocolSettings settings)
        {
            decimal votersCount = (decimal)(BigInteger)snapshot[CreateStorageKey(Prefix_VotersCount)];
            decimal voterTurnout = votersCount / (decimal)TotalAmount;
            var candidates = GetCandidates(snapshot);
            if (voterTurnout < EffectiveVoterTurnout || candidates.Length < settings.CommitteeMembersCount)
                return settings.StandbyCommittee.Select(p => (p, candidates.FirstOrDefault(k => k.PublicKey.Equals(p)).Votes));
            return candidates.OrderByDescending(p => p.Votes).ThenBy(p => p.PublicKey).Take(settings.CommitteeMembersCount);
        }

        [ContractMethod(CpuFee = 1 << 22, RequiredCallFlags = CallFlags.ReadStates)]
        private ECPoint[] GetNextBlockValidators(ApplicationEngine engine)
        {
            return GetNextBlockValidators(engine.Snapshot, engine.ProtocolSettings.ValidatorsCount);
        }

        public ECPoint[] GetNextBlockValidators(DataCache snapshot, int validatorsCount)
        {
            return GetCommitteeFromCache(snapshot)
                .Take(validatorsCount)
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
