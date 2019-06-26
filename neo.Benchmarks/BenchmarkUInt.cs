using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;

using BenchmarkDotNet.Attributes;

namespace Neo.Benchmarks
{
    [TestClass]
    public class BenchmarkUInt : BenchmarkBase
    {
        private const int MAX_TESTS = 1000;

        byte[][] base_32_1;
        byte[][] base_32_2;
        byte[][] base_20_1;
        byte[][] base_20_2;

        private Random random;

        [TestInitialize]
        public void TestSetup()
        {
            // this is a Test class and also Benchmark class
            // it is supposed to run and verify unit tests, but also provide benchmarking
            Setup();
        }

        [GlobalSetup]
        public override void Setup()
        {
            int SEED = 123456789;
            random = new Random(SEED);

            base_32_1 = new byte[MAX_TESTS][];
            base_32_2 = new byte[MAX_TESTS][];
            base_20_1 = new byte[MAX_TESTS][];
            base_20_2 = new byte[MAX_TESTS][];

            for (var i = 0; i < MAX_TESTS; i++)
            {
                base_32_1[i] = RandomBytes(32);
                base_20_1[i] = RandomBytes(20);
                if (i % 2 == 0)
                {
                    base_32_2[i] = RandomBytes(32);
                    base_20_2[i] = RandomBytes(20);
                }
                else
                {
                    base_32_2[i] = new byte[32];
                    Buffer.BlockCopy(base_32_1[i], 0, base_32_2[i], 0, 32);
                    base_20_2[i] = new byte[20];
                    Buffer.BlockCopy(base_20_1[i], 0, base_20_2[i], 0, 20);
                }
            }

            base.Setup();
        }

        private byte[] RandomBytes(int count)
        {
            byte[] randomBytes = new byte[count];
            random.NextBytes(randomBytes);
            return randomBytes;
        }

        public delegate object BenchmarkMethod();

        public (TimeSpan, object) LocalBenchmark(BenchmarkMethod method)
        {
            Stopwatch sw0 = new Stopwatch();
            sw0.Start();
            var result = method();
            sw0.Stop();
            TimeSpan elapsed = sw0.Elapsed;
            Console.WriteLine($"Elapsed={elapsed} Sum={result}");
            return (elapsed, result);
        }

        [Benchmark]
        public void Benchmark_CompareTo_UInt256()
        {
            // testing "official UInt256 version"
            UInt256[] uut_32_1 = new UInt256[MAX_TESTS];
            UInt256[] uut_32_2 = new UInt256[MAX_TESTS];

            for (var i = 0; i < MAX_TESTS; i++)
            {
                uut_32_1[i] = new UInt256(base_32_1[i]);
                uut_32_2[i] = new UInt256(base_32_2[i]);
            }

            var checksum0 = LocalBenchmark(() =>
            {
                var checksum = 0;
                for (var i = 0; i < MAX_TESTS; i++)
                {
                    checksum += uut_32_1[i].CompareTo(uut_32_2[i]);
                }

                return checksum;
            }).Item2;

            var checksum1 = LocalBenchmark(() =>
            {
                var checksum = 0;
                for (var i = 0; i < MAX_TESTS; i++)
                {
                    checksum += code1_UInt256CompareTo(base_32_1[i], base_32_2[i]);
                }

                return checksum;
            }).Item2;

            var checksum2 = LocalBenchmark(() =>
            {
                var checksum = 0;
                for (var i = 0; i < MAX_TESTS; i++)
                {
                    checksum += code2_UInt256CompareTo(base_32_1[i], base_32_2[i]);
                }

                return checksum;
            }).Item2;

            var checksum3 = LocalBenchmark(() =>
            {
                var checksum = 0;
                for (var i = 0; i < MAX_TESTS; i++)
                {
                    checksum += code3_UInt256CompareTo(base_32_1[i], base_32_2[i]);
                }

                return checksum;
            }).Item2;

            //checksum0.Should().Be(checksum1);
            //checksum0.Should().Be(checksum2);
            //checksum0.Should().Be(checksum3);
        }

        [Benchmark]
        public void Benchmark_CompareTo_UInt160()
        {
            // testing "official UInt160 version"
            UInt160[] uut_20_1 = new UInt160[MAX_TESTS];
            UInt160[] uut_20_2 = new UInt160[MAX_TESTS];

            for (var i = 0; i < MAX_TESTS; i++)
            {
                uut_20_1[i] = new UInt160(base_20_1[i]);
                uut_20_2[i] = new UInt160(base_20_2[i]);
            }

            var checksum0 = LocalBenchmark(() =>
            {
                var checksum = 0;
                for (var i = 0; i < MAX_TESTS; i++)
                {
                    checksum += uut_20_1[i].CompareTo(uut_20_2[i]);
                }

                return checksum;
            }).Item2;

            var checksum1 = LocalBenchmark(() =>
            {
                var checksum = 0;
                for (var i = 0; i < MAX_TESTS; i++)
                {
                    checksum += code1_UInt160CompareTo(base_20_1[i], base_20_2[i]);
                }

                return checksum;
            }).Item2;

            var checksum2 = LocalBenchmark(() =>
            {
                var checksum = 0;
                for (var i = 0; i < MAX_TESTS; i++)
                {
                    checksum += code2_UInt160CompareTo(base_20_1[i], base_20_2[i]);
                }

                return checksum;
            }).Item2;

            var checksum3 = LocalBenchmark(() =>
            {
                var checksum = 0;
                for (var i = 0; i < MAX_TESTS; i++)
                {
                    checksum += code3_UInt160CompareTo(base_20_1[i], base_20_2[i]);
                }

                return checksum;
            }).Item2;

            //checksum0.Should().Be(checksum1);
            //checksum0.Should().Be(checksum2);
            //checksum0.Should().Be(checksum3);
        }

        [TestMethod]
        public void Benchmark_UInt_IsCorrect_Self_CompareTo()
        {
            for (var i = 0; i < MAX_TESTS; i++)
            {
                code1_UInt160CompareTo(base_20_1[i], base_20_1[i]).Should().Be(0);
                code2_UInt160CompareTo(base_20_1[i], base_20_1[i]).Should().Be(0);
                code3_UInt160CompareTo(base_20_1[i], base_20_1[i]).Should().Be(0);
                code1_UInt256CompareTo(base_32_1[i], base_32_1[i]).Should().Be(0);
                code2_UInt256CompareTo(base_32_1[i], base_32_1[i]).Should().Be(0);
                code3_UInt256CompareTo(base_32_1[i], base_32_1[i]).Should().Be(0);
            }
        }

        private int code1_UInt256CompareTo(byte[] b1, byte[] b2)
        {
            byte[] x = b1;
            byte[] y = b2;
            for (int i = x.Length - 1; i >= 0; i--)
            {
                if (x[i] > y[i])
                    return 1;
                if (x[i] < y[i])
                    return -1;
            }
            return 0;
        }

        private unsafe int code2_UInt256CompareTo(byte[] b1, byte[] b2)
        {
            fixed (byte* px = b1, py = b2)
            {
                uint* lpx = (uint*)px;
                uint* lpy = (uint*)py;
                for (int i = 256 / 32 - 1; i >= 0; i--)
                {
                    if (lpx[i] > lpy[i])
                        return 1;
                    if (lpx[i] < lpy[i])
                        return -1;
                }
            }
            return 0;
        }

        private unsafe int code3_UInt256CompareTo(byte[] b1, byte[] b2)
        {
            fixed (byte* px = b1, py = b2)
            {
                ulong* lpx = (ulong*)px;
                ulong* lpy = (ulong*)py;
                for (int i = 256 / 64 - 1; i >= 0; i--)
                {
                    if (lpx[i] > lpy[i])
                        return 1;
                    if (lpx[i] < lpy[i])
                        return -1;
                }
            }
            return 0;
        }
        private int code1_UInt160CompareTo(byte[] b1, byte[] b2)
        {
            byte[] x = b1;
            byte[] y = b2;
            for (int i = x.Length - 1; i >= 0; i--)
            {
                if (x[i] > y[i])
                    return 1;
                if (x[i] < y[i])
                    return -1;
            }
            return 0;
        }

        private unsafe int code2_UInt160CompareTo(byte[] b1, byte[] b2)
        {
            fixed (byte* px = b1, py = b2)
            {
                uint* lpx = (uint*)px;
                uint* lpy = (uint*)py;
                for (int i = 160 / 32 - 1; i >= 0; i--)
                {
                    if (lpx[i] > lpy[i])
                        return 1;
                    if (lpx[i] < lpy[i])
                        return -1;
                }
            }
            return 0;
        }

        private unsafe int code3_UInt160CompareTo(byte[] b1, byte[] b2)
        {
            // LSB -----------------> MSB
            // --------------------------
            // | 8B      | 8B      | 4B |
            // --------------------------
            //   0l        1l        4i
            // --------------------------
            fixed (byte* px = b1, py = b2)
            {
                uint* ipx = (uint*)px;
                uint* ipy = (uint*)py;
                if (ipx[4] > ipy[4])
                    return 1;
                if (ipx[4] < ipy[4])
                    return -1;

                ulong* lpx = (ulong*)px;
                ulong* lpy = (ulong*)py;
                if (lpx[1] > lpy[1])
                    return 1;
                if (lpx[1] < lpy[1])
                    return -1;
                if (lpx[0] > lpy[0])
                    return 1;
                if (lpx[0] < lpy[0])
                    return -1;
            }
            return 0;
        }

    }
}
