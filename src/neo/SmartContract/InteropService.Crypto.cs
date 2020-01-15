using Neo.Cryptography;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Linq;
using Array = Neo.VM.Types.Array;

namespace Neo.SmartContract
{
    partial class InteropService
    {
        public static class Crypto
        {
            public static readonly InteropDescriptor ECDsaSecp256r1Verify = Register("Neo.Crypto.ECDsa.Secp256r1.Verify", Crypto_ECDsaSecp256r1Verify, 0_01000000, TriggerType.All, CallFlags.None);
            public static readonly InteropDescriptor ECDsaSecp256k1Verify = Register("Neo.Crypto.ECDsa.Secp256k1.Verify", Crypto_ECDsaSecp256k1Verify, 0_01000000, TriggerType.All, CallFlags.None);
            public static readonly InteropDescriptor ECDsaSecp256r1CheckMultiSig = Register("Neo.Crypto.ECDsa.Secp256r1.CheckMultiSig", Crypto_ECDsaSecp256r1CheckMultiSig, GetECDsaCheckMultiSigPrice, TriggerType.All, CallFlags.None);
            public static readonly InteropDescriptor ECDsaSecp256k1CheckMultiSig = Register("Neo.Crypto.ECDsa.Secp256k1.CheckMultiSig", Crypto_ECDsaSecp256k1CheckMultiSig, GetECDsaCheckMultiSigPrice, TriggerType.All, CallFlags.None);

            private static long GetECDsaCheckMultiSigPrice(EvaluationStack stack)
            {
                if (stack.Count < 2) return 0;
                var item = stack.Peek(1);
                int n;
                if (item is Array array) n = array.Count;
                else n = (int)item.GetBigInteger();
                if (n < 1) return 0;
                return ECDsaSecp256r1Verify.Price * n;
            }

            private static bool Crypto_ECDsaSecp256r1Verify(ApplicationEngine engine)
            {
                return Crypto_ECDsaVerify(engine, Cryptography.ECC.ECCurve.Curve.Secp256r1);
            }

            private static bool Crypto_ECDsaSecp256k1Verify(ApplicationEngine engine)
            {
                return Crypto_ECDsaVerify(engine, Cryptography.ECC.ECCurve.Curve.Secp256k1);
            }

            private static bool Crypto_ECDsaVerify(ApplicationEngine engine, Cryptography.ECC.ECCurve.Curve curve)
            {
                StackItem item0 = engine.CurrentContext.EvaluationStack.Pop();
                ReadOnlySpan<byte> message = item0 switch
                {
                    InteropInterface _interface => _interface.GetInterface<IVerifiable>().GetHashData(),
                    Null _ => engine.ScriptContainer.GetHashData(),
                    _ => item0.GetSpan()
                };
                ReadOnlySpan<byte> pubkey = engine.CurrentContext.EvaluationStack.Pop().GetSpan();
                ReadOnlySpan<byte> signature = engine.CurrentContext.EvaluationStack.Pop().GetSpan();
                try
                {
                    engine.CurrentContext.EvaluationStack.Push(Cryptography.Crypto.VerifySignature(message, signature, pubkey, curve));
                }
                catch (ArgumentException)
                {
                    engine.CurrentContext.EvaluationStack.Push(false);
                }
                return true;
            }

            private static bool Crypto_ECDsaSecp256r1CheckMultiSig(ApplicationEngine engine)
            {
                return Crypto_ECDsaCheckMultiSig(engine, Cryptography.ECC.ECCurve.Curve.Secp256r1);
            }

            private static bool Crypto_ECDsaSecp256k1CheckMultiSig(ApplicationEngine engine)
            {
                return Crypto_ECDsaCheckMultiSig(engine, Cryptography.ECC.ECCurve.Curve.Secp256k1);
            }

            private static bool Crypto_ECDsaCheckMultiSig(ApplicationEngine engine, Cryptography.ECC.ECCurve.Curve curve)
            {
                StackItem item0 = engine.CurrentContext.EvaluationStack.Pop();
                ReadOnlySpan<byte> message = item0 switch
                {
                    InteropInterface _interface => _interface.GetInterface<IVerifiable>().GetHashData(),
                    Null _ => engine.ScriptContainer.GetHashData(),
                    _ => item0.GetSpan()
                };
                int n;
                byte[][] pubkeys;
                StackItem item = engine.CurrentContext.EvaluationStack.Pop();
                if (item is Array array1)
                {
                    pubkeys = array1.Select(p => p.GetSpan().ToArray()).ToArray();
                    n = pubkeys.Length;
                    if (n == 0) return false;
                }
                else
                {
                    n = (int)item.GetBigInteger();
                    if (n < 1 || n > engine.CurrentContext.EvaluationStack.Count) return false;
                    pubkeys = new byte[n][];
                    for (int i = 0; i < n; i++)
                        pubkeys[i] = engine.CurrentContext.EvaluationStack.Pop().GetSpan().ToArray();
                }
                int m;
                byte[][] signatures;
                item = engine.CurrentContext.EvaluationStack.Pop();
                if (item is Array array2)
                {
                    signatures = array2.Select(p => p.GetSpan().ToArray()).ToArray();
                    m = signatures.Length;
                    if (m == 0 || m > n) return false;
                }
                else
                {
                    m = (int)item.GetBigInteger();
                    if (m < 1 || m > n || m > engine.CurrentContext.EvaluationStack.Count) return false;
                    signatures = new byte[m][];
                    for (int i = 0; i < m; i++)
                        signatures[i] = engine.CurrentContext.EvaluationStack.Pop().GetSpan().ToArray();
                }
                bool fSuccess = true;
                try
                {
                    for (int i = 0, j = 0; fSuccess && i < m && j < n;)
                    {
                        if (Cryptography.Crypto.VerifySignature(message, signatures[i], pubkeys[j], curve))
                            i++;
                        j++;
                        if (m - i > n - j)
                            fSuccess = false;
                    }
                }
                catch (ArgumentException)
                {
                    fSuccess = false;
                }
                engine.CurrentContext.EvaluationStack.Push(fSuccess);
                return true;
            }
        }
    }
}
