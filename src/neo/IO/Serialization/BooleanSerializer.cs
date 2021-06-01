using Neo.IO.Json;
using Neo.VM;
using Neo.VM.Types;

namespace Neo.IO.Serialization
{
    public sealed class BooleanSerializer : Serializer<bool>
    {
        public override bool Deserialize(MemoryReader reader, SerializedAttribute attribute)
        {
            return reader.ReadBoolean();
        }

        public override bool FromJson(JObject json, SerializedAttribute attribute)
        {
            return json.AsBoolean();
        }

        public override bool FromStackItem(StackItem item, SerializedAttribute attribute)
        {
            return item.GetBoolean();
        }

        public override void Serialize(MemoryWriter writer, bool value)
        {
            writer.Write(value);
        }

        public override JObject ToJson(bool value)
        {
            return value;
        }

        public override StackItem ToStackItem(bool value, ReferenceCounter referenceCounter)
        {
            return value;
        }
    }
}
