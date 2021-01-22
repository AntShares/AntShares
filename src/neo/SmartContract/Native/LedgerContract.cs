#pragma warning disable IDE0051

using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using System;
using System.Linq;
using System.Numerics;

namespace Neo.SmartContract.Native
{
    public sealed class LedgerContract : NativeContract
    {
        private const byte Prefix_BlockHash = 9;
        private const byte Prefix_CurrentBlock = 12;
        private const byte Prefix_Block = 5;
        private const byte Prefix_Transaction = 11;
        private const byte Prefix_CurrentHeader = 14;

        internal LedgerContract()
        {
        }

        internal override void OnPersist(ApplicationEngine engine)
        {
            engine.Snapshot.Add(CreateStorageKey(Prefix_BlockHash).AddBigEndian(engine.PersistingBlock.Index), new StorageItem(engine.PersistingBlock.Hash.ToArray(), true));
            engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_Block).Add(engine.PersistingBlock.Hash), () => new StorageItem(Trim(engine.PersistingBlock).ToArray(), true));
            foreach (Transaction tx in engine.PersistingBlock.Transactions)
            {
                engine.Snapshot.Add(CreateStorageKey(Prefix_Transaction).Add(tx.Hash), new StorageItem(new TransactionState
                {
                    BlockIndex = engine.PersistingBlock.Index,
                    Transaction = tx
                }, true));
            }
        }

        internal override void PostPersist(ApplicationEngine engine)
        {
            HashIndexState state = engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_CurrentBlock), () => new StorageItem(new HashIndexState())).GetInteroperable<HashIndexState>();
            state.Hash = engine.PersistingBlock.Hash;
            state.Index = engine.PersistingBlock.Index;
        }

        internal bool Initialized(DataCache snapshot)
        {
            return snapshot.Find(CreateStorageKey(Prefix_Block).ToArray()).Any();
        }

        private bool IsTraceableBlock(DataCache snapshot, uint index)
        {
            uint currentIndex = CurrentIndex(snapshot);
            if (index > currentIndex) return false;
            return index + ProtocolSettings.Default.MaxTraceableBlocks > currentIndex;
        }

        public UInt256 GetBlockHash(DataCache snapshot, uint index)
        {
            StorageItem item = snapshot.TryGet(CreateStorageKey(Prefix_BlockHash).AddBigEndian(index));
            if (item is null) return null;
            return new UInt256(item.Value);
        }

        [ContractMethod(0_01000000, CallFlags.ReadStates)]
        public UInt256 CurrentHash(DataCache snapshot)
        {
            return snapshot[CreateStorageKey(Prefix_CurrentBlock)].GetInteroperable<HashIndexState>().Hash;
        }

        [ContractMethod(0_01000000, CallFlags.ReadStates)]
        public uint CurrentIndex(DataCache snapshot)
        {
            return snapshot[CreateStorageKey(Prefix_CurrentBlock)].GetInteroperable<HashIndexState>().Index;
        }

        public bool ContainsBlock(DataCache snapshot, UInt256 hash)
        {
            return snapshot.Contains(CreateStorageKey(Prefix_Block).Add(hash));
        }

        public bool ContainsTransaction(DataCache snapshot, UInt256 hash)
        {
            return snapshot.Contains(CreateStorageKey(Prefix_Transaction).Add(hash));
        }

        public TrimmedBlock GetTrimmedBlock(DataCache snapshot, UInt256 hash)
        {
            StorageItem item = snapshot.TryGet(CreateStorageKey(Prefix_Block).Add(hash));
            if (item is null) return null;
            return item.Value.AsSerializable<TrimmedBlock>();
        }

        [ContractMethod(0_01000000, CallFlags.ReadStates)]
        private TrimmedBlock GetBlock(DataCache snapshot, byte[] indexOrHash)
        {
            UInt256 hash;
            if (indexOrHash.Length < UInt256.Length)
                hash = GetBlockHash(snapshot, (uint)new BigInteger(indexOrHash));
            else if (indexOrHash.Length == UInt256.Length)
                hash = new UInt256(indexOrHash);
            else
                throw new ArgumentException(null, nameof(indexOrHash));
            if (hash is null) return null;
            TrimmedBlock block = GetTrimmedBlock(snapshot, hash);
            if (block is null || !IsTraceableBlock(snapshot, block.Index)) return null;
            return block;
        }

        public Block GetBlock(DataCache snapshot, UInt256 hash)
        {
            TrimmedBlock state = GetTrimmedBlock(snapshot, hash);
            if (state is null) return null;
            return new Block
            {
                Version = state.Version,
                PrevHash = state.PrevHash,
                MerkleRoot = state.MerkleRoot,
                Timestamp = state.Timestamp,
                Index = state.Index,
                NextConsensus = state.NextConsensus,
                Witness = state.Witness,
                ConsensusData = state.ConsensusData,
                Transactions = state.Hashes.Skip(1).Select(p => GetTransaction(snapshot, p)).ToArray()
            };
        }

        public Block GetBlock(DataCache snapshot, uint index)
        {
            UInt256 hash = GetBlockHash(snapshot, index);
            if (hash is null) return null;
            return GetBlock(snapshot, hash);
        }

        public Header GetHeader(DataCache snapshot, UInt256 hash)
        {
            return GetTrimmedBlock(snapshot, hash)?.Header;
        }

        public Header GetHeader(DataCache snapshot, uint index)
        {
            UInt256 hash = GetBlockHash(snapshot, index);
            if (hash is null) return null;
            return GetHeader(snapshot, hash);
        }

        public TransactionState GetTransactionState(DataCache snapshot, UInt256 hash)
        {
            return snapshot.TryGet(CreateStorageKey(Prefix_Transaction).Add(hash))?.GetInteroperable<TransactionState>();
        }

        public Transaction GetTransaction(DataCache snapshot, UInt256 hash)
        {
            return GetTransactionState(snapshot, hash)?.Transaction;
        }

        public void SetCurrentHeader(DataCache snapshot, UInt256 hash, uint index)
        {
            HashIndexState state = snapshot.GetAndChange(CreateStorageKey(Prefix_CurrentHeader), () => new StorageItem(new HashIndexState())).GetInteroperable<HashIndexState>();
            state.Hash = hash;
            state.Index = index;
        }

        public UInt256 CurrentHeaderHash(DataCache snapshot)
        {
            return snapshot[CreateStorageKey(Prefix_CurrentHeader)].GetInteroperable<HashIndexState>().Hash;
        }

        public uint CurrentHeaderIndex(DataCache snapshot)
        {
            return snapshot[CreateStorageKey(Prefix_CurrentHeader)].GetInteroperable<HashIndexState>().Index;
        }

        public void SaveHeader(DataCache snapshot, Header header)
        {
            snapshot.Add(CreateStorageKey(Prefix_Block).Add(header.Hash), new StorageItem(header.Trim().ToArray(), true));
        }

        [ContractMethod(0_01000000, CallFlags.ReadStates, Name = "getTransaction")]
        private Transaction GetTransactionForContract(DataCache snapshot, UInt256 hash)
        {
            TransactionState state = GetTransactionState(snapshot, hash);
            if (state is null || !IsTraceableBlock(snapshot, state.BlockIndex)) return null;
            return state.Transaction;
        }

        [ContractMethod(0_01000000, CallFlags.ReadStates)]
        private int GetTransactionHeight(DataCache snapshot, UInt256 hash)
        {
            TransactionState state = GetTransactionState(snapshot, hash);
            if (state is null || !IsTraceableBlock(snapshot, state.BlockIndex)) return -1;
            return (int)state.BlockIndex;
        }

        [ContractMethod(0_02000000, CallFlags.ReadStates)]
        private Transaction GetTransactionFromBlock(DataCache snapshot, byte[] blockIndexOrHash, int txIndex)
        {
            UInt256 hash;
            if (blockIndexOrHash.Length < UInt256.Length)
                hash = GetBlockHash(snapshot, (uint)new BigInteger(blockIndexOrHash));
            else if (blockIndexOrHash.Length == UInt256.Length)
                hash = new UInt256(blockIndexOrHash);
            else
                throw new ArgumentException(null, nameof(blockIndexOrHash));
            if (hash is null) return null;
            TrimmedBlock block = GetTrimmedBlock(snapshot, hash);
            if (block is null || !IsTraceableBlock(snapshot, block.Index)) return null;
            if (txIndex < 0 || txIndex >= block.Hashes.Length - 1)
                throw new ArgumentOutOfRangeException(nameof(txIndex));
            return GetTransaction(snapshot, block.Hashes[txIndex + 1]);
        }

        private static TrimmedBlock Trim(Block block)
        {
            return new TrimmedBlock
            {
                Version = block.Version,
                PrevHash = block.PrevHash,
                MerkleRoot = block.MerkleRoot,
                Timestamp = block.Timestamp,
                Index = block.Index,
                NextConsensus = block.NextConsensus,
                Witness = block.Witness,
                Hashes = block.Transactions.Select(p => p.Hash).Prepend(block.ConsensusData.Hash).ToArray(),
                ConsensusData = block.ConsensusData
            };
        }
    }
}
