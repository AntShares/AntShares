using Neo.IO.Caching;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Neo
{
    /// <summary>
    /// A helper class that provides common functions.
    /// </summary>
    public static class Helper
    {
        private static readonly DateTime unixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BitLen(int w)
        {
            return (w < 1 << 15 ? (w < 1 << 7
                ? (w < 1 << 3 ? (w < 1 << 1
                ? (w < 1 << 0 ? (w < 0 ? 32 : 0) : 1)
                : (w < 1 << 2 ? 2 : 3)) : (w < 1 << 5
                ? (w < 1 << 4 ? 4 : 5)
                : (w < 1 << 6 ? 6 : 7)))
                : (w < 1 << 11
                ? (w < 1 << 9 ? (w < 1 << 8 ? 8 : 9) : (w < 1 << 10 ? 10 : 11))
                : (w < 1 << 13 ? (w < 1 << 12 ? 12 : 13) : (w < 1 << 14 ? 14 : 15)))) : (w < 1 << 23 ? (w < 1 << 19
                ? (w < 1 << 17 ? (w < 1 << 16 ? 16 : 17) : (w < 1 << 18 ? 18 : 19))
                : (w < 1 << 21 ? (w < 1 << 20 ? 20 : 21) : (w < 1 << 22 ? 22 : 23))) : (w < 1 << 27
                ? (w < 1 << 25 ? (w < 1 << 24 ? 24 : 25) : (w < 1 << 26 ? 26 : 27))
                : (w < 1 << 29 ? (w < 1 << 28 ? 28 : 29) : (w < 1 << 30 ? 30 : 31)))));
        }

        /// <summary>
        /// Concatenates the specified byte arrays.
        /// </summary>
        /// <param name="buffers">The byte arrays to concatenate.</param>
        /// <returns>The concatenated byte array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] Concat(params byte[][] buffers)
        {
            int length = 0;
            for (int i = 0; i < buffers.Length; i++)
                length += buffers[i].Length;
            byte[] dst = new byte[length];
            int p = 0;
            foreach (byte[] src in buffers)
            {
                Buffer.BlockCopy(src, 0, dst, p, src.Length);
                p += src.Length;
            }
            return dst;
        }

        /// <summary>
        /// Concatenates two byte arrays.
        /// </summary>
        /// <param name="a">The first byte array to concatenate.</param>
        /// <param name="b">The second byte array to concatenate.</param>
        /// <returns>The concatenated byte array.</returns>
        public static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            byte[] buffer = new byte[a.Length + b.Length];
            a.CopyTo(buffer);
            b.CopyTo(buffer.AsSpan(a.Length));
            return buffer;
        }

        internal static int GetLowestSetBit(this BigInteger i)
        {
            if (i.Sign == 0)
                return -1;
            byte[] b = i.ToByteArray();
            int w = 0;
            while (b[w] == 0)
                w++;
            for (int x = 0; x < 8; x++)
                if ((b[w] & 1 << x) > 0)
                    return x + w * 8;
            throw new Exception();
        }

        internal static void Remove<T>(this HashSet<T> set, ISet<T> other)
        {
            if (set.Count > other.Count)
            {
                set.ExceptWith(other);
            }
            else
            {
                set.RemoveWhere(u => other.Contains(u));
            }
        }

        internal static void Remove<T>(this HashSet<T> set, HashSetCache<T> other)
            where T : IEquatable<T>
        {
            if (set.Count > other.Count)
            {
                set.ExceptWith(other);
            }
            else
            {
                set.RemoveWhere(u => other.Contains(u));
            }
        }

        internal static void Remove<T, V>(this HashSet<T> set, IReadOnlyDictionary<T, V> other)
        {
            if (set.Count > other.Count)
            {
                set.ExceptWith(other.Keys);
            }
            else
            {
                set.RemoveWhere(u => other.ContainsKey(u));
            }
        }

        internal static string GetVersion(this Assembly assembly)
        {
            CustomAttributeData attribute = assembly.CustomAttributes.FirstOrDefault(p => p.AttributeType == typeof(AssemblyInformationalVersionAttribute));
            if (attribute == null) return assembly.GetName().Version.ToString(3);
            return (string)attribute.ConstructorArguments[0].Value;
        }

        /// <summary>
        /// Converts a hex <see cref="string"/> to byte array.
        /// </summary>
        /// <param name="value">The hex <see cref="string"/> to convert.</param>
        /// <returns>The converted byte array.</returns>
        public static byte[] HexToBytes(this string value)
        {
            if (value == null || value.Length == 0)
                return Array.Empty<byte>();
            if (value.Length % 2 == 1)
                throw new FormatException();
            byte[] result = new byte[value.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = byte.Parse(value.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static BigInteger Mod(this BigInteger x, BigInteger y)
        {
            x %= y;
            if (x.Sign < 0)
                x += y;
            return x;
        }

        internal static BigInteger ModInverse(this BigInteger a, BigInteger n)
        {
            BigInteger i = n, v = 0, d = 1;
            while (a > 0)
            {
                BigInteger t = i / a, x = a;
                a = i % x;
                i = x;
                x = d;
                d = v - t * x;
                v = x;
            }
            v %= n;
            if (v.Sign < 0) v = (v + n) % n;
            return v;
        }

        internal static BigInteger NextBigInteger(this Random rand, int sizeInBits)
        {
            if (sizeInBits < 0)
                throw new ArgumentException("sizeInBits must be non-negative");
            if (sizeInBits == 0)
                return 0;
            Span<byte> b = stackalloc byte[sizeInBits / 8 + 1];
            rand.NextBytes(b);
            if (sizeInBits % 8 == 0)
                b[^1] = 0;
            else
                b[^1] &= (byte)((1 << sizeInBits % 8) - 1);
            return new BigInteger(b);
        }

        /// <summary>
        /// Finds the sum of the specified integers.
        /// </summary>
        /// <param name="source">The specified integers.</param>
        /// <returns>The sum of the integers.</returns>
        public static BigInteger Sum(this IEnumerable<BigInteger> source)
        {
            var sum = BigInteger.Zero;
            foreach (var bi in source) sum += bi;
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TestBit(this BigInteger i, int index)
        {
            return (i & (BigInteger.One << index)) > BigInteger.Zero;
        }

        /// <summary>
        /// Converts a <see cref="BigInteger"/> to byte array and eliminates all the leading zeros.
        /// </summary>
        /// <param name="i">The <see cref="BigInteger"/> to convert.</param>
        /// <returns>The converted byte array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] ToByteArrayStandard(this BigInteger i)
        {
            if (i.IsZero) return Array.Empty<byte>();
            return i.ToByteArray();
        }

        /// <summary>
        /// Converts a byte array to hex <see cref="string"/>.
        /// </summary>
        /// <param name="value">The byte array to convert.</param>
        /// <returns>The converted hex <see cref="string"/>.</returns>
        public static string ToHexString(this byte[] value)
        {
            StringBuilder sb = new();
            foreach (byte b in value)
                sb.AppendFormat("{0:x2}", b);
            return sb.ToString();
        }

        /// <summary>
        /// Converts a byte array to hex <see cref="string"/>.
        /// </summary>
        /// <param name="value">The byte array to convert.</param>
        /// <param name="reverse">Indicates whether it should be converted in the reversed byte order.</param>
        /// <returns>The converted hex <see cref="string"/>.</returns>
        public static string ToHexString(this byte[] value, bool reverse = false)
        {
            StringBuilder sb = new();
            for (int i = 0; i < value.Length; i++)
                sb.AppendFormat("{0:x2}", value[reverse ? value.Length - i - 1 : i]);
            return sb.ToString();
        }

        /// <summary>
        /// Converts a byte array to hex <see cref="string"/>.
        /// </summary>
        /// <param name="value">The byte array to convert.</param>
        /// <returns>The converted hex <see cref="string"/>.</returns>
        public static string ToHexString(this ReadOnlySpan<byte> value)
        {
            StringBuilder sb = new();
            foreach (byte b in value)
                sb.AppendFormat("{0:x2}", b);
            return sb.ToString();
        }

        /// <summary>
        /// Converts a <see cref="DateTime"/> to timestamp.
        /// </summary>
        /// <param name="time">The <see cref="DateTime"/> to convert.</param>
        /// <returns>The converted timestamp.</returns>
        public static uint ToTimestamp(this DateTime time)
        {
            return (uint)(time.ToUniversalTime() - unixEpoch).TotalSeconds;
        }

        /// <summary>
        /// Converts a <see cref="DateTime"/> to timestamp in milliseconds.
        /// </summary>
        /// <param name="time">The <see cref="DateTime"/> to convert.</param>
        /// <returns>The converted timestamp.</returns>
        public static ulong ToTimestampMS(this DateTime time)
        {
            return (ulong)(time.ToUniversalTime() - unixEpoch).TotalMilliseconds;
        }

        /// <summary>
        /// Checks if address is IPv4 Maped to IPv6 format, if so, Map to IPv4.
        /// Otherwise, return current address.
        /// </summary>
        internal static IPAddress Unmap(this IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            return address;
        }

        /// <summary>
        /// Checks if IPEndPoint is IPv4 Maped to IPv6 format, if so, unmap to IPv4.
        /// Otherwise, return current endpoint.
        /// </summary>
        internal static IPEndPoint Unmap(this IPEndPoint endPoint)
        {
            if (!endPoint.Address.IsIPv4MappedToIPv6)
                return endPoint;
            return new IPEndPoint(endPoint.Address.Unmap(), endPoint.Port);
        }
    }
}
