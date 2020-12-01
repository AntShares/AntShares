using Neo.IO;
using Neo.IO.Json;
using Neo.SmartContract;
using System;
using System.IO;

namespace Neo.Network.P2P.Payloads
{
    public class Witness : ISerializable
    {
        /// <summary>
        /// This is designed to allow a MultiSig 21/11 (committee)
        /// Invocation = 11 * (64 + 2) = 726
        /// </summary>
        private const int MaxInvocationScript = 1024;

        /// <summary>
        /// Verification = m + (PUSH_PubKey * 21) + length + null + syscall = 1 + ((2 + 33) * 21) + 2 + 1 + 5 = 744
        /// </summary>
        private const int MaxVerificationScript = 1024;

        public byte[] InvocationScript;
        private byte[] _VerificationScript;
        public byte[] VerificationScript
        {
            get => _VerificationScript;
            set
            {
                _VerificationScript = value;
                StateDependent = _VerificationScript.Length == 0 || SmartContract.Helper.IsSolidTransfer(_VerificationScript);
            }
        }

        internal long GasConsumed { get; set; }

        private UInt160 _scriptHash;
        public virtual UInt160 ScriptHash
        {
            get
            {
                if (_scriptHash == null)
                {
                    _scriptHash = VerificationScript.ToScriptHash();
                }
                return _scriptHash;
            }
        }

        public bool StateDependent { get; private set; } = true;

        public int Size => InvocationScript.GetVarSize() + VerificationScript.GetVarSize();

        void ISerializable.Deserialize(BinaryReader reader)
        {
            InvocationScript = reader.ReadVarBytes(MaxInvocationScript);
            VerificationScript = reader.ReadVarBytes(MaxVerificationScript);
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.WriteVarBytes(InvocationScript);
            writer.WriteVarBytes(VerificationScript);
        }

        public JObject ToJson()
        {
            JObject json = new JObject();
            json["invocation"] = Convert.ToBase64String(InvocationScript);
            json["verification"] = Convert.ToBase64String(VerificationScript);
            return json;
        }
    }
}
