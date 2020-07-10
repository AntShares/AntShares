#pragma warning disable IDE0051

using Neo.Cryptography.ECC;
using Neo.Ledger;
using Neo.Persistence;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Numerics;
using Array = Neo.VM.Types.Array;

namespace Neo.SmartContract.Native.Tokens
{
    public partial class NeoToken
    {
        private const byte Prefix_GasPerBlock = 17;
        private const byte Prefix_RewardRatio = 73;
        private const byte Prefix_VoterRewardPerCommittee = 23;
        private const byte Prefix_HolderRewardPerBlock = 57;

        [ContractMethod(0_05000000, CallFlags.AllowModifyStates)]
        private bool SetGasPerBlock(ApplicationEngine engine, BigInteger gasPerBlock)
        {
            if (gasPerBlock < 0 || gasPerBlock > 8 * GAS.Factor) return false;
            if (!CheckCommitteeWitness(engine)) return false;
            StorageItem item = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_GasPerBlock));
            item.Value = gasPerBlock.ToByteArray();
            return true;
        }

        [ContractMethod(0_05000000, CallFlags.AllowModifyStates)]
        private bool SetRewardRatio(ApplicationEngine engine, byte neoHoldersRewardRatio, byte committeesRewardRatio, byte votersRewardRatio)
        {
            if (checked(neoHoldersRewardRatio + committeesRewardRatio + votersRewardRatio) != 100) return false;
            if (!CheckCommitteeWitness(engine)) return false;
            RewardRatio rewardRatio = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_RewardRatio), () => new StorageItem(new RewardRatio())).GetInteroperable<RewardRatio>();
            rewardRatio.NeoHolder = neoHoldersRewardRatio;
            rewardRatio.Committee = committeesRewardRatio;
            rewardRatio.Voter = votersRewardRatio;
            return true;
        }

        [ContractMethod(1_00000000, CallFlags.AllowStates)]
        public BigInteger GetGasPerBlock(StoreView snapshot)
        {
            return new BigInteger(snapshot.Storages.TryGet(CreateStorageKey(Prefix_GasPerBlock)).Value);
        }

        [ContractMethod(1_00000000, CallFlags.AllowStates)]
        internal RewardRatio GetRewardRatio(StoreView snapshot)
        {
            return snapshot.Storages.TryGet(CreateStorageKey(Prefix_RewardRatio)).GetInteroperable<RewardRatio>();
        }

        private void DistributeGas(ApplicationEngine engine, UInt160 account, NeoAccountState state)
        {
            BigInteger gas = CalculateBonus(engine.Snapshot, state.VoteTo, state.Balance, state.BalanceHeight, engine.Snapshot.PersistingBlock.Index);
            state.BalanceHeight = engine.Snapshot.PersistingBlock.Index;
            GAS.Mint(engine, account, gas);
        }

        private BigInteger CalculateBonus(StoreView snapshot, ECPoint vote, BigInteger value, uint start, uint end)
        {
            if (value.IsZero || start >= end) return BigInteger.Zero;
            if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));

            BigInteger neoHolderReward = CalculateNeoHolderReward(snapshot, value, start, end);
            if (vote is null) return neoHolderReward;

            var voteScriptHash = Contract.CreateSignatureContract(vote).ScriptHash;
            var endKey = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(voteScriptHash).Add(uint.MaxValue - start - 1);
            var startKey = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(voteScriptHash).Add(uint.MaxValue - end - 1);
            var enumerator = snapshot.Storages.FindRange(startKey, endKey).GetEnumerator();
            if (!enumerator.MoveNext()) return neoHolderReward;

            var endRewardPerNeo = new BigInteger(enumerator.Current.Value.Value);
            var startRewardPerNeo = BigInteger.Zero;
            var borderKey = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(voteScriptHash).Add(uint.MaxValue);
            enumerator = snapshot.Storages.FindRange(endKey, borderKey).GetEnumerator();
            if (enumerator.MoveNext())
                startRewardPerNeo = new BigInteger(enumerator.Current.Value.Value);

            return neoHolderReward + value * (endRewardPerNeo - startRewardPerNeo) / 10000L;
        }

        private BigInteger CalculateNeoHolderReward(StoreView snapshot, BigInteger value, uint start, uint end)
        {
            var endRewardItem = snapshot.Storages.TryGet(CreateStorageKey(Prefix_HolderRewardPerBlock).Add(uint.MaxValue - end - 1));
            var startRewardItem = snapshot.Storages.TryGet(CreateStorageKey(Prefix_HolderRewardPerBlock).Add(uint.MaxValue - start - 1));
            BigInteger startReward = startRewardItem is null ? 0 : new BigInteger(startRewardItem.Value);
            return value * (new BigInteger(endRewardItem.Value) - startReward) / TotalAmount;
        }

        [ContractMethod(0_03000000, CallFlags.AllowStates)]
        public BigInteger UnclaimedGas(StoreView snapshot, UInt160 account, uint end)
        {
            StorageItem storage = snapshot.Storages.TryGet(CreateStorageKey(Prefix_Account).Add(account));
            if (storage is null) return BigInteger.Zero;
            NeoAccountState state = storage.GetInteroperable<NeoAccountState>();
            return CalculateBonus(snapshot, state.VoteTo, state.Balance, state.BalanceHeight, end);
        }

        private void DistributeGasForCommittee(ApplicationEngine engine)
        {
            var gasPerBlock = GetGasPerBlock(engine.Snapshot);
            (ECPoint, BigInteger)[] committeeVotes = GetCommitteeVotes(engine.Snapshot);
            int validatorNumber = GetValidators(engine.Snapshot).Length;
            RewardRatio rewardRatio = GetRewardRatio(engine.Snapshot);
            BigInteger holderRewardPerBlock = gasPerBlock * rewardRatio.NeoHolder / 100; // The final calculation should be divided by the total number of NEO
            BigInteger committeeRewardPerBlock = gasPerBlock * rewardRatio.Committee / 100 / committeeVotes.Length;
            BigInteger voterRewardPerBlock = gasPerBlock * rewardRatio.Voter / 100 / (committeeVotes.Length + validatorNumber);

            // Keep track of incremental gains of neo holders

            var index = engine.Snapshot.PersistingBlock.Index;
            var holderRewards = holderRewardPerBlock;
            var holderRewardKey = CreateStorageKey(Prefix_HolderRewardPerBlock).Add(uint.MaxValue - index - 1);
            var holderBorderKey = CreateStorageKey(Prefix_HolderRewardPerBlock).Add(uint.MaxValue);
            var enumerator = engine.Snapshot.Storages.FindRange(holderRewardKey, holderBorderKey).GetEnumerator();
            if (enumerator.MoveNext())
                holderRewards += new BigInteger(enumerator.Current.Value.Value);
            engine.Snapshot.Storages.Add(holderRewardKey, new StorageItem() { Value = holderRewards.ToByteArray() });

            for (var i = 0; i < committeeVotes.Length; i++)
            {
                // Keep track of incremental gains for each committee's voters

                UInt160 committeeAddr = Contract.CreateSignatureContract(committeeVotes[i].Item1).ScriptHash;
                BigInteger voterRewardPerCommittee = (i < validatorNumber ? 2 : 1) * voterRewardPerBlock * 10000L / committeeVotes[i].Item2; // Zoom in 10000 times, and the final calculation should be divided 10000L
                enumerator = engine.Snapshot.Storages.Find(CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(committeeAddr).ToArray()).GetEnumerator();
                if (enumerator.MoveNext())
                    voterRewardPerCommittee += new BigInteger(enumerator.Current.Value.Value);
                var storageKey = CreateStorageKey(Prefix_VoterRewardPerCommittee).Add(committeeAddr).Add(uint.MaxValue - index - 1);
                engine.Snapshot.Storages.Add(storageKey, new StorageItem() { Value = voterRewardPerCommittee.ToByteArray() });

                // Mint the reward for committee by each block

                GAS.Mint(engine, committeeAddr, committeeRewardPerBlock);
            }
        }
    }

    internal class RewardRatio : IInteroperable
    {
        public int NeoHolder;
        public int Committee;
        public int Voter;

        public void FromStackItem(StackItem stackItem)
        {
            Array array = (Array)stackItem;
            NeoHolder = (int)array[0].GetInteger();
            Committee = (int)array[1].GetInteger();
            Voter = (int)array[2].GetInteger();
        }

        public StackItem ToStackItem(ReferenceCounter referenceCounter)
        {
            return new Array() { new Integer(NeoHolder), new Integer(Committee), new Integer(Voter) };
        }
    }
}
