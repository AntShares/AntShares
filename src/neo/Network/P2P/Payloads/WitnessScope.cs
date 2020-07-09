using System;

namespace Neo.Network.P2P.Payloads
{
    [Flags]
    public enum WitnessScope : byte
    {
        /// <summary>
        /// It's only valid for be a sender, it can't be used during the execution
        /// </summary>
        FeeOnly = 0x00,

        /// <summary>
        /// Global allows this witness in all contexts (default Neo2 behavior)
        /// This cannot be combined with other flags
        /// </summary>
        Global = 0x01,

        /// <summary>
        /// CalledByEntry means that this condition must hold: EntryScriptHash == CallingScriptHash
        /// No params is needed, as the witness/permission/signature given on first invocation will automatically expire if entering deeper internal invokes
        /// This can be default safe choice for native NEO/GAS (previously used on Neo 2 as "attach" mode)
        /// </summary>
        CalledByEntry = 0x10,

        /// <summary>
        /// Custom hash for contract-specific
        /// </summary>
        CustomContracts = 0x20,

        /// <summary>
        ///  Custom pubkey for group members
        /// </summary>
        CustomGroups = 0x30
    }
}
