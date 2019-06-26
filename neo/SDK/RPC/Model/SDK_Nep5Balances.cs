﻿using Neo.IO.Json;
using System.Linq;
using System.Numerics;

namespace Neo.SDK.RPC.Model
{
    public class SDK_Nep5Balances
    {
        public string Address { get; set; }
        
        public SDK_Nep5Balance[] Balances { get; set; }

        public JObject ToJson()
        {
            JObject json = new JObject();
            json["address"] = Address;
            json["balance"] = Balances.Select(p => p.ToJson()).ToArray();
            return json;
        }

        public static SDK_Nep5Balances FromJson(JObject json)
        {
            SDK_Nep5Balances nep5Balance = new SDK_Nep5Balances();
            nep5Balance.Address = json["address"].AsString();
            //List<Balance> listBalance = new List<Balance>();
            nep5Balance.Balances = ((JArray)json["balance"]).Select(p => SDK_Nep5Balance.FromJson(p)).ToArray();
            return nep5Balance;
        }
    }

    public class SDK_Nep5Balance
    {
        public UInt160 AssetHash { get; set; }
        
        public BigInteger Amount { get; set; }
        
        public uint LastUpdatedBlock { get; set; }

        public JObject ToJson()
        {
            JObject json = new JObject();
            json["asset_hash"] = AssetHash.ToArray().ToHexString();
            json["amount"] = Amount.ToString();
            json["last_updated_block"] = LastUpdatedBlock.ToString();
            return json;
        }

        public static SDK_Nep5Balance FromJson(JObject json)
        {
            SDK_Nep5Balance balance = new SDK_Nep5Balance();
            balance.AssetHash = UInt160.Parse(json["asset_hash"].AsString());
            balance.Amount = BigInteger.Parse(json["amount"].AsString());
            balance.LastUpdatedBlock = uint.Parse(json["last_updated_block"].AsString());
            return balance;
        }
    }
}
