using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Data.RocksDB;
using RocksDbSharp;
using System;

namespace Neo.Persistence.RocksDB
{
    internal class DbMetaDataCache<T> : MetaDataCache<T>
        where T : class, ICloneable<T>, ISerializable, new()
    {
        /// <summary>
        /// In RocksDB we use the family as prefix, so we don't need a key for MetaDataCache
        /// </summary>
        private static readonly byte[] EmptyKey = new byte[0];

        private readonly DB db;
        private readonly ReadOptions options;
        private readonly WriteBatch batch;
        private readonly ColumnFamilyHandle family;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="db">DB</param>
        /// <param name="options">Options</param>
        /// <param name="batch">Batch</param>
        /// <param name="family">Column familiy, is used as a prefix</param>
        /// <param name="factory">Factory</param>
        public DbMetaDataCache(DB db, ReadOptions options, WriteBatch batch, ColumnFamilyHandle family, Func<T> factory = null)
            : base(factory)
        {
            this.db = db;
            this.options = options ?? DB.ReadDefault;
            this.batch = batch;
            this.family = family;
        }

        protected override void AddInternal(T item)
        {
            batch?.Put(EmptyKey, item.ToArray(), family);
        }

        protected override T TryGetInternal()
        {
            if (!db.TryGet(family, options, EmptyKey, out var value))
                return null;
            return value.AsSerializable<T>();
        }

        protected override void UpdateInternal(T item)
        {
            batch?.Put(EmptyKey, item.ToArray(), family);
        }
    }
}
