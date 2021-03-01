using Neo.Cryptography;
using Neo.IO;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using System;
using static Neo.SmartContract.Helper;

namespace Neo.Wallets
{
    public static class Helper
    {
        public static byte[] Sign(this IVerifiable verifiable, KeyPair key, uint magic)
        {
            return Crypto.Sign(verifiable.GetSignData(magic), key.PrivateKey, key.PublicKey.EncodePoint(false)[1..]);
        }

        public static string ToAddress(this UInt160 scriptHash, byte version)
        {
            Span<byte> data = stackalloc byte[21];
            data[0] = version;
            scriptHash.ToArray().CopyTo(data[1..]);
            return Base58.Base58CheckEncode(data);
        }

        public static UInt160 ToScriptHash(this string address, byte version)
        {
            byte[] data = address.Base58CheckDecode();
            if (data.Length != 21)
                throw new FormatException();
            if (data[0] != version)
                throw new FormatException();
            return new UInt160(data.AsSpan(1));
        }

        internal static byte[] XOR(byte[] x, byte[] y)
        {
            if (x.Length != y.Length) throw new ArgumentException();
            byte[] r = new byte[x.Length];
            for (int i = 0; i < r.Length; i++)
                r[i] = (byte)(x[i] ^ y[i]);
            return r;
        }

        public static long CalculateNetworkFee(DataCache snapshot, Transaction tx, ProtocolSettings settings, Func<UInt160, byte[]> accountScript)
        {
            UInt160[] hashes = tx.GetScriptHashesForVerifying(snapshot);

            // base size for transaction: includes const_header + signers + attributes + script + hashes
            int size = Transaction.HeaderSize + tx.Signers.GetVarSize() + tx.Attributes.GetVarSize() + tx.Script.GetVarSize() + IO.Helper.GetVarSize(hashes.Length);
            uint exec_fee_factor = NativeContract.Policy.GetExecFeeFactor(snapshot);
            long networkFee = 0;
            foreach (UInt160 hash in hashes)
            {
                byte[] witnessScript = accountScript(hash);

                if (witnessScript is null && tx.Witnesses != null)
                {
                    // Try to find the script in the witnesses

                    foreach (var witness in tx.Witnesses)
                    {
                        if (witness.ScriptHash == hash)
                        {
                            witnessScript = witness.VerificationScript;
                            break;
                        }
                    }
                }

                if (witnessScript is null)
                {
                    var contract = NativeContract.ContractManagement.GetContract(snapshot, hash);
                    if (contract is null) continue;
                    var md = contract.Manifest.Abi.GetMethod("verify", 0);
                    if (md is null)
                        throw new ArgumentException($"The smart contract {contract.Hash} haven't got verify method without arguments");
                    if (md.ReturnType != ContractParameterType.Boolean)
                        throw new ArgumentException("The verify method doesn't return boolean value.");

                    // Empty invocation and verification scripts
                    size += Array.Empty<byte>().GetVarSize() * 2;

                    // Check verify cost
                    using ApplicationEngine engine = ApplicationEngine.Create(TriggerType.Verification, tx, snapshot.CreateSnapshot(), settings: settings);
                    engine.LoadContract(contract, md, CallFlags.None);
                    if (engine.Execute() == VMState.FAULT) throw new ArgumentException($"Smart contract {contract.Hash} verification fault.");
                    if (!engine.ResultStack.Pop().GetBoolean()) throw new ArgumentException($"Smart contract {contract.Hash} returns false.");

                    networkFee += engine.GasConsumed;
                }
                else if (witnessScript.IsSignatureContract())
                {
                    size += 67 + witnessScript.GetVarSize();
                    networkFee += exec_fee_factor * SignatureContractCost();
                }
                else if (witnessScript.IsMultiSigContract(out int m, out int n))
                {
                    int size_inv = 66 * m;
                    size += IO.Helper.GetVarSize(size_inv) + size_inv + witnessScript.GetVarSize();
                    networkFee += exec_fee_factor * MultiSignatureContractCost(m, n);
                }
                else
                {
                    //We can support more contract types in the future.
                }
            }
            networkFee += size * NativeContract.Policy.GetFeePerByte(snapshot);
            return networkFee;
        }
    }
}
