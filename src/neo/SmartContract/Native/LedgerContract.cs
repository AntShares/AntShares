#pragma warning disable IDE0051

using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using System;
using System.Linq;
using System.Numerics;

namespace Neo.SmartContract.Native
{
    /// <summary>
    /// A native contract for storing all blocks and transactions.
    /// </summary>
    public sealed class LedgerContract : NativeContract
    {
        private const byte Prefix_BlockHash = 9;
        private const byte Prefix_CurrentBlock = 12;
        private const byte Prefix_Block = 5;
        private const byte Prefix_Transaction = 11;
        private const byte Prefix_TrimmedTransaction = 3;

        internal LedgerContract()
        {
        }

        internal override ContractTask OnPersist(ApplicationEngine engine)
        {
            engine.Snapshot.Add(CreateStorageKey(Prefix_BlockHash).AddBigEndian(engine.PersistingBlock.Index), new StorageItem(engine.PersistingBlock.Hash.ToArray()));
            engine.Snapshot.Add(CreateStorageKey(Prefix_Block).Add(engine.PersistingBlock.Hash), new StorageItem(Trim(engine.PersistingBlock).ToArray()));
            foreach (Transaction tx in engine.PersistingBlock.Transactions)
            {
                engine.Snapshot.Add(CreateStorageKey(Prefix_Transaction).Add(tx.Hash), new StorageItem(new TransactionState
                {
                    BlockIndex = engine.PersistingBlock.Index,
                    Transaction = tx
                }));
                foreach (var attr in tx.GetAttributes<ConflictAttribute>())
                {
                    var hash = ((ConflictAttribute)attr).Hash;
                    engine.Snapshot.Add(CreateStorageKey(Prefix_TrimmedTransaction).Add(hash), new StorageItem(engine.PersistingBlock.Index));
                }
            }
            return ContractTask.CompletedTask;
        }

        internal override ContractTask PostPersist(ApplicationEngine engine)
        {
            HashIndexState state = engine.Snapshot.GetAndChange(CreateStorageKey(Prefix_CurrentBlock), () => new StorageItem(new HashIndexState())).GetInteroperable<HashIndexState>();
            state.Hash = engine.PersistingBlock.Hash;
            state.Index = engine.PersistingBlock.Index;
            return ContractTask.CompletedTask;
        }

        internal bool Initialized(DataCache snapshot)
        {
            return snapshot.Find(CreateStorageKey(Prefix_Block).ToArray()).Any();
        }

        private bool IsTraceableBlock(DataCache snapshot, uint index, uint maxTraceableBlocks)
        {
            uint currentIndex = CurrentIndex(snapshot);
            if (index > currentIndex) return false;
            return index + maxTraceableBlocks > currentIndex;
        }

        /// <summary>
        /// Gets the hash of the specified block.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="index">The index of the block.</param>
        /// <returns>The hash of the block.</returns>
        public UInt256 GetBlockHash(DataCache snapshot, uint index)
        {
            StorageItem item = snapshot.TryGet(CreateStorageKey(Prefix_BlockHash).AddBigEndian(index));
            if (item is null) return null;
            return new UInt256(item.Value);
        }

        /// <summary>
        /// Gets the hash of the current block.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <returns>The hash of the current block.</returns>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public UInt256 CurrentHash(DataCache snapshot)
        {
            return snapshot[CreateStorageKey(Prefix_CurrentBlock)].GetInteroperable<HashIndexState>().Hash;
        }

        /// <summary>
        /// Gets the index of the current block.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <returns>The index of the current block.</returns>
        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        public uint CurrentIndex(DataCache snapshot)
        {
            return snapshot[CreateStorageKey(Prefix_CurrentBlock)].GetInteroperable<HashIndexState>().Index;
        }

        /// <summary>
        /// Determine whether the specified block is contained in the blockchain.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="hash">The hash of the block.</param>
        /// <returns><see langword="true"/> if the blockchain contains the block; otherwise, <see langword="false"/>.</returns>
        public bool ContainsBlock(DataCache snapshot, UInt256 hash)
        {
            return snapshot.Contains(CreateStorageKey(Prefix_Block).Add(hash));
        }

        /// <summary>
        /// Determine whether the specified transaction is contained in the blockchain.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="hash">The hash of the transaction.</param>
        /// <returns><see langword="true"/> if the blockchain contains the transaction; otherwise, <see langword="false"/>.</returns>
        public bool ContainsTransaction(DataCache snapshot, UInt256 hash)
        {
            return snapshot.Contains(CreateStorageKey(Prefix_Transaction).Add(hash));
        }

        /// <summary>
        /// Gets a <see cref="TrimmedBlock"/> with the specified hash.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="hash">The hash of the block.</param>
        /// <returns>The trimmed block.</returns>
        public TrimmedBlock GetTrimmedBlock(DataCache snapshot, UInt256 hash)
        {
            StorageItem item = snapshot.TryGet(CreateStorageKey(Prefix_Block).Add(hash));
            if (item is null) return null;
            return item.Value.AsSerializable<TrimmedBlock>();
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        private TrimmedBlock GetBlock(ApplicationEngine engine, byte[] indexOrHash)
        {
            UInt256 hash;
            if (indexOrHash.Length < UInt256.Length)
                hash = GetBlockHash(engine.Snapshot, (uint)new BigInteger(indexOrHash));
            else if (indexOrHash.Length == UInt256.Length)
                hash = new UInt256(indexOrHash);
            else
                throw new ArgumentException(null, nameof(indexOrHash));
            if (hash is null) return null;
            TrimmedBlock block = GetTrimmedBlock(engine.Snapshot, hash);
            if (block is null || !IsTraceableBlock(engine.Snapshot, block.Index, engine.ProtocolSettings.MaxTraceableBlocks)) return null;
            return block;
        }

        /// <summary>
        /// Gets a block with the specified hash.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="hash">The hash of the block.</param>
        /// <returns>The block with the specified hash.</returns>
        public Block GetBlock(DataCache snapshot, UInt256 hash)
        {
            TrimmedBlock state = GetTrimmedBlock(snapshot, hash);
            if (state is null) return null;
            return new Block
            {
                Header = state.Header,
                Transactions = state.Hashes.Select(p => GetTransaction(snapshot, p)).ToArray()
            };
        }

        /// <summary>
        /// Gets a block with the specified index.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="index">The index of the block.</param>
        /// <returns>The block with the specified index.</returns>
        public Block GetBlock(DataCache snapshot, uint index)
        {
            UInt256 hash = GetBlockHash(snapshot, index);
            if (hash is null) return null;
            return GetBlock(snapshot, hash);
        }

        /// <summary>
        /// Gets a block header with the specified hash.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="hash">The hash of the block.</param>
        /// <returns>The block header with the specified hash.</returns>
        public Header GetHeader(DataCache snapshot, UInt256 hash)
        {
            return GetTrimmedBlock(snapshot, hash)?.Header;
        }

        /// <summary>
        /// Gets a block header with the specified index.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="index">The index of the block.</param>
        /// <returns>The block header with the specified index.</returns>
        public Header GetHeader(DataCache snapshot, uint index)
        {
            UInt256 hash = GetBlockHash(snapshot, index);
            if (hash is null) return null;
            return GetHeader(snapshot, hash);
        }

        /// <summary>
        /// Gets a <see cref="TransactionState"/> with the specified hash.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="hash">The hash of the transaction.</param>
        /// <returns>The <see cref="TransactionState"/> with the specified hash.</returns>
        public TransactionState GetTransactionState(DataCache snapshot, UInt256 hash)
        {
            return snapshot.TryGet(CreateStorageKey(Prefix_Transaction).Add(hash))?.GetInteroperable<TransactionState>();
        }

        /// <summary>
        /// Gets a transaction with the specified hash.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="hash">The hash of the transaction.</param>
        /// <returns>The transaction with the specified hash.</returns>
        public Transaction GetTransaction(DataCache snapshot, UInt256 hash)
        {
            return GetTransactionState(snapshot, hash)?.Transaction;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates, Name = "getTransaction")]
        private Transaction GetTransactionForContract(ApplicationEngine engine, UInt256 hash)
        {
            TransactionState state = GetTransactionState(engine.Snapshot, hash);
            if (state is null || !IsTraceableBlock(engine.Snapshot, state.BlockIndex, engine.ProtocolSettings.MaxTraceableBlocks)) return null;
            return state.Transaction;
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.ReadStates)]
        private int GetTransactionHeight(ApplicationEngine engine, UInt256 hash)
        {
            TransactionState state = GetTransactionState(engine.Snapshot, hash);
            if (state is null || !IsTraceableBlock(engine.Snapshot, state.BlockIndex, engine.ProtocolSettings.MaxTraceableBlocks)) return -1;
            return (int)state.BlockIndex;
        }

        [ContractMethod(CpuFee = 1 << 16, RequiredCallFlags = CallFlags.ReadStates)]
        private Transaction GetTransactionFromBlock(ApplicationEngine engine, byte[] blockIndexOrHash, int txIndex)
        {
            UInt256 hash;
            if (blockIndexOrHash.Length < UInt256.Length)
                hash = GetBlockHash(engine.Snapshot, (uint)new BigInteger(blockIndexOrHash));
            else if (blockIndexOrHash.Length == UInt256.Length)
                hash = new UInt256(blockIndexOrHash);
            else
                throw new ArgumentException(null, nameof(blockIndexOrHash));
            if (hash is null) return null;
            TrimmedBlock block = GetTrimmedBlock(engine.Snapshot, hash);
            if (block is null || !IsTraceableBlock(engine.Snapshot, block.Index, engine.ProtocolSettings.MaxTraceableBlocks)) return null;
            if (txIndex < 0 || txIndex >= block.Hashes.Length)
                throw new ArgumentOutOfRangeException(nameof(txIndex));
            return GetTransaction(engine.Snapshot, block.Hashes[txIndex]);
        }

        private static TrimmedBlock Trim(Block block)
        {
            return new TrimmedBlock
            {
                Header = block.Header,
                Hashes = block.Transactions.Select(p => p.Hash).ToArray()
            };
        }
    }
}
