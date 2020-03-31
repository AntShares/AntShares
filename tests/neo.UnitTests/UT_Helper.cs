using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Network.P2P;
using Neo.SmartContract;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;

namespace Neo.UnitTests
{
    [TestClass]
    public class UT_Helper
    {
        [TestMethod]
        public void GetHashData()
        {
            TestVerifiable verifiable = new TestVerifiable();
            byte[] res = verifiable.GetHashData();
            res.Length.Should().Be(8);
            byte[] requiredData = new byte[] { 7, 116, 101, 115, 116, 83, 116, 114 };
            for (int i = 0; i < requiredData.Length; i++)
            {
                res[i].Should().Be(requiredData[i]);
            }
        }

        [TestMethod]
        public void Sign()
        {
            TestVerifiable verifiable = new TestVerifiable();
            byte[] res = verifiable.Sign(new KeyPair(TestUtils.GetByteArray(32, 0x42)));
            res.Length.Should().Be(64);
        }

        [TestMethod]
        public void ToScriptHash()
        {
            byte[] testByteArray = TestUtils.GetByteArray(64, 0x42);
            UInt160 res = testByteArray.ToScriptHash();
            res.Should().Be(UInt160.Parse("2d3b96ae1bcc5a585e075e3b81920210dec16302"));
        }

        [TestMethod]
        public void TestGetLowestSetBit()
        {
            var big1 = new BigInteger(0);
            big1.GetLowestSetBit().Should().Be(-1);

            var big2 = new BigInteger(512);
            big2.GetLowestSetBit().Should().Be(9);
        }

        [TestMethod]
        public void TestGetBitLength()
        {
            var b1 = new BigInteger(100);
            b1.GetBitLength().Should().Be(7);

            var b2 = new BigInteger(-100);
            b2.GetBitLength().Should().Be(7);
        }

        [TestMethod]
        public void TestHexToBytes()
        {
            string nullStr = null;
            nullStr.HexToBytes().ToHexString().Should().Be(new byte[0].ToHexString());
            string emptyStr = "";
            emptyStr.HexToBytes().ToHexString().Should().Be(new byte[0].ToHexString());
            string str1 = "hab";
            Action action = () => str1.HexToBytes();
            action.Should().Throw<FormatException>();
            string str2 = "0102";
            byte[] bytes = str2.HexToBytes();
            bytes.ToHexString().Should().Be(new byte[] { 0x01, 0x02 }.ToHexString());
        }

        [TestMethod]
        public void TestNextBigIntegerForRandom()
        {
            Random ran = new Random();
            Action action1 = () => ran.NextBigInteger(-1);
            action1.Should().Throw<ArgumentException>();

            ran.NextBigInteger(0).Should().Be(0);
            ran.NextBigInteger(8).Should().NotBeNull();
            ran.NextBigInteger(9).Should().NotBeNull();
        }

        [TestMethod]
        public void TestNextBigIntegerForRandomNumberGenerator()
        {
            var ran = RandomNumberGenerator.Create();
            Action action1 = () => ran.NextBigInteger(-1);
            action1.Should().Throw<ArgumentException>();

            ran.NextBigInteger(0).Should().Be(0);
            ran.NextBigInteger(8).Should().NotBeNull();
            ran.NextBigInteger(9).Should().NotBeNull();
        }

        [TestMethod]
        public void TestUnmapForIPAddress()
        {
            var addr = new IPAddress(new byte[] { 127, 0, 0, 1 });
            addr.Unmap().Should().Be(addr);

            var addr2 = addr.MapToIPv6();
            addr2.Unmap().Should().Be(addr);
        }

        [TestMethod]
        public void TestUnmapForIPEndPoin()
        {
            var addr = new IPAddress(new byte[] { 127, 0, 0, 1 });
            var endPoint = new IPEndPoint(addr, 8888);
            endPoint.Unmap().Should().Be(endPoint);

            var addr2 = addr.MapToIPv6();
            var endPoint2 = new IPEndPoint(addr2, 8888);
            endPoint2.Unmap().Should().Be(endPoint);
        }

        [TestMethod]
        public void TestWeightedAverage()
        {
            var foo1 = new Foo
            {
                Value = 1,
                Weight = 2
            };
            var foo2 = new Foo
            {
                Value = 2,
                Weight = 3
            };
            var list = new List<Foo>
            {
                foo1,foo2
            };
            list.WeightedAverage(p => p.Value, p => p.Weight).Should().Be(new BigInteger(1));

            var foo3 = new Foo
            {
                Value = 1,
                Weight = 0
            };
            var foo4 = new Foo
            {
                Value = 2,
                Weight = 0
            };
            var list2 = new List<Foo>
            {
                foo3, foo4
            };
            list2.WeightedAverage(p => p.Value, p => p.Weight).Should().Be(BigInteger.Zero);
        }

        [TestMethod]
        public void WeightFilter()
        {
            var w1 = new Woo
            {
                Value = 1
            };
            var w2 = new Woo
            {
                Value = 2
            };
            var list = new List<Woo>
            {
                w1, w2
            };
            var ret = list.WeightedFilter(0.3, 0.6, p => p.Value, (p, w) => new Result
            {
                Info = p,
                Weight = w
            });
            var sum = BigInteger.Zero;
            foreach (Result res in ret)
            {
                sum = BigInteger.Add(res.Weight, sum);
            }
            sum.Should().Be(BigInteger.Zero);

            var w3 = new Woo
            {
                Value = 3
            };

            var list2 = new List<Woo>
            {
                w1, w2, w3
            };
            var ret2 = list2.WeightedFilter(0.3, 0.4, p => p.Value, (p, w) => new Result
            {
                Info = p,
                Weight = w
            });
            sum = BigInteger.Zero;
            foreach (Result res in ret2)
            {
                sum = BigInteger.Add(res.Weight, sum);
            }
            sum.Should().Be(BigInteger.Zero);

            CheckArgumentOutOfRangeException(-1, 0.4, p => p.Value, list2);

            CheckArgumentOutOfRangeException(0.2, 1.4, p => p.Value, list2);

            CheckArgumentOutOfRangeException(0.8, 0.3, p => p.Value, list2);

            CheckArgumentOutOfRangeException(0.3, 0.8, p => p.Value, list2);

            CheckArgumentNullException(0.3, 0.6, null, list2);

            CheckArgumentNullException(0.3, 0.4, p => p.Value, null);

            list2.WeightedFilter(0.3, 0.3, p => p.Value, (p, w) => new Result
            {
                Info = p,
                Weight = w
            }).WeightedAverage(p => p.Weight, p => p.Weight).Should().Be(0);


            var list3 = new List<Woo>();
            list3.WeightedFilter(0.3, 0.6, p => p.Value, (p, w) => new Result
            {
                Info = p,
                Weight = w
            }).WeightedAverage(p => p.Weight, p => p.Weight).Should().Be(0);

        }

        private static void CheckArgumentOutOfRangeException(double start, double end, Func<Woo, BigInteger> func, List<Woo> list)
        {
            Action action = () => list.WeightedFilter(start, end, func, (p, w) => new Result
            {
                Info = p,
                Weight = w
            }).WeightedAverage(p => p.Weight, p => p.Weight);
            action.Should().Throw<ArgumentOutOfRangeException>();
        }

        private static void CheckArgumentNullException(double start, double end, Func<Woo, BigInteger> func, List<Woo> list)
        {
            Action action = () => list.WeightedFilter(start, end, func, (p, w) => new Result
            {
                Info = p,
                Weight = w
            }).WeightedAverage(p => p.Weight, p => p.Weight);
            action.Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void TestConcat()
        {
            var a = new byte[] { 0x01 };
            var b = new byte[] { 0x02 };
            a = a.Concat(b);
            Assert.AreEqual(2, a.Length);
        }

        [TestMethod]
        public void TestAdd()
        {
            var a = "ab".HexToBytes();
            byte b = 0x0c;
            a = a.Add(b);
            Assert.AreEqual("ab0c", a.ToHexString());

            a = new byte[0];
            a = a.Add(b);
            Assert.AreEqual(1, a.Length);
            Assert.AreEqual("0c", a.ToHexString());
        }

        [TestMethod]
        public void TestSkip()
        {
            var s = "abcd01".HexToBytes();
            s = s.Skip(2);
            Assert.AreEqual("01", s.ToHexString());

            s = new byte[] { 0x01 };
            s = s.Skip(1);
            Assert.AreEqual(0, s.Length);
            s = s.Skip(2);
            Assert.AreEqual(0, s.Length);
        }

        [TestMethod]
        public void TestCommonPrefix()
        {
            var a = "1234abcd".HexToBytes();
            var b = "".HexToBytes();
            var prefix = a.CommonPrefix(b);
            Assert.IsTrue(prefix.Length == 0);

            b = "100000".HexToBytes();
            prefix = a.CommonPrefix(b);
            Assert.IsTrue(prefix.Length == 0);

            b = "1234".HexToBytes();
            prefix = a.CommonPrefix(b);
            Assert.AreEqual("1234", prefix.ToHexString());

            b = a;
            prefix = a.CommonPrefix(b);
            Assert.AreEqual("1234abcd", prefix.ToHexString());

            a = new byte[0];
            b = new byte[0];
            prefix = a.CommonPrefix(b);
            Assert.IsTrue(prefix.Length == 0);
        }

        [TestMethod]
        public void TestEqual()
        {
            var a = new byte[] { 1, 2, 3, 4, 5 };
            var b = new byte[] { 1, 2, 3, 4, 6 };
            var c = new byte[0];
            var d = new byte[] { 1, 2, 3, 4, 5 };
            var e = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
            var f = new byte[] { 1 };

            Assert.IsFalse(a.Equal(b));
            Assert.IsFalse(a.Equal(c));
            Assert.IsTrue(a.Equal(d));
            Assert.IsFalse(a.Equal(e));
            Assert.IsFalse(a.Equal(f));
        }

        [TestMethod]
        public void TestToNibbles()
        {
            var a = "1234abcd".HexToBytes();
            var n = a.ToNibbles();
            Assert.AreEqual("010203040a0b0c0d", n.ToHexString());
        }
    }

    class Foo
    {
        public int Weight { set; get; }
        public int Value { set; get; }
    }

    class Woo
    {
        public int Value { set; get; }
    }

    class Result
    {
        public Woo Info { set; get; }
        public BigInteger Weight { set; get; }
    }
}
