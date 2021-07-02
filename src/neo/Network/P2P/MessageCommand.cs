using Neo.Cryptography;
using Neo.IO.Caching;
using Neo.Network.P2P.Payloads;

namespace Neo.Network.P2P
{
    /// <summary>
    /// Represents the command of a message.
    /// </summary>
    public enum MessageCommand : byte
    {
        #region handshaking

        /// <summary>
        /// Sent when a connection is established.
        /// </summary>
        [ReflectionCache(typeof(VersionPayload))]
        Version = 0x00,

        /// <summary>
        /// Sent to respond to <see cref="Version"/> messages.
        /// </summary>
        Verack = 0x01,

        #endregion

        #region connectivity

        /// <summary>
        /// Sent to request for remote nodes.
        /// </summary>
        GetAddr = 0x10,

        /// <summary>
        /// Sent to respond to <see cref="GetAddr"/> messages.
        /// </summary>
        [ReflectionCache(typeof(AddrPayload))]
        Addr = 0x11,

        /// <summary>
        /// Sent to detect whether the connection has been disconnected.
        /// </summary>
        [ReflectionCache(typeof(PingPayload))]
        Ping = 0x18,

        /// <summary>
        /// Sent to respond to <see cref="Ping"/> messages.
        /// </summary>
        [ReflectionCache(typeof(PingPayload))]
        Pong = 0x19,

        #endregion

        #region synchronization

        /// <summary>
        /// Sent to request for headers.
        /// </summary>
        [ReflectionCache(typeof(GetBlockByIndexPayload))]
        GetHeaders = 0x20,

        /// <summary>
        /// Sent to respond to <see cref="GetHeaders"/> messages.
        /// </summary>
        [ReflectionCache(typeof(HeadersPayload))]
        Headers = 0x21,

        /// <summary>
        /// Sent to request for blocks.
        /// </summary>
        [ReflectionCache(typeof(GetBlocksPayload))]
        GetBlocks = 0x24,

        /// <summary>
        /// Sent to request for memory pool.
        /// </summary>
        Mempool = 0x25,

        /// <summary>
        /// Sent to relay inventories.
        /// </summary>
        [ReflectionCache(typeof(InvPayload))]
        Inv = 0x27,

        /// <summary>
        /// Sent to request for inventories.
        /// </summary>
        [ReflectionCache(typeof(InvPayload))]
        GetData = 0x28,

        /// <summary>
        /// Sent to request for blocks.
        /// </summary>
        [ReflectionCache(typeof(GetBlockByIndexPayload))]
        GetBlockByIndex = 0x29,

        /// <summary>
        /// Sent to respond to <see cref="GetData"/> messages when the inventories are not found.
        /// </summary>
        [ReflectionCache(typeof(InvPayload))]
        NotFound = 0x2a,

        /// <summary>
        /// Sent to send a transaction.
        /// </summary>
        [ReflectionCache(typeof(Transaction))]
        Transaction = 0x2b,

        /// <summary>
        /// Sent to send a block.
        /// </summary>
        [ReflectionCache(typeof(Block))]
        Block = 0x2c,

        /// <summary>
        /// Sent to send an <see cref="NotaryRequest"/>.
        /// </summary>
        [ReflectionCache(typeof(NotaryRequest))]
        Notary = 0x2d,

        /// <summary>
        /// Sent to send an <see cref="ExtensiblePayload"/>.
        /// </summary>
        [ReflectionCache(typeof(ExtensiblePayload))]
        Extensible = 0x2e,

        /// <summary>
        /// Sent to reject an inventory.
        /// </summary>
        Reject = 0x2f,

        #endregion

        #region SPV protocol

        /// <summary>
        /// Sent to load the <see cref="BloomFilter"/>.
        /// </summary>
        [ReflectionCache(typeof(FilterLoadPayload))]
        FilterLoad = 0x30,

        /// <summary>
        /// Sent to update the items for the <see cref="BloomFilter"/>.
        /// </summary>
        [ReflectionCache(typeof(FilterAddPayload))]
        FilterAdd = 0x31,

        /// <summary>
        /// Sent to clear the <see cref="BloomFilter"/>.
        /// </summary>
        FilterClear = 0x32,

        /// <summary>
        /// Sent to send a filtered block.
        /// </summary>
        [ReflectionCache(typeof(MerkleBlockPayload))]
        MerkleBlock = 0x38,

        #endregion

        #region others

        /// <summary>
        /// Sent to send an alert.
        /// </summary>
        Alert = 0x40,

        #endregion
    }
}
