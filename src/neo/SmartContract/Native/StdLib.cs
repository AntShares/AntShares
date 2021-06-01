#pragma warning disable IDE0051

using Neo.Cryptography;
using Neo.IO;
using Neo.IO.Json;
using Neo.VM.Types;
using Neo.Wallets;
using System;
using System.Globalization;
using System.Numerics;

namespace Neo.SmartContract.Native
{
    /// <summary>
    /// A native contract library that provides useful functions.
    /// </summary>
    public sealed class StdLib : NativeContract
    {
        private const int MaxInputLength = 1024;

        internal StdLib() { }

        [ContractMethod(CpuFee = 1 << 12)]
        private static byte[] Serialize(ApplicationEngine engine, StackItem item)
        {
            return BinarySerializer.Serialize(item, engine.Limits.MaxItemSize);
        }

        [ContractMethod(CpuFee = 1 << 14)]
        private static StackItem Deserialize(ApplicationEngine engine, byte[] data)
        {
            return BinarySerializer.Deserialize(data, engine.Limits.MaxStackSize, engine.ReferenceCounter);
        }

        [ContractMethod(CpuFee = 1 << 12)]
        private static byte[] JsonSerialize(ApplicationEngine engine, StackItem item)
        {
            return JsonSerializer.SerializeToByteArray(item, engine.Limits.MaxItemSize);
        }

        [ContractMethod(CpuFee = 1 << 14)]
        private static StackItem JsonDeserialize(ApplicationEngine engine, byte[] json)
        {
            return JsonSerializer.Deserialize(JObject.Parse(json, 10), engine.ReferenceCounter);
        }

        /// <summary>
        /// Converts an integer to a <see cref="string"/>.
        /// </summary>
        /// <param name="value">The integer to convert.</param>
        /// <returns>The converted <see cref="string"/>.</returns>
        [ContractMethod(CpuFee = 1 << 12)]
        public static string Itoa(BigInteger value)
        {
            return Itoa(value, 10);
        }

        /// <summary>
        /// Converts an integer to a <see cref="string"/>.
        /// </summary>
        /// <param name="value">The integer to convert.</param>
        /// <param name="base">The base of the integer. Only support 10 and 16.</param>
        /// <returns>The converted <see cref="string"/>.</returns>
        [ContractMethod(CpuFee = 1 << 12)]
        public static string Itoa(BigInteger value, int @base)
        {
            return @base switch
            {
                10 => value.ToString(),
                16 => value.ToString("x"),
                _ => throw new ArgumentOutOfRangeException(nameof(@base))
            };
        }

        /// <summary>
        /// Converts a <see cref="string"/> to an integer.
        /// </summary>
        /// <param name="value">The <see cref="string"/> to convert.</param>
        /// <returns>The converted integer.</returns>
        [ContractMethod(CpuFee = 1 << 6)]
        public static BigInteger Atoi(string value)
        {
            return Atoi(value, 10);
        }

        /// <summary>
        /// Converts a <see cref="string"/> to an integer.
        /// </summary>
        /// <param name="value">The <see cref="string"/> to convert.</param>
        /// <param name="base">The base of the integer. Only support 10 and 16.</param>
        /// <returns>The converted integer.</returns>
        [ContractMethod(CpuFee = 1 << 6)]
        public static BigInteger Atoi([MaxLength(MaxInputLength)] string value, int @base)
        {
            return @base switch
            {
                10 => BigInteger.Parse(value),
                16 => BigInteger.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
                _ => throw new ArgumentOutOfRangeException(nameof(@base))
            };
        }

        /// <summary>
        /// Encodes a byte array into a base64 <see cref="string"/>.
        /// </summary>
        /// <param name="data">The byte array to be encoded.</param>
        /// <returns>The encoded <see cref="string"/>.</returns>
        [ContractMethod(CpuFee = 1 << 5)]
        public static string Base64Encode([MaxLength(MaxInputLength)] byte[] data)
        {
            return Convert.ToBase64String(data);
        }

        /// <summary>
        /// Decodes a byte array from a base64 <see cref="string"/>.
        /// </summary>
        /// <param name="s">The base64 <see cref="string"/>.</param>
        /// <returns>The decoded byte array.</returns>
        [ContractMethod(CpuFee = 1 << 5)]
        public static byte[] Base64Decode([MaxLength(MaxInputLength)] string s)
        {
            return Convert.FromBase64String(s);
        }

        /// <summary>
        /// Decodes an address into byte array.
        /// </summary>
        /// <param name="engine">Execution engine</param>
        /// <param name="address">The address <see cref="string"/>.</param>
        /// <returns>The decoded byte array.</returns>
        [ContractMethod(CpuFee = 1 << 5)]
        public static byte[] FromAddress(ApplicationEngine engine, [MaxLength(MaxInputLength)] string address)
        {
            return address.ToScriptHash(engine.ProtocolSettings.AddressVersion).ToArray();
        }

        /// <summary>
        /// Decodes a byte array into an address.
        /// </summary>
        /// <param name="engine">Execution engine</param>
        /// <param name="data">Data</param>
        /// <returns>The decoded byte array.</returns>
        [ContractMethod(CpuFee = 1 << 5)]
        public static string ToAddress(ApplicationEngine engine, [MaxLength(UInt160.Length)] byte[] data)
        {
            return new UInt160(data).ToAddress(engine.ProtocolSettings.AddressVersion);
        }

        /// <summary>
        /// Encodes a byte array into a base58 <see cref="string"/>.
        /// </summary>
        /// <param name="data">The byte array to be encoded.</param>
        /// <returns>The encoded <see cref="string"/>.</returns>
        [ContractMethod(CpuFee = 1 << 13)]
        public static string Base58Encode([MaxLength(MaxInputLength)] byte[] data)
        {
            return Base58.Encode(data);
        }

        /// <summary>
        /// Decodes a byte array from a base58 <see cref="string"/>.
        /// </summary>
        /// <param name="s">The base58 <see cref="string"/>.</param>
        /// <returns>The decoded byte array.</returns>
        [ContractMethod(CpuFee = 1 << 10)]
        public static byte[] Base58Decode([MaxLength(MaxInputLength)] string s)
        {
            return Base58.Decode(s);
        }

        /// <summary>
        /// Converts a byte array to its equivalent <see cref="string"/> representation that is encoded with base-58 digits. The encoded <see cref="string"/> contains the checksum of the binary data.
        /// </summary>
        /// <param name="data">The byte array to be encoded.</param>
        /// <returns>The encoded <see cref="string"/>.</returns>
        [ContractMethod(CpuFee = 1 << 16)]
        public static string Base58CheckEncode([MaxLength(MaxInputLength)] byte[] data)
        {
            return Base58.Base58CheckEncode(data);
        }

        /// <summary>
        /// Converts the specified <see cref="string"/>, which encodes binary data as base-58 digits, to an equivalent byte array. The encoded <see cref="string"/> contains the checksum of the binary data.
        /// </summary>
        /// <param name="s">The base58 <see cref="string"/>.</param>
        /// <returns>The decoded byte array.</returns>
        [ContractMethod(CpuFee = 1 << 16)]
        public static byte[] Base58CheckDecode([MaxLength(MaxInputLength)] string s)
        {
            return Base58.Base58CheckDecode(s);
        }

        [ContractMethod(CpuFee = 1 << 5)]
        private static int MemoryCompare([MaxLength(MaxInputLength)] byte[] str1, [MaxLength(MaxInputLength)] byte[] str2)
        {
            return Math.Sign(str1.AsSpan().SequenceCompareTo(str2));
        }

        [ContractMethod(CpuFee = 1 << 6)]
        private static int MemorySearch([MaxLength(MaxInputLength)] byte[] mem, byte[] value)
        {
            return MemorySearch(mem, value, 0, false);
        }

        [ContractMethod(CpuFee = 1 << 6)]
        private static int MemorySearch([MaxLength(MaxInputLength)] byte[] mem, byte[] value, int start)
        {
            return MemorySearch(mem, value, start, false);
        }

        [ContractMethod(CpuFee = 1 << 6)]
        private static int MemorySearch([MaxLength(MaxInputLength)] byte[] mem, byte[] value, int start, bool backward)
        {
            if (backward)
            {
                return mem.AsSpan(0, start).LastIndexOf(value);
            }
            else
            {
                int index = mem.AsSpan(start).IndexOf(value);
                if (index < 0) return -1;
                return index + start;
            }
        }

        [ContractMethod(CpuFee = 1 << 8)]
        private static string[] StringSplit([MaxLength(MaxInputLength)] string str, string separator)
        {
            return str.Split(separator);
        }

        [ContractMethod(CpuFee = 1 << 8)]
        private static string[] StringSplit([MaxLength(MaxInputLength)] string str, string separator, bool removeEmptyEntries)
        {
            StringSplitOptions options = removeEmptyEntries ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None;
            return str.Split(separator, options);
        }
    }
}
