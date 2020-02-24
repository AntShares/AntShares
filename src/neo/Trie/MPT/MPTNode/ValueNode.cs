using Neo.Cryptography;
using Neo.IO;
using System.IO;

namespace Neo.Trie.MPT
{
    public class ValueNode : MPTNode
    {
        public byte[] Value;

        protected override byte[] GenHash()
        {
            return Value.Length < 32 ? (byte[])Value.Clone() : Crypto.Hash256(Value);
        }

        public ValueNode()
        {
            nType = NodeType.ValueNode;
        }

        public ValueNode(byte[] val)
        {
            nType = NodeType.ValueNode;
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
    }
}
