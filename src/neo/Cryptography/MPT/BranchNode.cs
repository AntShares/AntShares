using Neo.IO.Json;
using System.IO;

namespace Neo.Cryptography.MPT
{
    public class BranchNode : MPTNode
    {
        public const int ChildCount = 17;
        public readonly MPTNode[] Children = new MPTNode[ChildCount];

        protected override NodeType Type => NodeType.BranchNode;

        public BranchNode()
        {
            for (int i = 0; i < ChildCount; i++)
            {
                Children[i] = HashNode.EmptyNode;
            }
        }

        public override void EncodeSpecific(BinaryWriter writer)
        {
            for (int i = 0; i < ChildCount; i++)
            {
                var hashNode = new HashNode(Children[i].Hash);
                hashNode.EncodeSpecific(writer);
            }
        }

        public override void DecodeSpecific(BinaryReader reader)
        {
            for (int i = 0; i < ChildCount; i++)
            {
                var hashNode = new HashNode();
                hashNode.DecodeSpecific(reader);
                Children[i] = hashNode;
            }
        }

        public override JObject ToJson()
        {
            var jarr = new JArray();
            for (int i = 0; i < ChildCount; i++)
            {
                jarr.Add(Children[i].ToJson());
            }
            return jarr;
        }
    }
}
