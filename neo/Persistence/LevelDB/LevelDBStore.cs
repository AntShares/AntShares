﻿using Neo.IO.Caching;
using Neo.IO.Data.LevelDB;
using Neo.IO.Wrappers;
using Neo.Ledger;
using System;
using System.Reflection;

namespace Neo.Persistence.LevelDB
{
    public class LevelDBStore : Store, IDisposable
    {
        private readonly DB db;
        private readonly ReadOptions options;
        private readonly WriteOptions writeSync;

        public LevelDBStore(string path)
        {
            db = DB.Open(path, Options.Default);
            if (db.TryGet(ReadOptions.Default, SliceBuilder.Begin(Prefixes.SYS_Version), out Slice value) && Version.TryParse(value.ToString(), out Version version) && version >= Version.Parse("2.9.1"))
                return;

            options = new ReadOptions { FillCache = false };
            writeSync = new WriteOptions { Sync = true };

            using (WriteBatch batch = new WriteBatch())
            {
                using (Iterator it = db.NewIterator(options))
                {
                    for (it.SeekToFirst(); it.Valid(); it.Next())
                    {
                        batch.Delete(it.Key());
                    }
                }
                db.Put(WriteOptions.Default, SliceBuilder.Begin(Prefixes.SYS_Version), Assembly.GetExecutingAssembly().GetName().Version.ToString());
                db.Write(WriteOptions.Default, batch);
            }
        }

        public void Dispose()
        {
            db.Dispose();
            options.Dispose();
            writeSync.Dispose();
        }

        public override byte[] Get(byte prefix, byte[] key)
        {
            if (!db.TryGet(ReadOptions.Default, SliceBuilder.Begin(prefix).Add(key), out Slice slice))
                return null;
            return slice.ToArray();
        }

        public override DataCache<UInt256, TrimmedBlock> GetBlocks()
        {
            return new DbCache<UInt256, TrimmedBlock>(db, null, null, Prefixes.DATA_Block);
        }

        public override DataCache<UInt160, ContractState> GetContracts()
        {
            return new DbCache<UInt160, ContractState>(db, null, null, Prefixes.ST_Contract);
        }

        public override Snapshot GetSnapshot()
        {
            return new DbSnapshot(db);
        }

        public override DataCache<StorageKey, StorageItem> GetStorages()
        {
            return new DbCache<StorageKey, StorageItem>(db, null, null, Prefixes.ST_Storage);
        }

        public override DataCache<UInt256, TransactionState> GetTransactions()
        {
            return new DbCache<UInt256, TransactionState>(db, null, null, Prefixes.DATA_Transaction);
        }

        public override DataCache<UInt32Wrapper, HeaderHashList> GetHeaderHashList()
        {
            return new DbCache<UInt32Wrapper, HeaderHashList>(db, null, null, Prefixes.IX_HeaderHashList);
        }

        public override MetaDataCache<HashIndexState> GetBlockHashIndex()
        {
            return new DbMetaDataCache<HashIndexState>(db, null, null, Prefixes.IX_CurrentBlock);
        }

        public override MetaDataCache<HashIndexState> GetHeaderHashIndex()
        {
            return new DbMetaDataCache<HashIndexState>(db, null, null, Prefixes.IX_CurrentHeader);
        }

        public override void Put(byte prefix, byte[] key, byte[] value)
        {
            db.Put(WriteOptions.Default, SliceBuilder.Begin(prefix).Add(key), value);
        }

        public override void PutSync(byte prefix, byte[] key, byte[] value)
        {
            db.Put(writeSync, SliceBuilder.Begin(prefix).Add(key), value);
        }
    }
}
