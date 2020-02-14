using Neo.Cryptography;
using Neo.IO;
using System.IO;

namespace Neo.Trie.MPT
{
    public class ShortNode : MPTNode
    {
        public byte[] Key;

        public MPTNode Next;

        public override int Size => 1 + Key.Length + Next.GetHash().Length;

        protected override byte[] CalHash()
        {
            return Key.Concat(Next.GetHash()).Sha256();
        }
        
        public ShortNode()
        {
            nType = NodeType.ShortNode;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.WriteVarBytes(Key);
            writer.WriteVarBytes(Next.GetHash());
        }

        public override void Deserialize(BinaryReader reader)
        {
            Key = reader.ReadVarBytes();
            var hashNode = new HashNode(reader.ReadVarBytes());
            Next = hashNode;
        }
    }
}
