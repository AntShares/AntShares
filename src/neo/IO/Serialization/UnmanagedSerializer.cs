using Neo.IO.Json;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Numerics;

namespace Neo.IO.Serialization
{
    public class UnmanagedSerializer<T> : Serializer<T> where T : unmanaged
    {
        private static readonly bool isUnsigned;

        static UnmanagedSerializer()
        {
            Type t = typeof(T);
            if (t.IsEnum) t = t.GetEnumUnderlyingType();
            isUnsigned = t == typeof(byte) || t == typeof(ushort) || t == typeof(uint) || t == typeof(ulong);
        }

        public unsafe override T Deserialize(MemoryReader reader, SerializedAttribute attribute)
        {
            ReadOnlyMemory<byte> buffer = reader.ReadBytes(sizeof(T));
            fixed (byte* p = buffer.Span)
            {
                return *(T*)p;
            }
        }

        public sealed override T FromJson(JObject json, SerializedAttribute attribute)
        {
            return (T)Convert.ChangeType(json.AsNumber(), typeof(T));
        }

        public unsafe sealed override T FromStackItem(StackItem item, SerializedAttribute attribute)
        {
            Span<byte> buffer = stackalloc byte[sizeof(T)];
            BigInteger bi = item.GetInteger();
            bi.TryWriteBytes(buffer, out int count, isUnsigned);
            if (count < buffer.Length)
                buffer[count..].Fill(bi.Sign < 0 ? byte.MaxValue : byte.MinValue);
            return *(T*)buffer.GetPinnableReference();
        }

        public unsafe override void Serialize(MemoryWriter writer, T value)
        {
            ReadOnlySpan<byte> buffer = new ReadOnlySpan<byte>(&value, sizeof(T));
            writer.Write(buffer);
        }

        public sealed override JObject ToJson(T value)
        {
            return (double)Convert.ChangeType(value, typeof(double));
        }

        public unsafe sealed override StackItem ToStackItem(T value, ReferenceCounter referenceCounter)
        {
            ReadOnlySpan<byte> buffer = new ReadOnlySpan<byte>(&value, sizeof(T));
            return new BigInteger(buffer, isUnsigned);
        }
    }
}
