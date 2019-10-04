using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO.Caching;
using System;
using System.Collections;
using System.Linq;

namespace Neo.UnitTests.IO.Caching
{
    [TestClass]
    public class UT_FIFOSet
    {
        [TestMethod]
        public void FIFOSetTest()
        {
            var a = UInt256.Zero;
            var b = new UInt256();
            var c = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01
            });

            var set = new FIFOSet<UInt256>(3);

            Assert.IsTrue(set.Add(a));
            Assert.IsFalse(set.Add(a));
            Assert.IsFalse(set.Add(b));
            Assert.IsTrue(set.Add(c));

            CollectionAssert.AreEqual(set.ToArray(), new UInt256[] { a, c });

            var d = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x02
            });

            // Testing Fifo max size
            Assert.IsTrue(set.Add(d));
            CollectionAssert.AreEqual(set.ToArray(), new UInt256[] { a, c, d });

            var e = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x03
            });

            Assert.IsTrue(set.Add(e));
            Assert.IsFalse(set.Add(e));
            CollectionAssert.AreEqual(set.ToArray(), new UInt256[] { c, d, e });
        }

        [TestMethod]
        public void TestConstructor()
        {
            Action action1 = () => new FIFOSet<UInt256>(-1);
            action1.Should().Throw<ArgumentOutOfRangeException>();

            Action action2 = () => new FIFOSet<UInt256>(1, -1);
            action2.Should().Throw<ArgumentOutOfRangeException>();

            Action action3 = () => new FIFOSet<UInt256>(1, 2);
            action3.Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void TestAdd()
        {
            var a = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01
            });
            var b = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x02
            });
            var set = new FIFOSet<UInt256>(1, 1)
            {
                a,
                b
            };
            CollectionAssert.AreEqual(set.ToArray(), new UInt256[] { b });
        }

        [TestMethod]
        public void TestGetEnumerator()
        {
            var a = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01
            });
            var b = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x02
            });
            var set = new FIFOSet<UInt256>(1, 1)
            {
                a,
                b
            };
            IEnumerable ie = set;
            ie.GetEnumerator().Should().NotBeNull();
        }

        [TestMethod]
        public void TestExceptWith()
        {
            var a = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01
            });
            var b = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x02
            });
            var c = new UInt256(new byte[32] {
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x03
            });

            var set = new FIFOSet<UInt256>(10)
            {
                a,
                b,
                c
            };
            set.ExceptWith(new UInt256[] { b, c });
            CollectionAssert.AreEqual(set.ToArray(), new UInt256[] { a });
        }
    }
}
