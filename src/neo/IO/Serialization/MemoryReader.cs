using System;
using System.Buffers.Binary;

namespace Neo.IO.Serialization
{
    public sealed class MemoryReader
    {
        private readonly ReadOnlyMemory<byte> memory;

        public int Position { get; private set; }
        private ReadOnlySpan<byte> Span => memory.Span[Position..];

        public MemoryReader(ReadOnlyMemory<byte> memory)
        {
            this.memory = memory;
        }

        internal ReadOnlyMemory<byte> GetMemory(Range range)
        {
            return memory[range];
        }

        public byte Peek()
        {
            return Span[0];
        }

        public bool ReadBoolean()
        {
            return ReadByte() != 0;
        }

        public byte ReadByte()
        {
            byte result = Span[0];
            Position++;
            return result;
        }

        public ReadOnlyMemory<byte> ReadBytes(int size)
        {
            ReadOnlyMemory<byte> result = memory.Slice(Position, size);
            Position += size;
            return result;
        }

        public ushort ReadUInt16()
        {
            ushort result = BinaryPrimitives.ReadUInt16LittleEndian(Span);
            Position += sizeof(ushort);
            return result;
        }

        public uint ReadUInt32()
        {
            uint result = BinaryPrimitives.ReadUInt32LittleEndian(Span);
            Position += sizeof(uint);
            return result;
        }

        public ulong ReadUInt64()
        {
            ulong result = BinaryPrimitives.ReadUInt64LittleEndian(Span);
            Position += sizeof(ulong);
            return result;
        }

        public ReadOnlyMemory<byte> ReadVarBytes(int max = 0x1000000)
        {
            return ReadBytes((int)ReadVarInt((ulong)max));
        }

        public ulong ReadVarInt(ulong max = ulong.MaxValue)
        {
            byte fb = ReadByte();
            ulong value;
            if (fb == 0xFD)
                value = ReadUInt16();
            else if (fb == 0xFE)
                value = ReadUInt32();
            else if (fb == 0xFF)
                value = ReadUInt64();
            else
                value = fb;
            if (value > max) throw new FormatException();
            return value;
        }

        public string ReadVarString(int max = 0x1000000)
        {
            return Utility.StrictUTF8.GetString(ReadVarBytes(max).Span);
        }

        public static implicit operator MemoryReader(ReadOnlyMemory<byte> memory)
        {
            return new MemoryReader(memory);
        }

        public static implicit operator MemoryReader(byte[] memory)
        {
            return new MemoryReader(memory);
        }
    }
}
