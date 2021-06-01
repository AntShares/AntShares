using Neo.IO.Json;
using Neo.VM;
using Neo.VM.Types;
using System;

namespace Neo.IO.Serialization
{
    public abstract class Serializable
    {
        private ReadOnlyMemory<byte> _memory { get; set; }

        public int Size => _memory.IsEmpty ? GetSize() : _memory.Length;

        protected abstract int GetSize();

        public ReadOnlyMemory<byte> ToArray()
        {
            if (_memory.IsEmpty) Serializer.Serialize(this);
            return _memory;
        }

        public JObject ToJson()
        {
            return Serializer.ToJson(this);
        }

        public StackItem ToStackItem(ReferenceCounter referenceCounter = null)
        {
            return Serializer.ToStackItem(this, referenceCounter);
        }
    }
}
