namespace Neo.IO.Serialization
{
    public sealed class NullableArraySerializer<T> : ArraySerializer<T> where T : Serializable
    {
        protected override T DeserializeElement(MemoryReader reader)
        {
            return reader.ReadBoolean() ? base.DeserializeElement(reader) : null;
        }

        protected override void SerializeElement(MemoryWriter writer, T value)
        {
            bool isNull = value is null;
            writer.Write(!isNull);
            if (!isNull) base.SerializeElement(writer, value);
        }
    }
}
