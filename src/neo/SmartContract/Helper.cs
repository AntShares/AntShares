using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract.Native;
using Neo.VM;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Neo.SmartContract
{
    public static class Helper
    {
        private const long MaxVerificationGas = 0_50000000;

        public static UInt160 GetContractHash(UInt160 sender, byte[] script)
        {
            using var sb = new ScriptBuilder();
            sb.Emit(OpCode.ABORT);
            sb.EmitPush(sender);
            sb.EmitPush(script);

            return sb.ToArray().ToScriptHash();
        }

        public static UInt160 GetScriptHash(this ExecutionContext context)
        {
            return context.GetState<ExecutionContextState>().ScriptHash;
        }

        public static bool IsMultiSigContract(this byte[] script)
        {
            return IsMultiSigContract(script, out _, out _, null);
        }

        public static bool IsMultiSigContract(this byte[] script, out int m, out int n)
        {
            return IsMultiSigContract(script, out m, out n, null);
        }

        public static bool IsMultiSigContract(this byte[] script, out int m, out ECPoint[] points)
        {
            List<ECPoint> list = new List<ECPoint>();
            if (IsMultiSigContract(script, out m, out _, list))
            {
                points = list.ToArray();
                return true;
            }
            else
            {
                points = null;
                return false;
            }
        }

        private static bool IsMultiSigContract(byte[] script, out int m, out int n, List<ECPoint> points)
        {
            m = 0; n = 0;
            int i = 0;
            if (script.Length < 43) return false;
            switch (script[i])
            {
                case (byte)OpCode.PUSHINT8:
                    m = script[++i];
                    ++i;
                    break;
                case (byte)OpCode.PUSHINT16:
                    m = BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(++i));
                    i += 2;
                    break;
                case byte b when b >= (byte)OpCode.PUSH1 && b <= (byte)OpCode.PUSH16:
                    m = b - (byte)OpCode.PUSH0;
                    ++i;
                    break;
                default:
                    return false;
            }
            if (m < 1 || m > 1024) return false;
            while (script[i] == (byte)OpCode.PUSHDATA1)
            {
                if (script.Length <= i + 35) return false;
                if (script[++i] != 33) return false;
                points?.Add(ECPoint.DecodePoint(script.AsSpan(i + 1, 33), ECCurve.Secp256r1));
                i += 34;
                ++n;
            }
            if (n < m || n > 1024) return false;
            switch (script[i])
            {
                case (byte)OpCode.PUSHINT8:
                    if (script.Length <= i + 1 || n != script[++i]) return false;
                    ++i;
                    break;
                case (byte)OpCode.PUSHINT16:
                    if (script.Length < i + 3 || n != BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(++i))) return false;
                    i += 2;
                    break;
                case byte b when b >= (byte)OpCode.PUSH1 && b <= (byte)OpCode.PUSH16:
                    if (n != b - (byte)OpCode.PUSH0) return false;
                    ++i;
                    break;
                default:
                    return false;
            }
            if (script.Length != i + 6) return false;
            if (script[i++] != (byte)OpCode.PUSHNULL) return false;
            if (script[i++] != (byte)OpCode.SYSCALL) return false;
            if (BitConverter.ToUInt32(script, i) != ApplicationEngine.Neo_Crypto_CheckMultisigWithECDsaSecp256r1)
                return false;
            return true;
        }

        public static bool IsSignatureContract(this byte[] script)
        {
            if (script.Length != 41) return false;
            if (script[0] != (byte)OpCode.PUSHDATA1
                || script[1] != 33
                || script[35] != (byte)OpCode.PUSHNULL
                || script[36] != (byte)OpCode.SYSCALL
                || BitConverter.ToUInt32(script, 37) != ApplicationEngine.Neo_Crypto_VerifyWithECDsaSecp256r1)
                return false;
            return true;
        }

        private static readonly byte[] balanceOf = Encoding.UTF8.GetBytes("balanceOf");

        public static bool IsSolidTransfer(this byte[] script)
        {
            if (script.Length < 10) return false;
            int index;

            if (script[0] >= (byte)OpCode.PUSHM1 && script[0] <= (byte)OpCode.PUSH16)
            {
                if (script.Length != 65) return false;
                index = 1;
            }
            else if (script[0] == (byte)OpCode.PUSHINT8)
            {
                if (script.Length != 66) return false;
                index = 2;
            }
            else if (script[0] == (byte)OpCode.PUSHINT16)
            {
                if (script.Length != 67) return false;
                index = 3;
            }
            else if (script[0] == (byte)OpCode.PUSHINT32)
            {
                if (script.Length != 69) return false;
                index = 5;
            }
            else if (script[0] == (byte)OpCode.PUSHINT64)
            {
                if (script.Length != 73) return false;
                index = 9;
            }
            else if (script[0] == (byte)OpCode.PUSHINT128)
            {
                if (script.Length != 81) return false;
                index = 17;
            }
            else if (script[0] == (byte)OpCode.PUSHINT256)
            {
                if (script.Length != 97) return false;
                index = 33;
            }
            else
            {
                // Other pushes
                return false;
            }

            if (script[index++] != (byte)OpCode.PUSHDATA1) return false; // Address
            if (script[index++] != 20) return false;
            index += 20;
            if (script[index++] != (byte)OpCode.PUSH1) return false;
            if (script[index++] != (byte)OpCode.PACK) return false;
            if (script[index++] != (byte)OpCode.PUSHDATA1) return false;
            if (script[index++] != balanceOf.Length) return false; // balanceOf

            for (int x = 0; x < balanceOf.Length; index++, x++)
                if (script[index] != balanceOf[x]) return false;

            if (script[index++] != (byte)OpCode.PUSHDATA1) return false;    // Contract
            if (script[index++] != 20) return false;
            index += 20;
            if (script[index++] != (byte)OpCode.SYSCALL) return false;
            if (BitConverter.ToUInt32(script, index) != ApplicationEngine.System_Contract_Call)
                return false;
            index += 4;
            if (script[index++] != (byte)OpCode.EQUAL) return false;
            if (script[index++] != (byte)OpCode.ASSERT) return false;
            return true;
        }

        public static bool IsStandardContract(this byte[] script)
        {
            return script.IsSignatureContract() || script.IsMultiSigContract();
        }

        public static UInt160 ToScriptHash(this byte[] script)
        {
            return new UInt160(Crypto.Hash160(script));
        }

        public static UInt160 ToScriptHash(this ReadOnlySpan<byte> script)
        {
            return new UInt160(Crypto.Hash160(script));
        }

        internal static bool VerifyWitnesses(this IVerifiable verifiable, StoreView snapshot, long gas, WitnessFlag filter = WitnessFlag.All)
        {
            if (gas < 0) return false;
            if (gas > MaxVerificationGas) gas = MaxVerificationGas;

            UInt160[] hashes;
            try
            {
                hashes = verifiable.GetScriptHashesForVerifying(snapshot);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            if (hashes.Length != verifiable.Witnesses.Length) return false;
            for (int i = 0; i < hashes.Length; i++)
            {
                WitnessFlag flag = verifiable.Witnesses[i].StateDependent ? WitnessFlag.StateDependent : WitnessFlag.StateIndependent;
                if (!filter.HasFlag(flag))
                {
                    gas -= verifiable.Witnesses[i].GasConsumed;
                    if (gas < 0) return false;
                    continue;
                }

                using (ApplicationEngine engine = ApplicationEngine.Create(TriggerType.Verification, verifiable, snapshot?.Clone(), gas))
                {
                    CallFlags callFlags = verifiable.Witnesses[i].StateDependent ? CallFlags.AllowStates : CallFlags.None;
                    byte[] verification = verifiable.Witnesses[i].VerificationScript;

                    if (verification.Length == 0)
                    {
                        ContractState cs = snapshot.Contracts.TryGet(hashes[i]);
                        if (cs is null) return false;
                        if (engine.LoadContract(cs, "verify", callFlags, true) is null)
                            return false;
                    }
                    else
                    {
                        if (NativeContract.IsNative(hashes[i])) return false;
                        if (hashes[i] != verifiable.Witnesses[i].ScriptHash) return false;
                        engine.LoadScript(verification, callFlags, hashes[i], 0);
                    }

                    engine.LoadScript(verifiable.Witnesses[i].InvocationScript, CallFlags.None);
                    if (engine.Execute() == VMState.FAULT) return false;
                    if (engine.ResultStack.Count != 1 || !engine.ResultStack.Pop().GetBoolean()) return false;
                    gas -= engine.GasConsumed;
                    verifiable.Witnesses[i].GasConsumed = engine.GasConsumed;
                }
            }
            return true;
        }
    }
}
