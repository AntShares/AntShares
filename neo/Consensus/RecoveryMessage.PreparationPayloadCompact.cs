﻿using Neo.IO;
using Neo.Network.P2P.Payloads;
using System.IO;

namespace Neo.Consensus
{
    partial class RecoveryMessage
    {
        public class PreparationPayloadWitness : ISerializable
        {
            public ushort ValidatorIndex;
            public byte[] InvocationScript;

            int ISerializable.Size =>
                sizeof(ushort) +                //ValidatorIndex
                InvocationScript.GetVarSize();  //InvocationScript

            void ISerializable.Deserialize(BinaryReader reader)
            {
                ValidatorIndex = reader.ReadUInt16();
                InvocationScript = reader.ReadVarBytes(1024);
            }

            public static PreparationPayloadWitness FromPayload(ConsensusPayload payload)
            {
                return new PreparationPayloadWitness
                {
                    ValidatorIndex = payload.ValidatorIndex,
                    InvocationScript = payload.Witness.InvocationScript
                };
            }

            void ISerializable.Serialize(BinaryWriter writer)
            {
                writer.Write(ValidatorIndex);
                writer.WriteVarBytes(InvocationScript);
            }
        }
    }
}
