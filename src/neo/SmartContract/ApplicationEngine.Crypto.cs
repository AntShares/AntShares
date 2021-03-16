using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Network.P2P;
using System;

namespace Neo.SmartContract
{
    partial class ApplicationEngine
    {
        /// <summary>
        /// The price of Neo.Crypto.CheckSig.
        /// </summary>
        public const long CheckSigPrice = 1 << 15;

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of Neo.Crypto.CheckSig.
        /// Checks the signature for the current script container.
        /// </summary>
        public static readonly InteropDescriptor Neo_Crypto_CheckSig = Register("Neo.Crypto.CheckSig", nameof(CheckSig), CheckSigPrice, CallFlags.None);

        /// <summary>
        /// The <see cref="InteropDescriptor"/> of Neo.Crypto.CheckMultisig.
        /// Checks the signatures for the current script container.
        /// </summary>
        public static readonly InteropDescriptor Neo_Crypto_CheckMultisig = Register("Neo.Crypto.CheckMultisig", nameof(CheckMultisig), 0, CallFlags.None);

        /// <summary>
        /// The implementation of Neo.Crypto.CheckSig.
        /// Checks the signature for the current script container.
        /// </summary>
        /// <param name="pubkey">The public key of the account.</param>
        /// <param name="signature">The signature of the current script container.</param>
        /// <returns><see langword="true"/> if the signature is valid; otherwise, <see langword="false"/>.</returns>
        protected internal bool CheckSig(byte[] pubkey, byte[] signature)
        {
            try
            {
                return Crypto.VerifySignature(ScriptContainer.GetSignData(ProtocolSettings.Magic), signature, pubkey, ECCurve.Secp256r1);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// The implementation of Neo.Crypto.CheckMultisig.
        /// Checks the signatures for the current script container.
        /// </summary>
        /// <param name="pubkeys">The public keys of the account.</param>
        /// <param name="signatures">The signatures of the current script container.</param>
        /// <returns><see langword="true"/> if the signatures are valid; otherwise, <see langword="false"/>.</returns>
        protected internal bool CheckMultisig(byte[][] pubkeys, byte[][] signatures)
        {
            byte[] message = ScriptContainer.GetSignData(ProtocolSettings.Magic);
            int m = signatures.Length, n = pubkeys.Length;
            if (n == 0 || m == 0 || m > n) throw new ArgumentException();
            AddGas(CheckSigPrice * n * exec_fee_factor);
            try
            {
                for (int i = 0, j = 0; i < m && j < n;)
                {
                    if (Crypto.VerifySignature(message, signatures[i], pubkeys[j], ECCurve.Secp256r1))
                        i++;
                    j++;
                    if (m - i > n - j)
                        return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            return true;
        }
    }
}
