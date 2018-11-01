﻿using Neo.IO;
using Neo.Network.P2P.Payloads;
using System;
using System.IO;
using System.Linq;

namespace Neo.Consensus
{
    internal class PrepareRequest : ConsensusMessage
    {
        public ulong Nonce;
        public UInt160 NextConsensus;
        public UInt256[] TransactionHashes;
        public MinerTransaction MinerTransaction;
        public byte[] Signature;

        public PrepareRequest()
            : base(ConsensusMessageType.PrepareRequest)
        {
        }

        public static PrepareRequest Make(Block _header, UInt256[] TransactionHashes, MinerTransaction MinerTx, byte[] MySignature)
        {
            return new PrepareRequest
            {
                Nonce = _header.ConsensusData,
                NextConsensus = _header.NextConsensus,
                TransactionHashes = TransactionHashes,
                MinerTransaction = MinerTx,//(MinerTransaction)Transactions[TransactionHashes[0]],
                Signature = MySignature//Signatures[MyIndex]
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            Nonce = reader.ReadUInt64();
            NextConsensus = reader.ReadSerializable<UInt160>();
            TransactionHashes = reader.ReadSerializableArray<UInt256>();
            if (TransactionHashes.Distinct().Count() != TransactionHashes.Length)
                throw new FormatException();
            MinerTransaction = reader.ReadSerializable<MinerTransaction>();
            if (MinerTransaction.Hash != TransactionHashes[0])
                throw new FormatException();
            Signature = reader.ReadBytes(64);
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(Nonce);
            writer.Write(NextConsensus);
            writer.Write(TransactionHashes);
            writer.Write(MinerTransaction);
            writer.Write(Signature);
        }
    }
}
