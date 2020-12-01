using Neo.Cryptography.ECC;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Neo.SmartContract
{
    public class ContractParametersContext
    {
        private class ContextItem
        {
            public byte[] Script;
            public ContractParameter[] Parameters;
            public Dictionary<ECPoint, byte[]> Signatures;

            private ContextItem() { }

            public ContextItem(Contract contract)
            {
                this.Script = contract.Script;
                this.Parameters = contract.ParameterList.Select(p => new ContractParameter { Type = p }).ToArray();
            }

            public static ContextItem FromJson(JObject json)
            {
                return new ContextItem
                {
                    Script = Convert.FromBase64String(json["script"]?.AsString()),
                    Parameters = ((JArray)json["parameters"]).Select(p => ContractParameter.FromJson(p)).ToArray(),
                    Signatures = json["signatures"]?.Properties.Select(p => new
                    {
                        PublicKey = ECPoint.Parse(p.Key, ECCurve.Secp256r1),
                        Signature = Convert.FromBase64String(p.Value.AsString())
                    }).ToDictionary(p => p.PublicKey, p => p.Signature)
                };
            }

            public JObject ToJson()
            {
                JObject json = new JObject();
                if (Script != null)
                    json["script"] = Convert.ToBase64String(Script);
                json["parameters"] = new JArray(Parameters.Select(p => p.ToJson()));
                if (Signatures != null)
                {
                    json["signatures"] = new JObject();
                    foreach (var signature in Signatures)
                        json["signatures"][signature.Key.ToString()] = Convert.ToBase64String(signature.Value);
                }
                return json;
            }
        }

        public readonly IVerifiable Verifiable;
        private readonly Dictionary<UInt160, ContextItem> ContextItems;

        public bool Completed
        {
            get
            {
                if (ContextItems.Count < ScriptHashes.Count)
                    return false;
                return ContextItems.Values.All(p => p != null && p.Parameters.All(q => q.Value != null));
            }
        }

        /// <summary>
        /// Cache for public ScriptHashes field
        /// </summary>
        private UInt160[] _ScriptHashes = null;

        /// <summary>
        /// ScriptHashes are the verifiable ScriptHashes from Verifiable element
        /// Equivalent to: Verifiable.GetScriptHashesForVerifying(Blockchain.Singleton.GetSnapshot())
        /// </summary>
        public IReadOnlyList<UInt160> ScriptHashes
        {
            get
            {
                if (_ScriptHashes == null)
                {
                    // snapshot is not necessary for Transaction
                    if (Verifiable is Transaction)
                    {
                        _ScriptHashes = Verifiable.GetScriptHashesForVerifying(null);
                        return _ScriptHashes;
                    }

                    using (SnapshotView snapshot = Blockchain.Singleton.GetSnapshot())
                    {
                        _ScriptHashes = Verifiable.GetScriptHashesForVerifying(snapshot);
                    }
                }
                return _ScriptHashes;
            }
        }

        public ContractParametersContext(IVerifiable verifiable)
        {
            this.Verifiable = verifiable;
            this.ContextItems = new Dictionary<UInt160, ContextItem>();

            if (verifiable is Transaction tx && tx.Witnesses != null)
            {
                // Check solid transfer

                foreach (var witness in tx.Witnesses)
                {
                    if (witness.VerificationScript != null && witness.VerificationScript.IsSolidTransfer())
                    {
                        _ = CreateItem(Contract.Create(Array.Empty<ContractParameterType>(), witness.VerificationScript));
                    }
                }
            }
        }

        public bool Add(Contract contract, int index, object parameter)
        {
            ContextItem item = CreateItem(contract);
            if (item == null) return false;
            item.Parameters[index].Value = parameter;
            return true;
        }

        public bool Add(Contract contract, params object[] parameters)
        {
            ContextItem item = CreateItem(contract);
            if (item == null) return false;
            for (int index = 0; index < parameters.Length; index++)
            {
                item.Parameters[index].Value = parameters[index];
            }
            return true;
        }

        public bool AddSignature(Contract contract, ECPoint pubkey, byte[] signature)
        {
            if (contract.Script.IsMultiSigContract(out _, out ECPoint[] points))
            {
                ContextItem item = CreateItem(contract);
                if (item == null) return false;
                if (item.Parameters.All(p => p.Value != null)) return false;
                if (item.Signatures == null)
                    item.Signatures = new Dictionary<ECPoint, byte[]>();
                else if (item.Signatures.ContainsKey(pubkey))
                    return false;
                if (!points.Contains(pubkey)) return false;
                item.Signatures.Add(pubkey, signature);
                if (item.Signatures.Count == contract.ParameterList.Length)
                {
                    Dictionary<ECPoint, int> dic = points.Select((p, i) => new
                    {
                        PublicKey = p,
                        Index = i
                    }).ToDictionary(p => p.PublicKey, p => p.Index);
                    byte[][] sigs = item.Signatures.Select(p => new
                    {
                        Signature = p.Value,
                        Index = dic[p.Key]
                    }).OrderByDescending(p => p.Index).Select(p => p.Signature).ToArray();
                    for (int i = 0; i < sigs.Length; i++)
                        if (!Add(contract, i, sigs[i]))
                            throw new InvalidOperationException();
                    item.Signatures = null;
                }
                return true;
            }
            else
            {
                int index = -1;
                for (int i = 0; i < contract.ParameterList.Length; i++)
                    if (contract.ParameterList[i] == ContractParameterType.Signature)
                        if (index >= 0)
                            throw new NotSupportedException();
                        else
                            index = i;

                if (index == -1)
                {
                    // unable to find ContractParameterType.Signature in contract.ParameterList 
                    // return now to prevent array index out of bounds exception
                    return false;
                }
                return Add(contract, index, signature);
            }
        }

        private ContextItem CreateItem(Contract contract)
        {
            if (ContextItems.TryGetValue(contract.ScriptHash, out ContextItem item))
                return item;
            if (!ScriptHashes.Contains(contract.ScriptHash))
                return null;
            item = new ContextItem(contract);
            ContextItems.Add(contract.ScriptHash, item);
            return item;
        }

        public bool IsCompleted(UInt160 scriptHash)
        {
            if (!ContextItems.TryGetValue(scriptHash, out ContextItem item))
                return false;

            return item.Signatures.Count == item.Parameters.Length;
        }

        public static ContractParametersContext FromJson(JObject json)
        {
            var type = typeof(ContractParametersContext).GetTypeInfo().Assembly.GetType(json["type"].AsString());
            if (!typeof(IVerifiable).IsAssignableFrom(type)) throw new FormatException();

            var verifiable = (IVerifiable)Activator.CreateInstance(type);
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(json["hex"].AsString()), false))
            using (BinaryReader reader = new BinaryReader(ms, Utility.StrictUTF8))
            {
                verifiable.DeserializeUnsigned(reader);
            }
            ContractParametersContext context = new ContractParametersContext(verifiable);
            foreach (var property in json["items"].Properties)
            {
                context.ContextItems.Add(UInt160.Parse(property.Key), ContextItem.FromJson(property.Value));
            }
            return context;
        }

        public ContractParameter GetParameter(UInt160 scriptHash, int index)
        {
            return GetParameters(scriptHash)?[index];
        }

        public IReadOnlyList<ContractParameter> GetParameters(UInt160 scriptHash)
        {
            if (!ContextItems.TryGetValue(scriptHash, out ContextItem item))
                return null;
            return item.Parameters;
        }

        public byte[] GetScript(UInt160 scriptHash)
        {
            if (!ContextItems.TryGetValue(scriptHash, out ContextItem item))
                return null;
            return item.Script;
        }

        public Witness[] GetWitnesses()
        {
            if (!Completed) throw new InvalidOperationException();
            Witness[] witnesses = new Witness[ScriptHashes.Count];
            for (int i = 0; i < ScriptHashes.Count; i++)
            {
                ContextItem item = ContextItems[ScriptHashes[i]];
                using (ScriptBuilder sb = new ScriptBuilder())
                {
                    for (int j = item.Parameters.Length - 1; j >= 0; j--)
                    {
                        sb.EmitPush(item.Parameters[j]);
                    }
                    witnesses[i] = new Witness
                    {
                        InvocationScript = sb.ToArray(),
                        VerificationScript = item.Script ?? Array.Empty<byte>()
                    };
                }
            }
            return witnesses;
        }

        public static ContractParametersContext Parse(string value)
        {
            return FromJson(JObject.Parse(value));
        }

        public JObject ToJson()
        {
            JObject json = new JObject();
            json["type"] = Verifiable.GetType().FullName;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms, Utility.StrictUTF8))
            {
                Verifiable.SerializeUnsigned(writer);
                writer.Flush();
                json["hex"] = Convert.ToBase64String(ms.ToArray());
            }
            json["items"] = new JObject();
            foreach (var item in ContextItems)
                json["items"][item.Key.ToString()] = item.Value.ToJson();
            return json;
        }

        public override string ToString()
        {
            return ToJson().ToString();
        }
    }
}
