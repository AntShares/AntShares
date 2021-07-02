using Neo.IO.Json;
using Neo.Persistence;
using Neo.SmartContract.Native;
using System.IO;
using System.Linq;

namespace Neo.Network.P2P.Payloads
{
    /// <summary>
    /// Indicates that the transaction is an Notrary tx.
    /// </summary>
    public class NotaryAssisted : TransactionAttribute
    {
        /// <summary>
        /// Indicates how many signatures the Notary need to collect.
        /// </summary>
        public byte NKeys;

        public override TransactionAttributeType Type => TransactionAttributeType.NotaryAssisted;

        public override bool AllowMultiple => false;

        protected override void DeserializeWithoutType(BinaryReader reader)
        {
            NKeys = reader.ReadByte();
        }

        protected override void SerializeWithoutType(BinaryWriter writer)
        {
            writer.Write(NKeys);
        }

        public override JObject ToJson()
        {
            JObject json = base.ToJson();
            json["nkeys"] = NKeys;
            return json;
        }

        public override bool Verify(DataCache snapshot, Transaction tx)
        {
            return tx.Signers.First(p => p.Account.Equals(NativeContract.Notary.Hash)) is not null;
        }
    }
}
