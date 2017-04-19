﻿using AntShares.Core;
using AntShares.Cryptography.ECC;
using AntShares.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace AntShares.Implementations.Blockchains.LevelDB
{
    public class LevelDBBlockchain : Blockchain
    {
        private DB db;
        private Thread thread_persistence;
        private List<UInt256> header_index = new List<UInt256>();
        private Dictionary<UInt256, Header> header_cache = new Dictionary<UInt256, Header>();
        private Dictionary<UInt256, Block> block_cache = new Dictionary<UInt256, Block>();
        private uint current_block_height = 0;
        private uint stored_header_count = 0;
        private AutoResetEvent new_block_event = new AutoResetEvent(false);
        private bool disposed = false;

        public override BlockchainAbility Ability => BlockchainAbility.All;
        public override UInt256 CurrentBlockHash => header_index[(int)current_block_height];
        public override UInt256 CurrentHeaderHash => header_index[header_index.Count - 1];
        public override uint HeaderHeight => (uint)header_index.Count - 1;
        public override uint Height => current_block_height;
        public override bool IsReadOnly => false;
        public bool VerifyBlocks { get; set; } = true;

        public LevelDBBlockchain(string path)
        {
            header_index.Add(GenesisBlock.Hash);
            Version version;
            Slice value;
            db = DB.Open(path, new Options { CreateIfMissing = true });
            if (db.TryGet(ReadOptions.Default, SliceBuilder.Begin(DataEntryPrefix.SYS_Version), out value) && Version.TryParse(value.ToString(), out version) && version >= Version.Parse("0.6.6043.32131"))
            {
                ReadOptions options = new ReadOptions { FillCache = false };
                value = db.Get(options, SliceBuilder.Begin(DataEntryPrefix.SYS_CurrentBlock));
                UInt256 current_header_hash = new UInt256(value.ToArray().Take(32).ToArray());
                this.current_block_height = value.ToArray().ToUInt32(32);
                uint current_header_height = current_block_height;
                if (db.TryGet(options, SliceBuilder.Begin(DataEntryPrefix.SYS_CurrentHeader), out value))
                {
                    current_header_hash = new UInt256(value.ToArray().Take(32).ToArray());
                    current_header_height = value.ToArray().ToUInt32(32);
                }
                foreach (UInt256 hash in db.Find(options, SliceBuilder.Begin(DataEntryPrefix.IX_HeaderHashList), (k, v) =>
                {
                    using (MemoryStream ms = new MemoryStream(v.ToArray(), false))
                    using (BinaryReader r = new BinaryReader(ms))
                    {
                        return new
                        {
                            Index = k.ToArray().ToUInt32(1),
                            Hashes = r.ReadSerializableArray<UInt256>()
                        };
                    }
                }).OrderBy(p => p.Index).SelectMany(p => p.Hashes).ToArray())
                {
                    if (!hash.Equals(GenesisBlock.Hash))
                    {
                        header_index.Add(hash);
                    }
                    stored_header_count++;
                }
                if (stored_header_count == 0)
                {
                    Header[] headers = db.Find(options, SliceBuilder.Begin(DataEntryPrefix.DATA_Block), (k, v) => Header.FromTrimmedData(v.ToArray(), sizeof(long))).OrderBy(p => p.Height).ToArray();
                    for (int i = 1; i < headers.Length; i++)
                    {
                        header_index.Add(headers[i].Hash);
                    }
                }
                else if (current_header_height >= stored_header_count)
                {
                    for (UInt256 hash = current_header_hash; hash != header_index[(int)stored_header_count - 1];)
                    {
                        Header header = Header.FromTrimmedData(db.Get(options, SliceBuilder.Begin(DataEntryPrefix.DATA_Block).Add(hash)).ToArray(), sizeof(long));
                        header_index.Insert((int)stored_header_count, hash);
                        hash = header.PrevBlock;
                    }
                }
            }
            else
            {
                WriteBatch batch = new WriteBatch();
                ReadOptions options = new ReadOptions { FillCache = false };
                using (Iterator it = db.NewIterator(options))
                {
                    for (it.SeekToFirst(); it.Valid(); it.Next())
                    {
                        batch.Delete(it.Key());
                    }
                }
                db.Write(WriteOptions.Default, batch);
                Persist(GenesisBlock);
                db.Put(WriteOptions.Default, SliceBuilder.Begin(DataEntryPrefix.SYS_Version), GetType().GetTypeInfo().Assembly.GetName().Version.ToString());
            }
            thread_persistence = new Thread(PersistBlocks);
            thread_persistence.Name = "LevelDBBlockchain.PersistBlocks";
            thread_persistence.Start();
        }

        public override bool AddBlock(Block block)
        {
            lock (block_cache)
            {
                if (!block_cache.ContainsKey(block.Hash))
                {
                    block_cache.Add(block.Hash, block);
                }
            }
            lock (header_index)
            {
                if (block.Height - 1 >= header_index.Count) return false;
                if (block.Height == header_index.Count)
                {
                    if (VerifyBlocks && !block.Verify()) return false;
                    WriteBatch batch = new WriteBatch();
                    OnAddHeader(block.Header, batch);
                    db.Write(WriteOptions.Default, batch);
                }
                if (block.Height < header_index.Count)
                    new_block_event.Set();
            }
            return true;
        }

        protected internal override void AddHeaders(IEnumerable<Header> headers)
        {
            lock (header_index)
            {
                lock (header_cache)
                {
                    WriteBatch batch = new WriteBatch();
                    foreach (Header header in headers)
                    {
                        if (header.Height - 1 >= header_index.Count) break;
                        if (header.Height < header_index.Count) continue;
                        if (VerifyBlocks && !header.Verify()) break;
                        OnAddHeader(header, batch);
                        header_cache.Add(header.Hash, header);
                    }
                    db.Write(WriteOptions.Default, batch);
                    header_cache.Clear();
                }
            }
        }

        public override bool ContainsBlock(UInt256 hash)
        {
            if (base.ContainsBlock(hash)) return true;
            return GetHeader(hash)?.Height <= current_block_height;
        }

        public override bool ContainsTransaction(UInt256 hash)
        {
            if (base.ContainsTransaction(hash)) return true;
            Slice value;
            return db.TryGet(ReadOptions.Default, SliceBuilder.Begin(DataEntryPrefix.DATA_Transaction).Add(hash), out value);
        }

        public override bool ContainsUnspent(UInt256 hash, ushort index)
        {
            Slice value;
            if (!db.TryGet(ReadOptions.Default, SliceBuilder.Begin(DataEntryPrefix.ST_Coin).Add(hash), out value))
                return false;
            UnspentCoinState state = value.ToArray().AsSerializable<UnspentCoinState>();
            if (index >= state.Items.Length) return false;
            return !state.Items[index].HasFlag(CoinState.Spent);
        }

        public override void Dispose()
        {
            disposed = true;
            new_block_event.Set();
            if (!thread_persistence.ThreadState.HasFlag(ThreadState.Unstarted))
                thread_persistence.Join();
            new_block_event.Dispose();
            if (db != null)
            {
                db.Dispose();
                db = null;
            }
        }

        public override AssetState GetAssetState(UInt256 asset_id)
        {
            Slice slice;
            if (!db.TryGet(ReadOptions.Default, SliceBuilder.Begin(DataEntryPrefix.ST_Asset).Add(asset_id), out slice))
                return null;
            return slice.ToArray().AsSerializable<AssetState>();
        }

        public override Block GetBlock(UInt256 hash)
        {
            Block block = base.GetBlock(hash);
            if (block == null)
            {
                block = GetBlockInternal(ReadOptions.Default, hash);
            }
            return block;
        }

        public override UInt256 GetBlockHash(uint height)
        {
            UInt256 hash = base.GetBlockHash(height);
            if (hash != null) return hash;
            if (current_block_height < height) return null;
            lock (header_index)
            {
                if (header_index.Count <= height) return null;
                return header_index[(int)height];
            }
        }

        private Block GetBlockInternal(ReadOptions options, UInt256 hash)
        {
            Slice value;
            if (!db.TryGet(options, SliceBuilder.Begin(DataEntryPrefix.DATA_Block).Add(hash), out value))
                return null;
            int height;
            Block block = Block.FromTrimmedData(value.ToArray(), sizeof(long), p => GetTransaction(options, p, out height));
            if (block.Transactions.Length == 0) return null;
            return block;
        }

        public override ContractState GetContract(UInt160 hash)
        {
            Slice value;
            if (!db.TryGet(ReadOptions.Default, SliceBuilder.Begin(DataEntryPrefix.ST_Contract).Add(hash), out value))
                return null;
            return value.ToArray().AsSerializable<ContractState>();
        }

        public override IEnumerable<ValidatorState> GetEnrollments(IEnumerable<Transaction> others)
        {
            Dictionary<ECPoint, ValidatorState> dictionary = new Dictionary<ECPoint, ValidatorState>();
            ReadOptions options = new ReadOptions();
            using (options.Snapshot = db.GetSnapshot())
            {
                foreach (ValidatorState validator in db.Find(options, SliceBuilder.Begin(DataEntryPrefix.ST_Validator), (k, v) => v.ToArray().AsSerializable<ValidatorState>()))
                {
                    dictionary.Add(validator.PublicKey, validator);
                }
            }
            foreach (EnrollmentTransaction tx in others.OfType<EnrollmentTransaction>())
            {
                if (!dictionary.ContainsKey(tx.PublicKey))
                    dictionary.Add(tx.PublicKey, new ValidatorState
                    {
                        PublicKey = tx.PublicKey
                    });
            }
            return dictionary.Values;
        }

        public override Header GetHeader(uint height)
        {
            Header header = base.GetHeader(height);
            if (header != null) return header;
            UInt256 hash;
            lock (header_index)
            {
                if (header_index.Count <= height) return null;
                hash = header_index[(int)height];
            }
            return GetHeader(hash);
        }

        public override Header GetHeader(UInt256 hash)
        {
            Header header = base.GetHeader(hash);
            if (header != null) return header;
            lock (header_cache)
            {
                if (header_cache.ContainsKey(hash))
                    return header_cache[hash];
            }
            Slice value;
            if (!db.TryGet(ReadOptions.Default, SliceBuilder.Begin(DataEntryPrefix.DATA_Block).Add(hash), out value))
                return null;
            return Header.FromTrimmedData(value.ToArray(), sizeof(long));
        }

        public override Block GetNextBlock(UInt256 hash)
        {
            return GetBlockInternal(ReadOptions.Default, GetNextBlockHash(hash));
        }

        public override UInt256 GetNextBlockHash(UInt256 hash)
        {
            Header header = GetHeader(hash);
            if (header == null) return null;
            lock (header_index)
            {
                if (header.Height + 1 >= header_index.Count)
                    return null;
                return header_index[(int)header.Height + 1];
            }
        }

        public override long GetSysFeeAmount(UInt256 hash)
        {
            Slice value;
            if (!db.TryGet(ReadOptions.Default, SliceBuilder.Begin(DataEntryPrefix.DATA_Block).Add(hash), out value))
                return 0;
            return value.ToArray().ToInt64(0);
        }

        public override Transaction GetTransaction(UInt256 hash, out int height)
        {
            Transaction tx = base.GetTransaction(hash, out height);
            if (tx == null)
            {
                tx = GetTransaction(ReadOptions.Default, hash, out height);
            }
            return tx;
        }

        private Transaction GetTransaction(ReadOptions options, UInt256 hash, out int height)
        {
            Slice value;
            if (db.TryGet(options, SliceBuilder.Begin(DataEntryPrefix.DATA_Transaction).Add(hash), out value))
            {
                byte[] data = value.ToArray();
                height = data.ToInt32(0);
                return Transaction.DeserializeFrom(data, sizeof(uint));
            }
            else
            {
                height = -1;
                return null;
            }
        }

        public override Dictionary<ushort, SpentCoin> GetUnclaimed(UInt256 hash)
        {
            int height;
            Transaction tx = GetTransaction(ReadOptions.Default, hash, out height);
            if (tx == null) return null;
            Slice value;
            if (db.TryGet(ReadOptions.Default, SliceBuilder.Begin(DataEntryPrefix.ST_SpentCoin).Add(hash), out value))
            {
                SpentCoinState state = value.ToArray().AsSerializable<SpentCoinState>();
                return state.Items.ToDictionary(p => p.Key, p => new SpentCoin
                {
                    Output = tx.Outputs[p.Key],
                    StartHeight = (uint)height,
                    EndHeight = p.Value
                });
            }
            else
            {
                return new Dictionary<ushort, SpentCoin>();
            }
        }

        public override TransactionOutput GetUnspent(UInt256 hash, ushort index)
        {
            ReadOptions options = new ReadOptions();
            using (options.Snapshot = db.GetSnapshot())
            {
                Slice value;
                if (!db.TryGet(options, SliceBuilder.Begin(DataEntryPrefix.ST_Coin).Add(hash), out value))
                    return null;
                UnspentCoinState state = value.ToArray().AsSerializable<UnspentCoinState>();
                if (index >= state.Items.Length) return null;
                if (state.Items[index].HasFlag(CoinState.Spent)) return null;
                int height;
                return GetTransaction(options, hash, out height).Outputs[index];
            }
        }

        public override IEnumerable<VoteState> GetVotes(IEnumerable<Transaction> others)
        {
            ReadOptions options = new ReadOptions();
            using (options.Snapshot = db.GetSnapshot())
            {
                var inputs = others.SelectMany(p => p.Inputs).GroupBy(p => p.PrevHash, (k, g) =>
                {
                    int height;
                    Transaction tx = GetTransaction(options, k, out height);
                    return g.Select(p => tx.Outputs[p.PrevIndex]);
                }).SelectMany(p => p).Where(p => p.AssetId.Equals(AntShare.Hash)).Select(p => new
                {
                    p.ScriptHash,
                    Value = -p.Value
                });
                var outputs = others.SelectMany(p => p.Outputs).Where(p => p.AssetId.Equals(AntShare.Hash)).Select(p => new
                {
                    p.ScriptHash,
                    p.Value
                });
                var changes = inputs.Concat(outputs).GroupBy(p => p.ScriptHash).ToDictionary(p => p.Key, p => p.Sum(i => i.Value));
                var accounts = db.Find(options, SliceBuilder.Begin(DataEntryPrefix.ST_Account), (k, v) => v.ToArray().AsSerializable<AccountState>()).Where(p => p.Votes.Length > 0);
                foreach (AccountState account in accounts)
                {
                    Fixed8 balance = account.Balances.ContainsKey(AntShare.Hash) ? account.Balances[AntShare.Hash] : Fixed8.Zero;
                    if (changes.ContainsKey(account.ScriptHash))
                        balance += changes[account.ScriptHash];
                    if (balance <= Fixed8.Zero) continue;
                    yield return new VoteState
                    {
                        PublicKeys = account.Votes,
                        Count = balance
                    };
                }
            }
        }

        public override bool IsDoubleSpend(Transaction tx)
        {
            if (tx.Inputs.Length == 0) return false;
            ReadOptions options = new ReadOptions();
            using (options.Snapshot = db.GetSnapshot())
            {
                foreach (var group in tx.Inputs.GroupBy(p => p.PrevHash))
                {
                    Slice value;
                    if (!db.TryGet(options, SliceBuilder.Begin(DataEntryPrefix.ST_Coin).Add(group.Key), out value))
                        return true;
                    UnspentCoinState state = value.ToArray().AsSerializable<UnspentCoinState>();
                    if (group.Any(p => p.PrevIndex >= state.Items.Length || state.Items[p.PrevIndex].HasFlag(CoinState.Spent)))
                        return true;
                }
            }
            return false;
        }

        private void OnAddHeader(Header header, WriteBatch batch)
        {
            header_index.Add(header.Hash);
            while ((int)header.Height - 2000 >= stored_header_count)
            {
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter w = new BinaryWriter(ms))
                {
                    w.Write(header_index.Skip((int)stored_header_count).Take(2000).ToArray());
                    w.Flush();
                    batch.Put(SliceBuilder.Begin(DataEntryPrefix.IX_HeaderHashList).Add(stored_header_count), ms.ToArray());
                }
                stored_header_count += 2000;
            }
            batch.Put(SliceBuilder.Begin(DataEntryPrefix.DATA_Block).Add(header.Hash), SliceBuilder.Begin().Add(0L).Add(header.ToArray()));
            batch.Put(SliceBuilder.Begin(DataEntryPrefix.SYS_CurrentHeader), SliceBuilder.Begin().Add(header.Hash).Add(header.Height));
        }

        private void Persist(Block block)
        {
            DataCache<UInt160, AccountState> accounts = new DataCache<UInt160, AccountState>(db, DataEntryPrefix.ST_Account);
            DataCache<UInt256, UnspentCoinState> unspentcoins = new DataCache<UInt256, UnspentCoinState>(db, DataEntryPrefix.ST_Coin);
            DataCache<UInt256, SpentCoinState> spentcoins = new DataCache<UInt256, SpentCoinState>(db, DataEntryPrefix.ST_SpentCoin);
            DataCache<ECPoint, ValidatorState> validators = new DataCache<ECPoint, ValidatorState>(db, DataEntryPrefix.ST_Validator);
            DataCache<UInt256, AssetState> assets = new DataCache<UInt256, AssetState>(db, DataEntryPrefix.ST_Asset);
            DataCache<UInt160, ContractState> contracts = new DataCache<UInt160, ContractState>(db, DataEntryPrefix.ST_Contract);
            WriteBatch batch = new WriteBatch();
            long amount_sysfee = GetSysFeeAmount(block.PrevBlock) + (long)block.Transactions.Sum(p => p.SystemFee);
            batch.Put(SliceBuilder.Begin(DataEntryPrefix.DATA_Block).Add(block.Hash), SliceBuilder.Begin().Add(amount_sysfee).Add(block.Trim()));
            foreach (Transaction tx in block.Transactions)
            {
                batch.Put(SliceBuilder.Begin(DataEntryPrefix.DATA_Transaction).Add(tx.Hash), SliceBuilder.Begin().Add(block.Height).Add(tx.ToArray()));
                switch (tx.Type)
                {
                    case TransactionType.RegisterTransaction:
                        {
                            RegisterTransaction rtx = (RegisterTransaction)tx;
                            assets.Add(tx.Hash, new AssetState
                            {
                                AssetId = rtx.Hash,
                                AssetType = rtx.AssetType,
                                Name = rtx.Name,
                                Amount = rtx.Amount,
                                Available = Fixed8.Zero,
                                Precision = rtx.Precision,
                                Fee = Fixed8.Zero,
                                FeeAddress = new UInt160(),
                                Owner = rtx.Owner,
                                Admin = rtx.Admin,
                                Issuer = rtx.Admin,
                                Expiration = block.Height + 2000000,
                                IsFrozen = false
                            });
                        }
                        break;
                    case TransactionType.IssueTransaction:
                        foreach (TransactionResult result in tx.GetTransactionResults().Where(p => p.Amount < Fixed8.Zero))
                            assets[result.AssetId].Available -= result.Amount;
                        break;
                    case TransactionType.ClaimTransaction:
                        foreach (CoinReference input in ((ClaimTransaction)tx).Claims)
                        {
                            spentcoins[input.PrevHash].Items.Remove(input.PrevIndex);
                        }
                        break;
                    case TransactionType.EnrollmentTransaction:
                        {
                            EnrollmentTransaction enroll_tx = (EnrollmentTransaction)tx;
                            validators.Add(enroll_tx.PublicKey, new ValidatorState
                            {
                                PublicKey = enroll_tx.PublicKey
                            });
                        }
                        break;
                    case TransactionType.PublishTransaction:
                        {
                            PublishTransaction publish_tx = (PublishTransaction)tx;
                            contracts.Add(publish_tx.Code.ScriptHash, new ContractState
                            {
                                Script = publish_tx.Code.Script
                            });
                        }
                        break;
                }
                unspentcoins.Add(tx.Hash, new UnspentCoinState
                {
                    Items = Enumerable.Repeat(CoinState.Confirmed, tx.Outputs.Length).ToArray()
                });
                foreach (TransactionOutput output in tx.Outputs)
                {
                    AccountState account = accounts.GetOrAdd(output.ScriptHash, () => new AccountState
                    {
                        ScriptHash = output.ScriptHash,
                        IsFrozen = false,
                        Votes = new ECPoint[0],
                        Balances = new Dictionary<UInt256, Fixed8>()
                    });
                    if (account.Balances.ContainsKey(output.AssetId))
                        account.Balances[output.AssetId] += output.Value;
                    else
                        account.Balances[output.AssetId] = output.Value;
                }
            }
            foreach (var group in block.Transactions.SelectMany(p => p.Inputs).GroupBy(p => p.PrevHash))
            {
                int height;
                Transaction tx = GetTransaction(ReadOptions.Default, group.Key, out height);
                foreach (CoinReference input in group)
                {
                    unspentcoins[input.PrevHash].Items[input.PrevIndex] |= CoinState.Spent;
                    if (tx.Outputs[input.PrevIndex].AssetId.Equals(AntShare.Hash))
                    {
                        spentcoins.GetOrAdd(input.PrevHash, () => new SpentCoinState
                        {
                            TransactionHash = input.PrevHash,
                            TransactionHeight = (uint)height,
                            Items = new Dictionary<ushort, uint>()
                        }).Items.Add(input.PrevIndex, block.Height);
                    }
                    accounts[tx.Outputs[input.PrevIndex].ScriptHash].Balances[tx.Outputs[input.PrevIndex].AssetId] -= tx.Outputs[input.PrevIndex].Value;
                }
            }
            accounts.DeleteWhere((k, v) => !v.IsFrozen && v.Votes.Length == 0 && v.Balances.All(p => p.Value <= Fixed8.Zero));
            accounts.Commit(batch);
            unspentcoins.DeleteWhere((k, v) => v.Items.All(p => p.HasFlag(CoinState.Spent)));
            unspentcoins.Commit(batch);
            spentcoins.DeleteWhere((k, v) => v.Items.Count == 0);
            spentcoins.Commit(batch);
            validators.Commit(batch);
            assets.Commit(batch);
            contracts.Commit(batch);
            batch.Put(SliceBuilder.Begin(DataEntryPrefix.SYS_CurrentBlock), SliceBuilder.Begin().Add(block.Hash).Add(block.Height));
            db.Write(WriteOptions.Default, batch);
            current_block_height = block.Height;
        }

        private void PersistBlocks()
        {
            while (!disposed)
            {
                new_block_event.WaitOne();
                while (!disposed)
                {
                    UInt256 hash;
                    lock (header_index)
                    {
                        if (header_index.Count <= current_block_height + 1) break;
                        hash = header_index[(int)current_block_height + 1];
                    }
                    Block block;
                    lock (block_cache)
                    {
                        if (!block_cache.ContainsKey(hash)) break;
                        block = block_cache[hash];
                    }
                    Persist(block);
                    OnPersistCompleted(block);
                    lock (block_cache)
                    {
                        block_cache.Remove(hash);
                    }
                }
            }
        }
    }
}
