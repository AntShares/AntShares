using Neo.IO.Json;
using Neo.SmartContract;
using System.Linq;

namespace Neo.Wallets.NEP6
{
    internal class NEP6Contract : Contract
    {
        public string[] ParameterNames;
        public bool Deployed;

        public static NEP6Contract FromJson(JObject json)
        {
            if (json == null) return null;
            return new NEP6Contract
            {
                Script = json["script"].AsString().Base64ToBytes(),
                ParameterList = ((JArray)json["parameters"]).Select(p => p["type"].TryGetEnum<ContractParameterType>()).ToArray(),
                ParameterNames = ((JArray)json["parameters"]).Select(p => p["name"].AsString()).ToArray(),
                Deployed = json["deployed"].AsBoolean()
            };
        }

        public JObject ToJson()
        {
            JObject contract = new JObject();
            contract["script"] = Script.ToBase64String();
            contract["parameters"] = new JArray(ParameterList.Zip(ParameterNames, (type, name) =>
            {
                JObject parameter = new JObject();
                parameter["name"] = name;
                parameter["type"] = type;
                return parameter;
            }));
            contract["deployed"] = Deployed;
            return contract;
        }
    }
}
