using System;
using System.IO;

namespace Neo.IO.Serialization
{
    public sealed class MemoryWriter : BinaryWriter
    {
        public int Position => (int)BaseStream.Position;

        public MemoryWriter()
            : base(new MemoryStream(), Utility.StrictUTF8, false)
        {
        }

        public ReadOnlyMemory<byte> GetMemory(Range range)
        {
            Flush();
            MemoryStream ms = (MemoryStream)BaseStream;
            return ms.GetBuffer().AsMemory(range);
        }

        public byte[] ToArray()
        {
            Flush();
            MemoryStream ms = (MemoryStream)BaseStream;
            return ms.ToArray();
        }
    }
}
