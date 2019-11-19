using Neo.IO.Caching;
using Neo.IO.Wrappers;
using Neo.Ledger;
using System;

namespace Neo.Persistence
{
    public class RawView : StoreView
    {
        private readonly IReadOnlyStore store;

        public override DataCache<UInt256, TrimmedBlock> Blocks => new StoreDataCache<UInt256, TrimmedBlock>(store, Prefixes.DATA_Block);
        public override DataCache<UInt256, TransactionState> Transactions => new StoreDataCache<UInt256, TransactionState>(store, Prefixes.DATA_Transaction);
        public override DataCache<UInt160, ContractState> Contracts => new StoreDataCache<UInt160, ContractState>(store, Prefixes.ST_Contract);
        public override DataCache<StorageKey, StorageItem> Storages => new StoreDataCache<StorageKey, StorageItem>(store, Prefixes.ST_Storage);
        public override DataCache<UInt32Wrapper, HeaderHashList> HeaderHashList => new StoreDataCache<UInt32Wrapper, HeaderHashList>(store, Prefixes.IX_HeaderHashList);
        public override MetaDataCache<HashIndexState> BlockHashIndex => new StoreMetaDataCache<HashIndexState>(store, Prefixes.IX_CurrentBlock);
        public override MetaDataCache<HashIndexState> HeaderHashIndex => new StoreMetaDataCache<HashIndexState>(store, Prefixes.IX_CurrentHeader);

        public RawView(IReadOnlyStore store)
        {
            this.store = store;
        }

        public override void Commit()
        {
            throw new NotSupportedException();
        }
    }
}
