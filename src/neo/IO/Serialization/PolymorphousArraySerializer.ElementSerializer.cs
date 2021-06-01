using System;

namespace Neo.IO.Serialization
{
    partial class PolymorphousArraySerializer<T, TEnum>
    {
        private class ElementSerializer : CompositeSerializer<T>
        {
            protected override Type TargetType { get; }

            public ElementSerializer(Type type)
            {
                TargetType = type;
            }
        }
    }
}
