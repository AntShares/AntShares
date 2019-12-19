using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.SmartContract.Enumerators;
using Neo.SmartContract.Iterators;
using Neo.VM.Types;
using System;
using System.Collections.Generic;

namespace Neo.UnitTests.SmartContract.Enumerators
{
    [TestClass]
    public class UT_IteratorKeysWrapper
    {
        [TestMethod]
        public void TestGeneratorAndDispose()
        {
            IteratorKeysWrapper iteratorKeysWrapper = new IteratorKeysWrapper(new ArrayWrapper(new List<StackItem>()));
            Assert.IsNotNull(iteratorKeysWrapper);
            Action action = () => iteratorKeysWrapper.Dispose();
            action.Should().NotThrow<Exception>();
        }

        [TestMethod]
        public void TestNextAndValue()
        {
            List<StackItem> list = new List<StackItem> { StackItem.True };
            ArrayWrapper wrapper = new ArrayWrapper(list);
            IteratorKeysWrapper iteratorKeysWrapper = new IteratorKeysWrapper(wrapper);
            Action action = () => iteratorKeysWrapper.Next();
            action.Should().NotThrow<Exception>();
            Assert.AreEqual(new VM.Types.Integer(0), iteratorKeysWrapper.Value());
        }
    }
}
