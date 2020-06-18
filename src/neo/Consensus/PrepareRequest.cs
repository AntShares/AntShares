using Neo.IO;
using Neo.Network.P2P.Payloads;
using System;
using System.IO;
using System.Linq;

namespace Neo.Consensus
{
    public class PrepareRequest : ConsensusMessage
    {
        public ulong Timestamp;
        public ulong Nonce;
        public UInt256[] TransactionHashes;
        public byte[] StateRootSignature;

        public override int Size => base.Size
            + sizeof(ulong)                      //Timestamp
            + sizeof(ulong)                     //Nonce
            + StateRootSignature.GetVarSize()   //StateRootSignature
            + TransactionHashes.GetVarSize();   //TransactionHashes

        public PrepareRequest()
            : base(ConsensusMessageType.PrepareRequest)
        {
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            Timestamp = reader.ReadUInt64();
            Nonce = reader.ReadUInt64();
            StateRootSignature = reader.ReadVarBytes(1024);
            TransactionHashes = reader.ReadSerializableArray<UInt256>(Block.MaxTransactionsPerBlock);
            if (TransactionHashes.Distinct().Count() != TransactionHashes.Length)
                throw new FormatException();
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(Timestamp);
            writer.Write(Nonce);
            writer.WriteVarBytes(StateRootSignature);
            writer.Write(TransactionHashes);
        }
    }
}
