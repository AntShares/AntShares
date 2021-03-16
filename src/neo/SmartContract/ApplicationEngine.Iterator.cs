using Neo.SmartContract.Iterators;
using Neo.VM.Types;
using System;
using Array = Neo.VM.Types.Array;

namespace Neo.SmartContract
{
    partial class ApplicationEngine
    {
        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Iterator.Create.
        /// Creates an <see cref="IIterator"/> with the specified <see cref="StackItem"/>.
        /// </summary>
        public static readonly InteropDescriptor System_Iterator_Create = Register("System.Iterator.Create", nameof(CreateIterator), 1 << 4, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Iterator.Next.
        /// Advances the iterator to the next element of the collection.
        /// </summary>
        public static readonly InteropDescriptor System_Iterator_Next = Register("System.Iterator.Next", nameof(IteratorNext), 1 << 15, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of System.Iterator.Value.
        /// Gets the element in the collection at the current position of the iterator.
        /// </summary>
        public static readonly InteropDescriptor System_Iterator_Value = Register("System.Iterator.Value", nameof(IteratorValue), 1 << 4, CallFlags.None);

        /// <summary>
        /// The implementation of System.Iterator.Create.
        /// Creates an <see cref="IIterator"/> with the specified <see cref="StackItem"/>.
        /// </summary>
        /// <param name="item">The <see cref="StackItem"/> wrapped by the iterator.</param>
        /// <returns>The created iterator.</returns>
        protected internal IIterator CreateIterator(StackItem item)
        {
            return item switch
            {
                Array array => new ArrayWrapper(array),
                Map map => new MapWrapper(map, ReferenceCounter),
                VM.Types.Buffer buffer => new ByteArrayWrapper(buffer),
                PrimitiveType primitive => new ByteArrayWrapper(primitive),
                _ => throw new ArgumentException(null, nameof(item))
            };
        }

        /// <summary>
        /// The implementation of System.Iterator.Next.
        /// Advances the iterator to the next element of the collection.
        /// </summary>
        /// <param name="iterator">The iterator to be advanced.</param>
        /// <returns><see langword="true"/> if the iterator was successfully advanced to the next element; <see langword="false"/> if the iterator has passed the end of the collection.</returns>
        internal protected static bool IteratorNext(IIterator iterator)
        {
            return iterator.Next();
        }

        /// <summary>
        /// The implementation of System.Iterator.Value.
        /// Gets the element in the collection at the current position of the iterator.
        /// </summary>
        /// <param name="iterator">The iterator to be used.</param>
        /// <returns>The element in the collection at the current position of the iterator.</returns>
        internal protected static StackItem IteratorValue(IIterator iterator)
        {
            return iterator.Value();
        }
    }
}
