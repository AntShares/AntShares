﻿using LevelDB;
using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Data.LevelDB;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Neo.Persistence.LevelDB
{
    public class DbCache<TKey, TValue> : DataCache<TKey, TValue>
        where TKey : IEquatable<TKey>, ISerializable, new()
        where TValue : class, ICloneable<TValue>, ISerializable, new()
    {
        private readonly DB db;
        private readonly ReadOptions options;
        private readonly WriteBatch batch;
        private readonly byte prefix;

        public DbCache(DB db, ReadOptions options, WriteBatch batch, byte prefix)
        {
            this.db = db;
            this.options = options ?? new ReadOptions();
            this.batch = batch;
            this.prefix = prefix;
        }

        protected override void AddInternal(TKey key, TValue value)
        {
            batch?.Put(SliceBuilder.Begin(prefix).Add(key).ToArray(), value.ToArray());
        }

        public override void DeleteInternal(TKey key)
        {
            batch?.Delete(SliceBuilder.Begin(prefix).Add(key).ToArray());
        }

        protected override IEnumerable<KeyValuePair<TKey, TValue>> FindInternal(byte[] key_prefix)
        {
            return db.Find(options, SliceBuilder.Begin(prefix).Add(key_prefix), (k, v) => new KeyValuePair<TKey, TValue>(k.ToArray().AsSerializable<TKey>(1), v.ToArray().AsSerializable<TValue>()));
        }

        protected override TValue GetInternal(TKey key)
        {
            return db.Get<TValue>(options, prefix, key);
        }

        protected override TValue TryGetInternal(TKey key)
        {
            return db.TryGet<TValue>(options, prefix, key);
        }

        protected override void UpdateInternal(TKey key, TValue value)
        {
            batch?.Put(SliceBuilder.Begin(prefix).Add(key).ToArray(), value.ToArray());
        }

        public override void Dispose()
        {
            if (options != null) {
                MethodInfo method = options.GetType().GetMethod("FreeUnManagedObjects", BindingFlags.NonPublic | BindingFlags.Instance);
                method.Invoke(options, null);
            }
        }
    }
}
