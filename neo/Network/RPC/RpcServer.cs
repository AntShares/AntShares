﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Neo.Core;
using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Json;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Neo.Implementations.Blockchains.Utilities;

namespace Neo.Network.RPC
{
    public class RpcServer : IDisposable
    {
        protected readonly LocalNode LocalNode;
        private IWebHost host;

        public RpcServer(LocalNode localNode)
        {
            this.LocalNode = localNode;
        }

        private static JObject CreateErrorResponse(JObject id, int code, string message, JObject data = null)
        {
            JObject response = CreateResponse(id);
            response["error"] = new JObject();
            response["error"]["code"] = code;
            response["error"]["message"] = message;
            if (data != null)
                response["error"]["data"] = data;
            return response;
        }

        private static JObject CreateResponse(JObject id)
        {
            JObject response = new JObject();
            response["jsonrpc"] = "2.0";
            response["id"] = id;
            return response;
        }

        public void Dispose()
        {
            if (host != null)
            {
                host.Dispose();
                host = null;
            }
        }

        protected virtual JObject Process(string method, JArray _params)
        {
            switch (method)
            {
                case "getaccountstate":
                    {
                        UInt160 script_hash = Wallet.ToScriptHash(_params[0].AsString());
                        AccountState account = Blockchain.Default.GetAccountState(script_hash) ?? new AccountState(script_hash);
                        return account.ToJson();
                    }
                case "getassetstate":
                    {
                        UInt256 asset_id = UInt256.Parse(_params[0].AsString());
                        AssetState asset = Blockchain.Default.GetAssetState(asset_id);
                        return asset?.ToJson() ?? throw new RpcException(-100, "Unknown asset");
                    }
                case "getbestblockhash":
                    return Blockchain.Default.CurrentBlockHash.ToString();
                case "getblock":
                    {
                        Block block;
                        if (_params[0] is JNumber)
                        {
                            uint index = (uint)_params[0].AsNumber();
                            block = Blockchain.Default.GetBlock(index);
                        }
                        else
                        {
                            UInt256 hash = UInt256.Parse(_params[0].AsString());
                            block = Blockchain.Default.GetBlock(hash);
                        }
                        if (block == null)
                            throw new RpcException(-100, "Unknown block");
                        bool verbose = _params.Count >= 2 && _params[1].AsBooleanOrDefault(false);
                        if (verbose)
                        {
                            JObject json = block.ToJson();
                            json["confirmations"] = Blockchain.Default.Height - block.Index + 1;
                            UInt256 hash = Blockchain.Default.GetNextBlockHash(block.Hash);
                            if (hash != null)
                                json["nextblockhash"] = hash.ToString();
                            return json;
                        }
                        else
                        {
                            return block.ToArray().ToHexString();
                        }
                    }
                case "getblockcount":
                    return Blockchain.Default.Height + 1;
                case "getblockhash":
                    {
                        uint height = (uint)_params[0].AsNumber();
                        if (height >= 0 && height <= Blockchain.Default.Height)
                        {
                            return Blockchain.Default.GetBlockHash(height).ToString();
                        }
                        else
                        {
                            throw new RpcException(-100, "Invalid Height");
                        }
                    }
                case "getblocksysfee":
                    {
                        uint height = (uint)_params[0].AsNumber();
                        if (height >= 0 && height <= Blockchain.Default.Height)
                        {
                            return Blockchain.Default.GetSysFeeAmount(height).ToString();
                        }
                        else
                        {
                            throw new RpcException(-100, "Invalid Height");
                        }
                    }
                case "getconnectioncount":
                    return LocalNode.RemoteNodeCount;
                case "getcontractstate":
                    {
                        UInt160 script_hash = UInt160.Parse(_params[0].AsString());
                        ContractState contract = Blockchain.Default.GetContract(script_hash);
                        return contract?.ToJson() ?? throw new RpcException(-100, "Unknown contract");
                    }
                case "getrawmempool":
                    return new JArray(LocalNode.GetMemoryPool().Select(p => (JObject)p.Hash.ToString()));
                case "getrawtransaction":
                    {
                        UInt256 hash = UInt256.Parse(_params[0].AsString());
                        bool verbose = _params.Count >= 2 && _params[1].AsBooleanOrDefault(false);
                        int height = -1;
                        Transaction tx = LocalNode.GetTransaction(hash);
                        if (tx == null)
                            tx = Blockchain.Default.GetTransaction(hash, out height);
                        if (tx == null)
                            throw new RpcException(-100, "Unknown transaction");
                        if (verbose)
                        {
                            JObject json = tx.ToJson();
                            if (height >= 0)
                            {
                                Header header = Blockchain.Default.GetHeader((uint)height);
                                json["blockhash"] = header.Hash.ToString();
                                json["confirmations"] = Blockchain.Default.Height - header.Index + 1;
                                json["blocktime"] = header.Timestamp;
                            }
                            return json;
                        }
                        else
                        {
                            return tx.ToArray().ToHexString();
                        }
                    }
                case "getstorage":
                    {
                        UInt160 script_hash = UInt160.Parse(_params[0].AsString());
                        byte[] key = _params[1].AsString().HexToBytes();
                        StorageItem item = Blockchain.Default.GetStorageItem(new StorageKey
                        {
                            ScriptHash = script_hash,
                            Key = key
                        }) ?? new StorageItem();
                        return item.Value?.ToHexString();
                    }
                case "gettxout":
                    {
                        UInt256 hash = UInt256.Parse(_params[0].AsString());
                        ushort index = (ushort)_params[1].AsNumber();
                        return Blockchain.Default.GetUnspent(hash, index)?.ToJson(index);
                    }
                case "sendrawtransaction":
                    {
                        Console.WriteLine("sendrawtransaction 0");
                        Transaction tx = Transaction.DeserializeFrom(_params[0].AsString().HexToBytes());
                        //tx.Print = true;
                        Console.WriteLine("sendrawtransaction 1");
                        bool retval = LocalNode.Relay(tx);
                        Console.WriteLine($"sendrawtransaction 2 {retval}");
                        return retval;
                    }
                case "submitblock":
                    {
                        Block block = _params[0].AsString().HexToBytes().AsSerializable<Block>();
                        return LocalNode.Relay(block);
                    }
                case "validateaddress":
                    {
                        JObject json = new JObject();
                        UInt160 scriptHash;
                        try
                        {
                            scriptHash = Wallet.ToScriptHash(_params[0].AsString());
                        }
                        catch
                        {
                            scriptHash = null;
                        }
                        json["address"] = _params[0];
                        json["isvalid"] = scriptHash != null;
                        return json;
                    }
                case "listpeers":
                    {
                        JObject json = new JObject();

                        {
                            JArray unconnectedPeers = new JArray();
                            foreach (IPEndPoint peer in LocalNode.GetUnconnectedPeers())
                            {
                                JObject peerJson = new JObject();
                                peerJson["address"] = peer.Address.ToString();
                                peerJson["port"] = peer.Port;
                                unconnectedPeers.Add(peerJson);
                            }
                            json["unconnected"] = unconnectedPeers;
                        }

                        {
                            JArray badPeers = new JArray();
                            foreach (IPEndPoint peer in LocalNode.GetBadPeers())
                            {
                                JObject peerJson = new JObject();
                                peerJson["address"] = peer.Address.ToString();
                                peerJson["port"] = peer.Port;
                                badPeers.Add(peerJson);
                            }
                            json["bad"] = badPeers;
                        }

                        {
                            JArray connectedPeers = new JArray();
                            foreach (RemoteNode node in LocalNode.GetRemoteNodes())
                            {
                                JObject peerJson = new JObject();
                                peerJson["address"] = node.RemoteEndpoint.Address.ToString();
                                peerJson["port"] = node.ListenerEndpoint.Port;
                                connectedPeers.Add(peerJson);
                            }
                            json["connected"] = connectedPeers;
                        }

                        return json;
                    }
                case "gettxcount":
                    {
                        uint fromTs = (uint)_params[0].AsNumber();
                        uint toTs = (uint)_params[1].AsNumber();
#if DEBUG
                        Console.WriteLine($"fromTs:{fromTs};toTs:{toTs};");
#endif
                        uint minHeight = 0;

                        uint maxHeight = Blockchain.Default.Height;

                        uint fromHeight = getHeightOfTs(0, minHeight, maxHeight, fromTs);

                        uint toHeight = getHeightOfTs(0, fromHeight, maxHeight, toTs);

#if DEBUG
                        Console.WriteLine($"fromHeight:{fromHeight};toHeight:{toHeight};");
#endif
                        uint count = 0;

                        for (uint index = fromHeight; index < toHeight; index++)
                        {
                            Block block = Blockchain.Default.GetBlock(index);
                            foreach (Transaction t in block.Transactions)
                            {
                                bool isNeoTransaction = false;

                                foreach (TransactionOutput to in t.Outputs)
                                {
                                    if (to.AssetId == Blockchain.SystemShare.Hash)
                                    {
                                        isNeoTransaction = true;
                                    }
                                }
                                if (isNeoTransaction)
                                {
                                    count++;
                                }
                            }
#if DEBUG
                            Console.WriteLine($"fromHeight:{fromHeight};toHeight:{toHeight};index:{index};count:{count};");
#endif
                        }

                        return count;
                    }
                case "getaccountlist":
                    {
                        //Console.WriteLine("getaccountlist 0");

                        uint fromTs = (uint)_params[0].AsNumber();

                        uint toTs = (uint)_params[1].AsNumber();

                        uint minHeight = 0;

                        uint maxHeight = Blockchain.Default.Height;

                        uint fromHeight = getHeightOfTs(0, minHeight, maxHeight, fromTs);

                        uint toHeight = getHeightOfTs(0, fromHeight, maxHeight, toTs);

                        JArray list = new JArray();

                        Console.WriteLine($"getaccountlist 1 fromHeight:{fromHeight}; toHeight:{toHeight};");

                        Dictionary<UInt160, HashSet<UInt160>> neoFriendByAccount = new Dictionary<UInt160, HashSet<UInt160>>();
                        Dictionary<UInt160, HashSet<UInt160>> gasFriendByAccount = new Dictionary<UInt160, HashSet<UInt160>>();

                        Dictionary<UInt160, long> neoTxByAccount = new Dictionary<UInt160, long>();
                        Dictionary<UInt160, long> gasTxByAccount = new Dictionary<UInt160, long>();

                        Dictionary<UInt160, long> neoInByAccount = new Dictionary<UInt160, long>();
                        Dictionary<UInt160, decimal> gasInByAccount = new Dictionary<UInt160, decimal>();

                        Dictionary<UInt160, long> neoOutByAccount = new Dictionary<UInt160, long>();
                        Dictionary<UInt160, decimal> gasOutByAccount = new Dictionary<UInt160, decimal>();
                        for (uint index = fromHeight; index < toHeight; index++)
                        {
                            //Console.WriteLine($"getaccountlist 2  fromHeight:{fromHeight}; toHeight:{toHeight}; index:{index};");

                            Block block = Blockchain.Default.GetBlock(index);
                            //Console.WriteLine("getaccountlist 2.1");
                            foreach (Transaction t in block.Transactions)
                            {
								//Console.WriteLine("getaccountlist 3");
                                // TODO: try using GetTransactionResults?

								foreach (CoinReference cr in t.Inputs)
                                {
                                    //Console.WriteLine("getaccountlist 4");

                                    TransactionOutput ti = t.References[cr];
                                    UInt160 input = ti.ScriptHash;
                                    Dictionary<UInt160, HashSet<UInt160>> friendByAccount = null;
                                    Dictionary<UInt160, long> txByAccount = null;

                                    if (ti.AssetId == Blockchain.SystemShare.Hash)
                                    {
                                        friendByAccount = neoFriendByAccount;
                                        txByAccount = neoTxByAccount;
                                        increment(neoInByAccount, input, ti.Value);
                                    }
                                    else if (ti.AssetId == Blockchain.SystemCoin.Hash)
                                    {
                                        friendByAccount = gasFriendByAccount;
                                        txByAccount = gasTxByAccount;
										increment(gasInByAccount, input, ti.Value);
                                    }

                                    if (txByAccount != null)
                                    {
                                        foreach (TransactionOutput to in t.Outputs)
                                        {
                                            if (to.AssetId == ti.AssetId)
                                            {
                                                UInt160 output = to.ScriptHash;
                                                if (input == output)
                                                {
                                                    if (ti.AssetId == Blockchain.SystemShare.Hash)
                                                    {
                                                        increment(neoInByAccount, input, -ti.Value);
                                                    }
                                                    else if (ti.AssetId == Blockchain.SystemCoin.Hash)
                                                    {
                                                        increment(gasInByAccount, input, -ti.Value);
                                                    }
                                                }
                                                else
                                                {
                                                    if (txByAccount.ContainsKey(input))
                                                    {
                                                        txByAccount[input]++;
                                                    }
                                                    else
                                                    {
                                                        txByAccount[input] = 1;
                                                    }
                                                    if (txByAccount.ContainsKey(output))
                                                    {
                                                        txByAccount[output]++;
                                                    }
                                                    else
                                                    {
                                                        txByAccount[output] = 1;
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    if (friendByAccount != null)
                                    {
                                        if (!friendByAccount.ContainsKey(input))
                                        {
                                            friendByAccount[input] = new HashSet<UInt160>();
                                        }

                                        foreach (TransactionOutput to in t.Outputs)
                                        {
                                            if (to.AssetId == ti.AssetId)
                                            {
                                                UInt160 output = to.ScriptHash;

                                                if (!friendByAccount.ContainsKey(output))
                                                {
                                                    friendByAccount[output] = new HashSet<UInt160>();
                                                }

                                                friendByAccount[input].Add(output);
                                                friendByAccount[output].Add(input);
                                            }
                                        }
                                    }
                                }


                                foreach (TransactionOutput to in t.Outputs)
                                {
                                    UInt160 output = to.ScriptHash;
                                    if (to.AssetId == Blockchain.SystemShare.Hash)
                                    {
                                        increment(neoOutByAccount, output, to.Value);
                                    }
                                    if (to.AssetId == Blockchain.SystemCoin.Hash)
                                    {
                                        increment(gasOutByAccount, output, to.Value);
                                    }
                                }
                            }
                        }

                        DataCache<UInt160, AccountState> accountStateCache = Blockchain.Default.GetTable<UInt160, AccountState>();

                        Dictionary<UInt160, String> addressByAccount = new Dictionary<UInt160, String>();
                        foreach (KeyValuePair<UInt160, AccountState> accountStateEntry in accountStateCache.GetEnumerator())
                        {
                            UInt160 key = accountStateEntry.Value.ScriptHash;
                            String address = Wallet.ToAddress(key);
                            addressByAccount[key] = address;
                        }

                        foreach (KeyValuePair<UInt160, AccountState> accountStateEntry in accountStateCache.GetEnumerator())
                        {
                            UInt160 key = accountStateEntry.Value.ScriptHash;
                            String address = addressByAccount[key];

                            //Console.WriteLine($"getaccountlist 7 key:{key}; address:{address};");
                            JObject entry = new JObject();
                            entry["account"] = address;

                            if (accountStateEntry.Value.Balances.ContainsKey(Blockchain.SystemShare.Hash))
                            {
                                entry["neo"] = accountStateEntry.Value.Balances[Blockchain.SystemShare.Hash].value;
                            }
                            else
                            {
                                entry["neo"] = 0;
                            }

                            if (accountStateEntry.Value.Balances.ContainsKey(Blockchain.SystemCoin.Hash))
                            {
                                entry["gas"] = accountStateEntry.Value.Balances[Blockchain.SystemCoin.Hash].value;
                            }
                            else
                            {
                                entry["gas"] = 0;
                            }

                            if (neoInByAccount.ContainsKey(key))
                            {
                                entry["neo_in"] = neoInByAccount[key];
                            }
                            else
                            {
                                entry["neo_in"] = 0;
                            }

                            if (neoOutByAccount.ContainsKey(key))
                            {
                                entry["neo_out"] = neoOutByAccount[key];
                            }
                            else
                            {
                                entry["neo_out"] = 0;
                            }


                            if (gasInByAccount.ContainsKey(key))
                            {
                                entry["gas_in"] = (double)gasInByAccount[key];
                            }
                            else
                            {
                                entry["gas_in"] = 0;
                            }

                            if (gasOutByAccount.ContainsKey(key))
                            {
                                entry["gas_out"] = (double)gasOutByAccount[key];
                            }
                            else
                            {
                                entry["gas_out"] = 0;
                            }

                            if (neoTxByAccount.ContainsKey(key))
                            {
                                entry["neo_tx"] = neoTxByAccount[key];
                            }
                            else
                            {
                                entry["neo_tx"] = 0;
                            }

                            if (gasTxByAccount.ContainsKey(key))
                            {
                                entry["gas_tx"] = gasTxByAccount[key];
                            }
                            else
                            {
                                entry["gas_tx"] = 0;
                            }

                            if (neoFriendByAccount.ContainsKey(key))
                            {
                                entry["neo_friends"] = neoFriendByAccount[key].Count();
                            }
                            else
                            {
                                entry["neo_friends"] = 0;
                            }

                            if (gasFriendByAccount.ContainsKey(key))
                            {
                                entry["gas_friends"] = gasFriendByAccount[key].Count();
                            }
                            else
                            {
                                entry["gas_friends"] = 0;
                            }

                            list.Add(entry);
                        }

                        Console.WriteLine($"getaccountlist 8 {list.Count()}");

                        return list;
                    }
                default:
                    throw new RpcException(-32601, "Method not found");
            }
        }

        private static void increment(Dictionary<UInt160, decimal> map, UInt160 key, Fixed8 value)
        {
            if (map.ContainsKey(key))
            {
                map[key] += (decimal)value;
            }
            else
            {
                map[key] = (decimal)value;
            }
        }

        private static void increment(Dictionary<UInt160, long> map, UInt160 key, Fixed8 value)
        {
            if (map.ContainsKey(key))
            {
                map[key] += (long)value;
            }
            else
            {
                map[key] = (long)value;
            }
        }

        private uint getHeightOfTs(uint level, uint minHeight, uint maxHeight, uint ts)
        {
            uint midHeight = minHeight + ((maxHeight - minHeight) / 2);
            if ((midHeight == minHeight) || (midHeight == maxHeight))
            {
                return minHeight;
            }
            Block midBlock = Blockchain.Default.GetBlock(midHeight);
            if (ts == midBlock.Timestamp)
            {
                return midHeight;
            }
            else if (ts < midBlock.Timestamp)
            {
#if DEBUG
                Console.WriteLine($"level:{level};minHeight:{minHeight};midHeight:{midHeight};midBlock.Timestamp:{midBlock.Timestamp};");
#endif
                return getHeightOfTs(level + 1, minHeight, midHeight, ts);
            }
            else
            {
#if DEBUG
                Console.WriteLine($"level:{level};midHeight:{midHeight};maxHeight:{maxHeight};midBlock.Timestamp:{midBlock.Timestamp};");
#endif
                return getHeightOfTs(level + 1, midHeight, maxHeight, ts);
            }
        }

        private async Task ProcessAsync(HttpContext context)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            context.Response.Headers["Access-Control-Max-Age"] = "31536000";
            if (context.Request.Method != "GET" && context.Request.Method != "POST") return;
            JObject request = null;
            if (context.Request.Method == "GET")
            {
                string jsonrpc = context.Request.Query["jsonrpc"];
                string id = context.Request.Query["id"];
                string method = context.Request.Query["method"];
                string _params = context.Request.Query["params"];
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(method) && !string.IsNullOrEmpty(_params))
                {
                    try
                    {
                        _params = Encoding.UTF8.GetString(Convert.FromBase64String(_params));
                    }
                    catch (FormatException) { }
                    request = new JObject();
                    if (!string.IsNullOrEmpty(jsonrpc))
                        request["jsonrpc"] = jsonrpc;
                    request["id"] = double.Parse(id);
                    request["method"] = method;
                    request["params"] = JObject.Parse(_params);
                }
            }
            else if (context.Request.Method == "POST")
            {
                using (StreamReader reader = new StreamReader(context.Request.Body))
                {
                    try
                    {
                        request = JObject.Parse(reader);
                    }
                    catch (FormatException) { }
                }
            }
            JObject response;
            if (request == null)
            {
                response = CreateErrorResponse(null, -32700, "Parse error");
            }
            else if (request is JArray)
            {
                JArray array = (JArray)request;
                if (array.Count == 0)
                {
                    response = CreateErrorResponse(request["id"], -32600, "Invalid Request");
                }
                else
                {
                    response = array.Select(p => ProcessRequest(p)).Where(p => p != null).ToArray();
                }
            }
            else
            {
                response = ProcessRequest(request);
            }
            if (response == null || (response as JArray)?.Count == 0) return;
            context.Response.ContentType = "application/json-rpc";
            await context.Response.WriteAsync(response.ToString());
        }

        private JObject ProcessRequest(JObject request)
        {
            if (!request.ContainsProperty("id")) return null;
            if (!request.ContainsProperty("method") || !request.ContainsProperty("params") || !(request["params"] is JArray))
            {
                return CreateErrorResponse(request["id"], -32600, "Invalid Request");
            }
            JObject result = null;
            try
            {
                result = Process(request["method"].AsString(), (JArray)request["params"]);
            }
            catch (Exception ex)
            {
#if DEBUG
                return CreateErrorResponse(request["id"], ex.HResult, ex.Message, ex.StackTrace);
#else
                return CreateErrorResponse(request["id"], ex.HResult, ex.Message);
#endif
            }
            JObject response = CreateResponse(request["id"]);
            response["result"] = result;
            return response;
        }

        public void Start(params string[] uriPrefix)
        {
            Start(uriPrefix, null, null);
        }

        public void Start(string[] uriPrefix, string sslCert, string password)
        {
            if (uriPrefix.Length == 0)
                throw new ArgumentException();
            IWebHostBuilder builder = new WebHostBuilder();
            builder = builder.UseKestrel();
            builder = builder.UseUrls(uriPrefix).Configure(app => app.Run(ProcessAsync));
            host = builder.Build();
            host.Start();
        }
    }
}
