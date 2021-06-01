using Neo.IO.Json;
using Neo.VM;
using Neo.VM.Types;
using System;

namespace Neo.IO.Serialization
{
    public sealed class MemorySerializer : Serializer<ReadOnlyMemory<byte>>
    {
        public override ReadOnlyMemory<byte> Deserialize(MemoryReader reader, SerializedAttribute attribute)
        {
            int max = attribute?.Max >= 0 ? attribute.Max : 0x1000000;
            return reader.ReadVarBytes(max);
        }

        public override ReadOnlyMemory<byte> FromJson(JObject json, SerializedAttribute attribute)
        {
            int max = attribute?.Max >= 0 ? attribute.Max : 0x1000000;
            byte[] result = Convert.FromBase64String(json.AsString());
            if (result.Length > max) throw new FormatException();
            return result;
        }

        public override ReadOnlyMemory<byte> FromStackItem(StackItem item, SerializedAttribute attribute)
        {
            int max = attribute?.Max >= 0 ? attribute.Max : 0x1000000;
            ReadOnlySpan<byte> result = item.GetSpan();
            if (result.Length > max) throw new FormatException();
            return result.ToArray();
        }

        public override void Serialize(MemoryWriter writer, ReadOnlyMemory<byte> value)
        {
            writer.WriteVarBytes(value.Span);
        }

        public override JObject ToJson(ReadOnlyMemory<byte> value)
        {
            return Convert.ToBase64String(value.Span);
        }

        public override StackItem ToStackItem(ReadOnlyMemory<byte> value, ReferenceCounter referenceCounter)
        {
            return value;
        }
    }
}
