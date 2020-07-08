using Neo.IO;
using Neo.IO.Json;
using System;
using System.IO;
using System.Linq;

namespace Neo.SmartContract.Manifest
{
    /// <summary>
    /// When a smart contract is deployed, it must explicitly declare the features and permissions it will use.
    /// When it is running, it will be limited by its declared list of features and permissions, and cannot make any behavior beyond the scope of the list.
    /// </summary>
    public class ContractManifest : ISerializable
    {
        /// <summary>
        /// Max length for a valid Contract Manifest
        /// </summary>
        public const int MaxLength = 4096;

        /// <summary>
        /// Serialized size
        /// </summary>
        public int Size => ToJson().ToString().GetVarSize();

        /// <summary>
        /// Contract hash
        /// </summary>
        public UInt160 Hash => Abi.Hash;

        /// <summary>
        /// A group represents a set of mutually trusted contracts. A contract will trust and allow any contract in the same group to invoke it, and the user interface will not give any warnings.
        /// </summary>
        public ContractGroup[] Groups { get; set; }

        /// <summary>
        /// The features field describes what features are available for the contract.
        /// </summary>
        public ContractFeatures Features { get; set; }

        /// <summary>
        /// For technical details of ABI, please refer to NEP-3: NeoContract ABI. (https://github.com/neo-project/proposals/blob/master/nep-3.mediawiki)
        /// </summary>
        public ContractAbi Abi { get; set; }

        /// <summary>
        /// The permissions field is an array containing a set of Permission objects. It describes which contracts may be invoked and which methods are called.
        /// </summary>
        public ContractPermission[] Permissions { get; set; }

        /// <summary>
        /// The trusts field is an array containing a set of contract hashes or group public keys. It can also be assigned with a wildcard *. If it is a wildcard *, then it means that it trusts any contract.
        /// If a contract is trusted, the user interface will not give any warnings when called by the contract.
        /// </summary>
        public WildcardContainer<UInt160> Trusts { get; set; }

        /// <summary>
        /// The safemethods field is an array containing a set of method names. It can also be assigned with a wildcard *. If it is a wildcard *, then it means that all methods of the contract are safe.
        /// If a method is marked as safe, the user interface will not give any warnings when it is called by any other contract.
        /// </summary>
        public WildcardContainer<string> SafeMethods { get; set; }

        /// <summary>
        /// Custom user data
        /// </summary>
        public JObject Extra { get; set; }

        /// <summary>
        /// NEP10 - SupportedStandards
        /// </summary>
        public string[] SupportedStandards { get; set; }

        /// <summary>
        /// Return true if is allowed
        /// </summary>
        /// <param name="manifest">Manifest</param>
        /// <param name="method">Method</param>
        /// <returns>Return true or false</returns>
        public bool CanCall(ContractManifest manifest, string method)
        {
            return Permissions.Any(u => u.IsAllowed(manifest, method));
        }

        /// <summary>
        /// Parse ContractManifest from json
        /// </summary>
        /// <param name="json">Json</param>
        /// <returns>Return ContractManifest</returns>
        public static ContractManifest FromJson(JObject json)
        {
            var manifest = new ContractManifest();
            manifest.DeserializeFromJson(json);
            return manifest;
        }

        /// <summary>
        /// Parse ContractManifest from json
        /// </summary>
        /// <param name="json">Json</param>
        /// <returns>Return ContractManifest</returns>
        public static ContractManifest Parse(ReadOnlySpan<byte> json) => FromJson(JObject.Parse(json));

        public static ContractManifest Parse(string json) => FromJson(JObject.Parse(json));

        /// <summary
        /// To json
        /// </summary>
        public JObject ToJson()
        {
            var feature = new JObject();
            feature["storage"] = Features.HasFlag(ContractFeatures.HasStorage);
            feature["payable"] = Features.HasFlag(ContractFeatures.Payable);

            var json = new JObject();
            json["groups"] = new JArray(Groups.Select(u => u.ToJson()).ToArray());
            json["features"] = feature;
            json["abi"] = Abi.ToJson();
            json["permissions"] = Permissions.Select(p => p.ToJson()).ToArray();
            json["trusts"] = Trusts.ToJson();
            json["safemethods"] = SafeMethods.ToJson();
            json["extra"] = Extra;
            json["supportedstandards"] = new JArray(SupportedStandards.Select(u => new JString(u)).ToArray());

            return json;
        }

        /// <summary>
        /// Clone
        /// </summary>
        /// <returns>Return a copy of this object</returns>
        public ContractManifest Clone()
        {
            return new ContractManifest
            {
                Groups = Groups.Select(p => p.Clone()).ToArray(),
                Features = Features,
                Abi = Abi.Clone(),
                Permissions = Permissions.Select(p => p.Clone()).ToArray(),
                Trusts = Trusts,
                SafeMethods = SafeMethods,
                Extra = Extra?.Clone(),
                SupportedStandards = SupportedStandards.Select(p => p).ToArray()
            };
        }

        /// <summary>
        /// String representation
        /// </summary>
        /// <returns>Return json string</returns>
        public override string ToString() => ToJson().ToString();

        public void Serialize(BinaryWriter writer)
        {
            writer.WriteVarString(ToJson().ToString());
        }

        public void Deserialize(BinaryReader reader)
        {
            DeserializeFromJson(JObject.Parse(reader.ReadVarString(MaxLength)));
        }

        private void DeserializeFromJson(JObject json)
        {
            Abi = ContractAbi.FromJson(json["abi"]);
            Groups = ((JArray)json["groups"]).Select(u => ContractGroup.FromJson(u)).ToArray();
            Features = ContractFeatures.NoProperty;
            Permissions = ((JArray)json["permissions"]).Select(u => ContractPermission.FromJson(u)).ToArray();
            Trusts = WildcardContainer<UInt160>.FromJson(json["trusts"], u => UInt160.Parse(u.AsString()));
            SafeMethods = WildcardContainer<string>.FromJson(json["safemethods"], u => u.AsString());
            Extra = json["extra"];
            SupportedStandards = ((JArray)json["supportedstandards"]).Select(u => u.AsString()).ToArray();
            if (json["features"]["storage"].AsBoolean()) Features |= ContractFeatures.HasStorage;
            if (json["features"]["payable"].AsBoolean()) Features |= ContractFeatures.Payable;
        }

        /// <summary>
        /// Return true if is valid
        /// </summary>
        /// <returns>Return true or false</returns>
        public bool IsValid(UInt160 hash)
        {
            if (!Abi.Hash.Equals(hash)) return false;
            return Groups.All(u => u.IsValid(hash));
        }
    }
}
