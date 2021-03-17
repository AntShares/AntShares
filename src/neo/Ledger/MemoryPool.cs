using Akka.Util.Internal;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Neo.Ledger
{
    public class MemoryPool : IReadOnlyCollection<Transaction>
    {
        // Allow a reverified transaction to be rebroadcasted if it has been this many block times since last broadcast.
        private const int BlocksTillRebroadcast = 10;
        private int RebroadcastMultiplierThreshold => Capacity / 10;

        private readonly double MaxMillisecondsToReverifyTx;

        // These two are not expected to be hit, they are just safegaurds.
        private readonly double MaxMillisecondsToReverifyTxPerIdle;

        private readonly NeoSystem _system;

        //
        /// <summary>
        /// Guarantees consistency of the pool data structures.
        ///
        /// Note: The data structures are only modified from the `Blockchain` actor; so operations guaranteed to be
        ///       performed by the blockchain actor do not need to acquire the read lock; they only need the write
        ///       lock for write operations.
        /// </summary>
        private readonly ReaderWriterLockSlim _txRwLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        /// <summary>
        /// Store all verified unsorted transactions currently in the pool.
        /// </summary>
        private readonly Dictionary<UInt256, PoolItem> _unsortedTransactions = new Dictionary<UInt256, PoolItem>();
        /// <summary>
        /// Stores the verified sorted transactins currently in the pool.
        /// </summary>
        private readonly SortedSet<PoolItem> _sortedTransactions = new SortedSet<PoolItem>();

        /// <summary>
        /// Store the unverified transactions currently in the pool.
        ///
        /// Transactions in this data structure were valid in some prior block, but may no longer be valid.
        /// The top ones that could make it into the next block get verified and moved into the verified data structures
        /// (_unsortedTransactions, and _sortedTransactions) after each block.
        /// </summary>
        private readonly Dictionary<UInt256, PoolItem> _unverifiedTransactions = new Dictionary<UInt256, PoolItem>();
        private readonly SortedSet<PoolItem> _unverifiedSortedTransactions = new SortedSet<PoolItem>();

        // Internal methods to aid in unit testing
        internal int SortedTxCount => _sortedTransactions.Count;
        internal int UnverifiedSortedTxCount => _unverifiedSortedTransactions.Count;

        /// <summary>
        /// Total maximum capacity of transactions the pool can hold.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Store all verified unsorted transactions' senders' fee currently in the memory pool.
        /// </summary>
        private TransactionVerificationContext VerificationContext = new TransactionVerificationContext();

        /// <summary>
        /// Total count of transactions in the pool.
        /// </summary>
        public int Count
        {
            get
            {
                _txRwLock.EnterReadLock();
                try
                {
                    return _unsortedTransactions.Count + _unverifiedTransactions.Count;
                }
                finally
                {
                    _txRwLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Total count of verified transactions in the pool.
        /// </summary>
        public int VerifiedCount => _unsortedTransactions.Count; // read of 32 bit type is atomic (no lock)

        public int UnVerifiedCount => _unverifiedTransactions.Count;

        public MemoryPool(NeoSystem system)
        {
            _system = system;
            Capacity = system.Settings.MemoryPoolMaxTransactions;
            MaxMillisecondsToReverifyTx = (double)system.Settings.MillisecondsPerBlock / 3;
            MaxMillisecondsToReverifyTxPerIdle = (double)system.Settings.MillisecondsPerBlock / 15;
        }

        /// <summary>
        /// Determine whether the pool is holding this transaction and has at some point verified it.
        /// Note: The pool may not have verified it since the last block was persisted. To get only the
        ///       transactions that have been verified during this block use GetVerifiedTransactions()
        /// </summary>
        /// <param name="hash">the transaction hash</param>
        /// <returns>true if the MemoryPool contain the transaction</returns>
        public bool ContainsKey(UInt256 hash)
        {
            _txRwLock.EnterReadLock();
            try
            {
                return _unsortedTransactions.ContainsKey(hash) || _unverifiedTransactions.ContainsKey(hash);
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        public bool TryGetValue(UInt256 hash, out Transaction tx)
        {
            _txRwLock.EnterReadLock();
            try
            {
                bool ret = _unsortedTransactions.TryGetValue(hash, out PoolItem item)
                           || _unverifiedTransactions.TryGetValue(hash, out item);
                tx = ret ? item.Tx : null;
                return ret;
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        // Note: This isn't used in Fill during consensus, fill uses GetSortedVerifiedTransactions()
        public IEnumerator<Transaction> GetEnumerator()
        {
            _txRwLock.EnterReadLock();
            try
            {
                return _unsortedTransactions.Select(p => p.Value.Tx)
                    .Concat(_unverifiedTransactions.Select(p => p.Value.Tx))
                    .ToList()
                    .GetEnumerator();
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerable<Transaction> GetVerifiedTransactions()
        {
            _txRwLock.EnterReadLock();
            try
            {
                return _unsortedTransactions.Select(p => p.Value.Tx).ToArray();
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        public void GetVerifiedAndUnverifiedTransactions(out IEnumerable<Transaction> verifiedTransactions,
            out IEnumerable<Transaction> unverifiedTransactions)
        {
            _txRwLock.EnterReadLock();
            try
            {
                verifiedTransactions = _sortedTransactions.Reverse().Select(p => p.Tx).ToArray();
                unverifiedTransactions = _unverifiedSortedTransactions.Reverse().Select(p => p.Tx).ToArray();
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        public IEnumerable<Transaction> GetSortedVerifiedTransactions()
        {
            _txRwLock.EnterReadLock();
            try
            {
                return _sortedTransactions.Reverse().Select(p => p.Tx).ToArray();
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PoolItem GetLowestFeeTransaction(SortedSet<PoolItem> verifiedTxSorted,
            SortedSet<PoolItem> unverifiedTxSorted, out SortedSet<PoolItem> sortedPool)
        {
            PoolItem minItem = unverifiedTxSorted.Min;
            sortedPool = minItem != null ? unverifiedTxSorted : null;

            PoolItem verifiedMin = verifiedTxSorted.Min;
            if (verifiedMin == null) return minItem;

            if (minItem != null && verifiedMin.CompareTo(minItem) >= 0)
                return minItem;

            sortedPool = verifiedTxSorted;
            minItem = verifiedMin;

            return minItem;
        }

        private PoolItem GetLowestFeeTransaction(out Dictionary<UInt256, PoolItem> unsortedTxPool, out SortedSet<PoolItem> sortedPool)
        {
            sortedPool = null;

            try
            {
                return GetLowestFeeTransaction(_sortedTransactions, _unverifiedSortedTransactions, out sortedPool);
            }
            finally
            {
                unsortedTxPool = Object.ReferenceEquals(sortedPool, _unverifiedSortedTransactions)
                   ? _unverifiedTransactions : _unsortedTransactions;
            }
        }

        // Note: this must only be called from a single thread (the Blockchain actor)
        internal bool CanTransactionFitInPool(Transaction tx)
        {
            if (Count < Capacity) return true;

            return GetLowestFeeTransaction(out _, out _).CompareTo(tx) <= 0;
        }

        /// <summary>
        /// Adds an already verified transaction to the memory pool.
        ///
        /// Note: This must only be called from a single thread (the Blockchain actor). To add a transaction to the pool
        ///       tell the Blockchain actor about the transaction.
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        internal VerifyResult TryAdd(Transaction tx, DataCache snapshot)
        {
            var poolItem = new PoolItem(tx);

            if (_unsortedTransactions.ContainsKey(tx.Hash)) return VerifyResult.AlreadyExists;

            List<Transaction> removedTransactions = null;
            _txRwLock.EnterWriteLock();
            try
            {
                VerifyResult result = tx.VerifyStateDependent(_system.Settings, snapshot, VerificationContext);
                if (result != VerifyResult.Succeed) return result;

                _unsortedTransactions.Add(tx.Hash, poolItem);
                VerificationContext.AddTransaction(tx);
                _sortedTransactions.Add(poolItem);

                if (Count > Capacity)
                    removedTransactions = RemoveOverCapacity();
            }
            finally
            {
                _txRwLock.ExitWriteLock();
            }

            foreach (IMemoryPoolTxObserverPlugin plugin in Plugin.TxObserverPlugins)
            {
                plugin.TransactionAdded(_system, poolItem.Tx);
                if (removedTransactions != null)
                    plugin.TransactionsRemoved(_system, MemoryPoolTxRemovalReason.CapacityExceeded, removedTransactions);
            }

            if (!_unsortedTransactions.ContainsKey(tx.Hash)) return VerifyResult.OutOfMemory;
            return VerifyResult.Succeed;
        }

        private List<Transaction> RemoveOverCapacity()
        {
            List<Transaction> removedTransactions = new List<Transaction>();
            do
            {
                PoolItem minItem = GetLowestFeeTransaction(out var unsortedPool, out var sortedPool);

                unsortedPool.Remove(minItem.Tx.Hash);
                sortedPool.Remove(minItem);
                removedTransactions.Add(minItem.Tx);

                if (ReferenceEquals(sortedPool, _sortedTransactions))
                    VerificationContext.RemoveTransaction(minItem.Tx);
            } while (Count > Capacity);

            return removedTransactions;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryRemoveVerified(UInt256 hash, out PoolItem item)
        {
            if (!_unsortedTransactions.TryGetValue(hash, out item))
                return false;

            _unsortedTransactions.Remove(hash);
            _sortedTransactions.Remove(item);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryRemoveUnVerified(UInt256 hash, out PoolItem item)
        {
            if (!_unverifiedTransactions.TryGetValue(hash, out item))
                return false;

            _unverifiedTransactions.Remove(hash);
            _unverifiedSortedTransactions.Remove(item);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InvalidateVerifiedTransactions()
        {
            foreach (PoolItem item in _sortedTransactions)
            {
                if (_unverifiedTransactions.TryAdd(item.Tx.Hash, item))
                    _unverifiedSortedTransactions.Add(item);
            }

            // Clear the verified transactions now, since they all must be reverified.
            _unsortedTransactions.Clear();
            VerificationContext = new TransactionVerificationContext();
            _sortedTransactions.Clear();
        }

        // Note: this must only be called from a single thread (the Blockchain actor)
        internal void UpdatePoolForBlockPersisted(Block block, DataCache snapshot, bool invalidate = true)
        {
            _txRwLock.EnterWriteLock();
            try
            {
                // First remove the transactions verified in the block.
                foreach (Transaction tx in block.Transactions)
                {
                    if (TryRemoveVerified(tx.Hash, out _)) continue;
                    TryRemoveUnVerified(tx.Hash, out _);
                }

                // Add all the previously verified transactions back to the unverified transactions
                if (invalidate) InvalidateVerifiedTransactions();
            }
            finally
            {
                _txRwLock.ExitWriteLock();
            }

            // If we know about headers of future blocks, no point in verifying transactions from the unverified tx pool
            // until we get caught up.
            if (block.Index > 0 && _system.HeaderCache.Count > 0)
                return;

            ReverifyTransactions(_sortedTransactions, _unverifiedSortedTransactions, (int)_system.Settings.MaxTransactionsPerBlock, MaxMillisecondsToReverifyTx, snapshot);
        }

        internal void InvalidateAllTransactions()
        {
            _txRwLock.EnterWriteLock();
            try
            {
                InvalidateVerifiedTransactions();
            }
            finally
            {
                _txRwLock.ExitWriteLock();
            }
        }

        private int ReverifyTransactions(SortedSet<PoolItem> verifiedSortedTxPool,
            SortedSet<PoolItem> unverifiedSortedTxPool, int count, double millisecondsTimeout, DataCache snapshot)
        {
            DateTime reverifyCutOffTimeStamp = TimeProvider.Current.UtcNow.AddMilliseconds(millisecondsTimeout);
            List<PoolItem> reverifiedItems = new List<PoolItem>(count);
            List<PoolItem> invalidItems = new List<PoolItem>();

            _txRwLock.EnterWriteLock();
            try
            {
                // Since unverifiedSortedTxPool is ordered in an ascending manner, we take from the end.
                foreach (PoolItem item in unverifiedSortedTxPool.Reverse().Take(count))
                {
                    if (item.Tx.VerifyStateDependent(_system.Settings, snapshot, VerificationContext) == VerifyResult.Succeed)
                    {
                        reverifiedItems.Add(item);
                        VerificationContext.AddTransaction(item.Tx);
                    }
                    else // Transaction no longer valid -- it will be removed from unverifiedTxPool.
                        invalidItems.Add(item);

                    if (TimeProvider.Current.UtcNow > reverifyCutOffTimeStamp) break;
                }

                int blocksTillRebroadcast = BlocksTillRebroadcast;
                // Increases, proportionally, blocksTillRebroadcast if mempool has more items than threshold bigger RebroadcastMultiplierThreshold
                if (Count > RebroadcastMultiplierThreshold)
                    blocksTillRebroadcast = blocksTillRebroadcast * Count / RebroadcastMultiplierThreshold;

                var rebroadcastCutOffTime = TimeProvider.Current.UtcNow.AddMilliseconds(-_system.Settings.MillisecondsPerBlock * blocksTillRebroadcast);
                foreach (PoolItem item in reverifiedItems)
                {
                    if (_unsortedTransactions.TryAdd(item.Tx.Hash, item))
                    {
                        verifiedSortedTxPool.Add(item);

                        if (item.LastBroadcastTimestamp < rebroadcastCutOffTime)
                        {
                            _system.LocalNode.Tell(new LocalNode.RelayDirectly { Inventory = item.Tx }, _system.Blockchain);
                            item.LastBroadcastTimestamp = TimeProvider.Current.UtcNow;
                        }
                    }
                    else
                        VerificationContext.RemoveTransaction(item.Tx);

                    _unverifiedTransactions.Remove(item.Tx.Hash);
                    unverifiedSortedTxPool.Remove(item);
                }

                foreach (PoolItem item in invalidItems)
                {
                    _unverifiedTransactions.Remove(item.Tx.Hash);
                    unverifiedSortedTxPool.Remove(item);
                }
            }
            finally
            {
                _txRwLock.ExitWriteLock();
            }

            var invalidTransactions = invalidItems.Select(p => p.Tx).ToArray();
            foreach (IMemoryPoolTxObserverPlugin plugin in Plugin.TxObserverPlugins)
                plugin.TransactionsRemoved(_system, MemoryPoolTxRemovalReason.NoLongerValid, invalidTransactions);

            return reverifiedItems.Count;
        }

        /// <summary>
        /// Reverify up to a given maximum count of transactions. Verifies less at a time once the max that can be
        /// persisted per block has been reached.
        ///
        /// Note: this must only be called from a single thread (the Blockchain actor)
        /// </summary>
        /// <param name="maxToVerify">Max transactions to reverify, the value passed can be >=1</param>
        /// <param name="snapshot">The snapshot to use for verifying.</param>
        /// <returns>true if more unsorted messages exist, otherwise false</returns>
        internal bool ReVerifyTopUnverifiedTransactionsIfNeeded(int maxToVerify, DataCache snapshot)
        {
            if (_system.HeaderCache.Count > 0)
                return false;

            if (_unverifiedSortedTransactions.Count > 0)
            {
                int verifyCount = _sortedTransactions.Count > _system.Settings.MaxTransactionsPerBlock ? 1 : maxToVerify;
                ReverifyTransactions(_sortedTransactions, _unverifiedSortedTransactions,
                    verifyCount, MaxMillisecondsToReverifyTxPerIdle, snapshot);
            }

            return _unverifiedTransactions.Count > 0;
        }
    }
}
