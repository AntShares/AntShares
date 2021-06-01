using Neo.IO.Caching;
using Neo.IO.Json;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Array = Neo.VM.Types.Array;

namespace Neo.IO.Serialization
{
    public sealed partial class PolymorphousArraySerializer<T, TEnum> : ArraySerializer<T>
        where T : Serializable
        where TEnum : unmanaged, Enum
    {
        private static readonly Dictionary<Type, ElementSerializer> serializers = ReflectionCache<TEnum>.GetTypes().ToDictionary(p => p, p => new ElementSerializer(p));
        private static readonly string typeKey = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).First(p => p.PropertyType == typeof(TEnum)).Name.ToLower();

        protected override T DeserializeElement(MemoryReader reader)
        {
            TEnum e = (TEnum)Enum.ToObject(typeof(TEnum), reader.Peek());
            Type type = ReflectionCache<TEnum>.GetType(e);
            ElementSerializer serializer = serializers[type];
            return serializer.Deserialize(reader, null);
        }

        protected override T ElementFromJson(JObject json)
        {
            TEnum e = json[typeKey].TryGetEnum<TEnum>();
            Type type = ReflectionCache<TEnum>.GetType(e);
            ElementSerializer serializer = serializers[type];
            return serializer.FromJson(json, null);
        }

        protected override T ElementFromStackItem(StackItem item)
        {
            Array array = (Array)item;
            Serializer<TEnum> enumSerializer = GetDefaultSerializer<TEnum>();
            TEnum e = enumSerializer.FromStackItem(array[0], null);
            Type type = ReflectionCache<TEnum>.GetType(e);
            ElementSerializer serializer = serializers[type];
            return serializer.FromStackItem(item, null);
        }

        protected override JObject ElementToJson(T value)
        {
            ElementSerializer serializer = serializers[value.GetType()];
            return serializer.ToJson(value);
        }

        protected override StackItem ElementToStackItem(T value, ReferenceCounter referenceCounter)
        {
            ElementSerializer serializer = serializers[value.GetType()];
            return serializer.ToStackItem(value, referenceCounter);
        }

        protected override void SerializeElement(MemoryWriter writer, T value)
        {
            ElementSerializer serializer = serializers[value.GetType()];
            serializer.Serialize(writer, value);
        }
    }
}
