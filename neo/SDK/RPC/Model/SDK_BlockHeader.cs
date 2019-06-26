﻿using Neo.IO.Json;
using Neo.Network.P2P.Payloads;

namespace Neo.SDK.RPC.Model
{
    public class SDK_BlockHeader
    {
        public Header Header { get; set; }
        
        public int Confirmations { get; set; }
        
        public UInt256 NextBlockHash { get; set; }

        public JObject ToJson()
        {
            JObject json = Header.ToJson();
            json["confirmations"] = Confirmations;
            json["nextblockhash"] = NextBlockHash.ToString();
            return json;
        }

        public static SDK_BlockHeader FromJson(JObject json)
        {
            SDK_BlockHeader block = new SDK_BlockHeader();
            block.Confirmations = (int)json["confirmations"].AsNumber();
            block.NextBlockHash = UInt256.Parse(json["nextblockhash"].AsString());
            block.Header = Header.FromJson(json);
            return block;
        }
    }
}
