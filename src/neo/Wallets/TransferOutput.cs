namespace Neo.Wallets
{
    public class TransferOutput
    {
        public UInt160 AssetId;
        public BigDecimal Value;
        public UInt160 ScriptHash;
        public bool SolidTransfer;
    }
}
