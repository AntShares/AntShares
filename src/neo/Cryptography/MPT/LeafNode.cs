using Neo.IO;
using Neo.IO.Json;
using Neo.SmartContract;
using System;
using System.IO;

namespace Neo.Cryptography.MPT
{
    public class LeafNode : MPTNode
    {
        //the max size when store StorageItem
        public const int MaxValueLength = 3 + InteropService.Storage.MaxValueSize + sizeof(bool);

        public byte[] Value;

        protected override NodeType Type => NodeType.LeafNode;

        public LeafNode()
        {
        }

        public LeafNode(ReadOnlySpan<byte> value)
        {
            Value = value.ToArray();
        }

        internal override void EncodeSpecific(BinaryWriter writer)
        {
            writer.WriteVarBytes(Value);
        }

        internal override void DecodeSpecific(BinaryReader reader)
        {
            Value = reader.ReadVarBytes(MaxValueLength);
        }

        public override JObject ToJson()
        {
            return new JObject
            {
                ["value"] = Value.ToHexString()
            };
        }
    }
}
