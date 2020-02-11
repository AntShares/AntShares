using System.IO;
using Neo.Cryptography;
using Neo.IO;

namespace Neo.Trie.MPT
{
    public class ValueNode : MPTNode
    {
        public byte[] Value;

        protected override byte[] CalHash()
        {
            return Value.Length < 32 ? (byte[])Value.Clone() : Value.Sha256();
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

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.WriteVarBytes(Value);
        }

        public override void Deserialize(BinaryReader reader)
        {
            Value = reader.ReadVarBytes();
        }
    }
}
