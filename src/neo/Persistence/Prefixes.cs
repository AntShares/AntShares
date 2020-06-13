namespace Neo.Persistence
{
    internal static class Prefixes
    {
        public const byte DATA_Block = 0x01;
        public const byte DATA_Transaction = 0x02;

        public const byte ST_Contract = 0x50;
        public const byte ST_LocalStateRoot = 0x60;

        public const byte IX_HeaderHashList = 0x80;
        public const byte IX_CurrentBlock = 0xc0;
        public const byte IX_CurrentHeader = 0xc1;
        public const byte IX_ContractId = 0xc2;
        public const byte IX_ValidatorsStateRoot = 0xc4;

        /* Prefixes 0xf0 to 0xff are reserved for external use.
         *
         * Note: The saved consensus state uses the Prefix 0xf4
         */
    }
}
