using Neo.IO.Json;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Array = Neo.VM.Types.Array;

namespace Neo.IO.Serialization
{
    public class CompositeSerializer<T> : Serializer<T> where T : Serializable
    {
        private (PropertyInfo, SerializedAttribute, Serializer)[] serializers;
        private Func<T> constructor;
        private Func<Serializable, ReadOnlyMemory<byte>> memoryGetter;
        private Action<Serializable, ReadOnlyMemory<byte>> memorySetter;

        protected virtual Type TargetType { get; } = typeof(T);

        public sealed override T Deserialize(MemoryReader reader, SerializedAttribute _)
        {
            EnsureInitialized();
            T obj = constructor();
            int start = reader.Position;
            foreach (var (info, attribute, serializer) in serializers)
                serializer.DeserializeProperty(reader, obj, info, attribute);
            int end = reader.Position;
            memorySetter(obj, reader.GetMemory(start..end));
            return obj;
        }

        private void EnsureInitialized()
        {
            if (serializers is null)
            {
                serializers = GetSerializers().OrderBy(p => p.Item2.Order).ToArray();
            }
            if (constructor is null)
            {
                var constructorInfo = TargetType.GetConstructor(Type.EmptyTypes);
                var newExpr = Expression.New(constructorInfo);
                var constructorExpr = Expression.Lambda<Func<T>>(newExpr);
                constructor = constructorExpr.Compile();
            }
            if (memoryGetter is null)
            {
                PropertyInfo info = typeof(Serializable).GetProperty("_memory", BindingFlags.NonPublic | BindingFlags.Instance);
                memoryGetter = (Func<Serializable, ReadOnlyMemory<byte>>)info.GetMethod.CreateDelegate(typeof(Func<Serializable, ReadOnlyMemory<byte>>));
                memorySetter = (Action<Serializable, ReadOnlyMemory<byte>>)info.SetMethod.CreateDelegate(typeof(Action<Serializable, ReadOnlyMemory<byte>>));
            }
        }

        public sealed override T FromJson(JObject json, SerializedAttribute _)
        {
            EnsureInitialized();
            T obj = constructor();
            foreach (var (info, attribute, serializer) in serializers)
                serializer.PropertyFromJson(json[info.Name.ToLower()], obj, info, attribute);
            return obj;
        }

        public sealed override T FromStackItem(StackItem item, SerializedAttribute _)
        {
            EnsureInitialized();
            T obj = constructor();
            Array array = (Array)item;
            for (int i = 0; i < serializers.Length; i++)
            {
                var (info, attribute, serializer) = serializers[i];
                serializer.PropertyFromStackItem(array[i], obj, info, attribute);
            }
            return obj;
        }

        private static Dictionary<string, PropertyInfo> GetProperties(Type type)
        {
            if (type.BaseType is null) return new Dictionary<string, PropertyInfo>();
            Dictionary<string, PropertyInfo> dictionary = GetProperties(type.BaseType);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                dictionary[property.Name] = property;
            return dictionary;
        }

        private IEnumerable<(PropertyInfo, SerializedAttribute, Serializer)> GetSerializers()
        {
            foreach (PropertyInfo info in GetProperties(TargetType).Values)
            {
                if (info.GetMethod is null || info.SetMethod is null) continue;
                SerializedAttribute attribute = info.GetCustomAttribute<SerializedAttribute>();
                if (attribute is null) continue;
                Serializer serializer = attribute.Serializer is null
                    ? GetDefaultSerializer(info.PropertyType)
                    : (Serializer)Activator.CreateInstance(attribute.Serializer);
                yield return (info, attribute, serializer);
            }
        }

        public sealed override void Serialize(MemoryWriter writer, T value)
        {
            EnsureInitialized();
            ReadOnlyMemory<byte> memory = memoryGetter(value);
            if (memory.IsEmpty)
            {
                int start = writer.Position;
                foreach (var (info, _, serializer) in serializers)
                    serializer.SerializeProperty(writer, value, info);
                int end = writer.Position;
                memorySetter(value, writer.GetMemory(start..end));
            }
            else
            {
                writer.Write(memory.Span);
            }
        }

        public sealed override JObject ToJson(T value)
        {
            EnsureInitialized();
            JObject json = new JObject();
            foreach (var (info, _, serializer) in serializers)
                json[info.Name.ToLower()] = serializer.PropertyToJson(value, info);
            return json;
        }

        public sealed override StackItem ToStackItem(T value, ReferenceCounter referenceCounter)
        {
            EnsureInitialized();
            Array array = new Array(referenceCounter);
            foreach (var (info, _, serializer) in serializers)
                array.Add(serializer.PropertyToStackItem(value, info, referenceCounter));
            return array;
        }
    }
}
