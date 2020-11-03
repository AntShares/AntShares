using Neo.Cryptography.ECC;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;

namespace Neo.SmartContract.Native.Tokens
{
    public sealed class GasToken : Nep5Token<AccountState>
    {
        public override int Id => -2;
        public override string Name => "GAS";
        public override string Symbol => "gas";
        public override byte Decimals => 8;

        internal GasToken()
        {
        }

        internal override void Initialize(ApplicationEngine engine)
        {
            UInt160 account = Blockchain.GetConsensusAddress(Blockchain.StandbyValidators);
            Mint(engine, account, 70_000_000 * Factor); // Mint for Neo2.x migration. If there is any surplus, we can burn it later
        }

        protected override void OnPersist(ApplicationEngine engine)
        {
            base.OnPersist(engine);
            long totalNetworkFee = 0;
            foreach (Transaction tx in engine.Snapshot.PersistingBlock.Transactions)
            {
                Burn(engine, tx.Sender, tx.SystemFee + tx.NetworkFee);
                totalNetworkFee += tx.NetworkFee;
            }
            ECPoint[] validators = NEO.GetNextBlockValidators(engine.Snapshot);
            UInt160 primary = Contract.CreateSignatureRedeemScript(validators[engine.Snapshot.PersistingBlock.ConsensusData.PrimaryIndex]).ToScriptHash();
            Mint(engine, primary, totalNetworkFee);
        }
    }
}
