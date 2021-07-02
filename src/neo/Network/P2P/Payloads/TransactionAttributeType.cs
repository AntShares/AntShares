using Neo.IO.Caching;

namespace Neo.Network.P2P.Payloads
{
    /// <summary>
    /// Represents the type of a <see cref="TransactionAttribute"/>.
    /// </summary>
    public enum TransactionAttributeType : byte
    {
        /// <summary>
        /// Indicates that the transaction is of high priority.
        /// </summary>
        [ReflectionCache(typeof(HighPriorityAttribute))]
        HighPriority = 0x01,

        /// <summary>
        /// Indicates that the transaction is an oracle response.
        /// </summary>
        [ReflectionCache(typeof(OracleResponse))]
        OracleResponse = 0x11,

        /// <summary>
        /// Indicates that the transaction is not valid before <see cref="NotValidBefore.Height"/>.
        /// </summary>
        [ReflectionCache(typeof(NotValidBefore))]
        NotValidBefore = 0xe0,

        /// <summary>
        /// Indicates that the transaction is conflict with <see cref="ConflictAttribute.Hash"/>.
        /// </summary>
        [ReflectionCache(typeof(ConflictAttribute))]
        Conflict = 0xe1,

        /// <summary>
        /// Indicates that the transaction need Notarys to collect signatures.
        /// </summary>
        [ReflectionCache(typeof(NotaryAssisted))]
        NotaryAssisted = 0xe2
    }
}
