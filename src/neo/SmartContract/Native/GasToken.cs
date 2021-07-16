#pragma warning disable IDE0051

using Neo.Cryptography.ECC;
using Neo.Network.P2P.Payloads;
using System;

namespace Neo.SmartContract.Native
{
    /// <summary>
    /// Represents the GAS token in the NEO system.
    /// </summary>
    public sealed class GasToken : FungibleToken<AccountState>
    {
        public override string Symbol => "GAS";
        public override byte Decimals => 8;

        internal GasToken()
        {
        }

        internal override ContractTask Initialize(ApplicationEngine engine)
        {
            UInt160 account = Contract.GetBFTAddress(engine.ProtocolSettings.StandbyValidators);
            return Mint(engine, account, 52_000_000 * Factor, false);
        }

        internal override async ContractTask OnPersist(ApplicationEngine engine)
        {
            long totalNetworkFee = 0;
            foreach (Transaction tx in engine.PersistingBlock.Transactions)
            {
                await Burn(engine, tx.Sender, tx.SystemFee + tx.NetworkFee);
                totalNetworkFee += tx.NetworkFee;
            }
            ECPoint[] validators = NEO.GetNextBlockValidators(engine.Snapshot, engine.ProtocolSettings.ValidatorsCount);
            UInt160 primary = Contract.CreateSignatureRedeemScript(validators[engine.PersistingBlock.PrimaryIndex]).ToScriptHash();
            await Mint(engine, primary, totalNetworkFee, false);
        }

        [ContractMethod(CpuFee = 1 << 15, RequiredCallFlags = CallFlags.States | CallFlags.AllowNotify)]
        private async ContractTask Refuel(ApplicationEngine engine, UInt160 account, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!engine.CheckWitnessInternal(account)) throw new InvalidOperationException();
            await Burn(engine, account, amount);
            engine.Refuel(amount);
        }
    }
}
