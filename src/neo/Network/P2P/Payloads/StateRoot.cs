using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.IO.Json;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.SmartContract.Native.Designate;
using System;
using System.IO;

namespace Neo.Network.P2P.Payloads
{
    public class StateRoot : ICloneable<StateRoot>, IVerifiable, ISerializable
    {
        public uint Index;
        public UInt256 RootHash;
        public Witness Witness;

        private UInt256 _hash = null;

        public UInt256 Hash
        {
            get
            {
                if (_hash == null)
                {
                    _hash = new UInt256(Crypto.Hash256(this.GetHashData()));
                }
                return _hash;
            }
        }

        Witness[] IVerifiable.Witnesses
        {
            get
            {
                return new[] { Witness };
            }
            set
            {
                if (value.Length != 1) throw new ArgumentException();
                Witness = value[0];
            }
        }

        public int Size =>
            sizeof(uint) +      //Index
            UInt256.Length +    //RootHash
            Witness.Size;       //Witness

        public StateRoot Clone()
        {
            return new StateRoot
            {
                Index = Index,
                RootHash = RootHash,
                Witness = Witness,
            };
        }

        public void FromReplica(StateRoot replica)
        {
            Index = replica.Index;
            RootHash = replica.RootHash;
            Witness = replica.Witness;
        }

        public void Deserialize(BinaryReader reader)
        {
            this.DeserializeUnsigned(reader);
            Witness[] arr = reader.ReadSerializableArray<Witness>();
            if (arr.Length < 1)
                Witness = null;
            else
                Witness = arr[0];
        }

        public void DeserializeUnsigned(BinaryReader reader)
        {
            Index = reader.ReadUInt32();
            RootHash = reader.ReadSerializable<UInt256>();
        }

        public void Serialize(BinaryWriter writer)
        {
            this.SerializeUnsigned(writer);
            if (Witness is null)
                writer.WriteVarInt(0);
            else
                writer.Write(new Witness[] { Witness });
        }

        public void SerializeUnsigned(BinaryWriter writer)
        {
            writer.Write(Index);
            writer.Write(RootHash);
        }

        public bool Verify(StoreView snapshot)
        {
            return this.VerifyWitnesses(snapshot, 1_00000000, WitnessFlag.StateIndependent);
        }

        public UInt160[] GetScriptHashesForVerifying(StoreView snapshot)
        {
            ECPoint[] validators = NativeContract.Designate.GetDesignatedByRole(snapshot, Role.StateValidator, Index);
            if (validators.Length < 1) throw new InvalidOperationException("No script hash for state root verifying");
            Contract contract = Contract.CreateMultiSigContract(validators.Length - (validators.Length - 1) / 3, validators);
            UInt160 script_hash = contract.ScriptHash;
            if (script_hash is null) throw new InvalidOperationException("No script hash for state root verifying");
            return new UInt160[] { script_hash };
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["index"] = Index;
            json["roothash"] = RootHash.ToString();
            json["witness"] = Witness?.ToJson();
            return json;
        }
    }
}
