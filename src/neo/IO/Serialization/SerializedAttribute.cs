using System;

namespace Neo.IO.Serialization
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class SerializedAttribute : Attribute
    {
        public int Order { get; }
        public Type Serializer { get; set; }
        public int Max { get; set; } = -1;

        public SerializedAttribute(int order)
        {
            this.Order = order;
        }
    }
}
