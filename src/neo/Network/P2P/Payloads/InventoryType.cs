namespace Neo.Network.P2P.Payloads
{
    /// <summary>
    /// Represents the type of an inventory.
    /// </summary>
    public enum InventoryType : byte
    {
        /// <summary>
        /// Indicates that the inventory is a <see cref="Transaction"/>.
        /// </summary>
        TX = MessageCommand.Transaction,

        /// <summary>
        /// Indicates that the inventory is a <see cref="Block"/>.
        /// </summary>
        Block = MessageCommand.Block,

        /// <summary>
        /// Indicates that the inventory is an <see cref="ExtensiblePayload"/>.
        /// </summary>
        Extensible = MessageCommand.Extensible,

        /// <summary>
        /// Indicates that the inventory is an <see cref="NotaryRequest"/>.
        /// </summary>
        Notary = MessageCommand.Notary
    }
}
