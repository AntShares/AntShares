using Neo.IO;
using System;
using System.IO;

namespace Neo.Network.P2P.Payloads
{
    public class GetFullBlocksPayload : ISerializable
    {
        private const ushort MaxBlocksCount = 500;
        public uint IndexStart;
        public ushort Count;

        public int Size => sizeof(uint) + sizeof(ushort);

        public static GetFullBlocksPayload Create(uint index_start, ushort count = 500)
        {
            return new GetFullBlocksPayload
            {
                IndexStart = index_start,
                Count = count
            };
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            IndexStart = reader.ReadUInt32();
            Count = reader.ReadUInt16();
            if (Count == 0 || Count > MaxBlocksCount) throw new FormatException();
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(IndexStart);
            writer.Write(Count);
        }
    }
}
