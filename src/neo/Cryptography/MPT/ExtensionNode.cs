using Neo.IO;
using Neo.IO.Json;
using Neo.SmartContract;
using System.IO;

namespace Neo.Cryptography.MPT
{
    public class ExtensionNode : MPTNode
    {
        //max lenght when store StorageKey
        public const int MaxKeyLength = (InteropService.Storage.MaxKeySize + sizeof(int)) * 2;

        public byte[] Key;
        public MPTNode Next;

        protected override NodeType Type => NodeType.ExtensionNode;

        internal override void EncodeSpecific(BinaryWriter writer)
        {
            writer.WriteVarBytes(Key);
            WriteHash(writer, Next.Hash);
        }

        internal override void DecodeSpecific(BinaryReader reader)
        {
            Key = reader.ReadVarBytes(MaxKeyLength);
            Next = new HashNode();
            Next.DecodeSpecific(reader);
        }

        public override JObject ToJson()
        {
            return new JObject
            {
                ["key"] = Key.ToHexString(),
                ["next"] = Next.ToJson()
            };
        }
    }
}
