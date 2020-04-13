using Neo.Cryptography;
using Neo.IO;
using Neo.IO.Json;
using System.IO;

namespace Neo.Trie.MPT
{
    public class LeafNode : MPTNode
    {
        public byte[] Value;

        public LeafNode()
        {
            nType = NodeType.LeafNode;
        }

        public LeafNode(byte[] val)
        {
            nType = NodeType.LeafNode;
            Value = (byte[])val.Clone();
        }

        public override void EncodeSpecific(BinaryWriter writer)
        {
            writer.WriteVarBytes(Value);
        }

        public override void DecodeSpecific(BinaryReader reader)
        {
            Value = reader.ReadVarBytes();
        }

        public override JObject ToJson()
        {
            var json = new JObject();
            json["value"] = Value.ToHexString();
            return json;
        }
    }
}
