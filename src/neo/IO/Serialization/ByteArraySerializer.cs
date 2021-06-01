using Neo.IO.Json;
using Neo.VM;
using Neo.VM.Types;
using System;

namespace Neo.IO.Serialization
{
    public sealed class ByteArraySerializer : Serializer<byte[]>
    {
        public override byte[] Deserialize(MemoryReader reader, SerializedAttribute attribute)
        {
            int max = attribute?.Max >= 0 ? attribute.Max : 0x1000000;
            return reader.ReadVarBytes(max).ToArray();
        }

        public override byte[] FromJson(JObject json, SerializedAttribute attribute)
        {
            int max = attribute?.Max >= 0 ? attribute.Max : 0x1000000;
            byte[] result = Convert.FromBase64String(json.AsString());
            if (result.Length > max) throw new FormatException();
            return result;
        }

        public override byte[] FromStackItem(StackItem item, SerializedAttribute attribute)
        {
            int max = attribute?.Max >= 0 ? attribute.Max : 0x1000000;
            ReadOnlySpan<byte> result = item.GetSpan();
            if (result.Length > max) throw new FormatException();
            return result.ToArray();
        }

        public override void Serialize(MemoryWriter writer, byte[] value)
        {
            writer.WriteVarBytes(value);
        }

        public override JObject ToJson(byte[] value)
        {
            return Convert.ToBase64String(value);
        }

        public override StackItem ToStackItem(byte[] value, ReferenceCounter referenceCounter)
        {
            return value;
        }
    }
}
