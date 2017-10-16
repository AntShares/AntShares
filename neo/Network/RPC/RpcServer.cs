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

        protected virtual JObject Process(string method, JArray _params, JObject errorTrace)
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
                        errorTrace["1"] = "init";
                        //Console.WriteLine("getaccountlist 0");

                        uint fromTs = (uint)_params[0].AsNumber();

                        uint toTs = (uint)_params[1].AsNumber();

                        uint minHeight = 0;

                        uint maxHeight = Blockchain.Default.Height;

                        uint fromHeight = getHeightOfTs(0, minHeight, maxHeight, fromTs);

                        uint toHeight = getHeightOfTs(0, fromHeight, maxHeight, toTs);

                        int roundPrecision = 2;

                        //Console.WriteLine($"getaccountlist 1 fromHeight:{fromHeight}; toHeight:{toHeight};");
                        errorTrace["2"] = $"fromHeight:{fromHeight}; toHeight:{toHeight};";

                        Dictionary<UInt160, long> neoTxByAccount = new Dictionary<UInt160, long>();
                        Dictionary<UInt160, long> gasTxByAccount = new Dictionary<UInt160, long>();

                        Dictionary<UInt160, long> neoInByAccount = new Dictionary<UInt160, long>();
                        Dictionary<UInt160, decimal> gasInByAccount = new Dictionary<UInt160, decimal>();

                        Dictionary<UInt160, long> neoOutByAccount = new Dictionary<UInt160, long>();
                        Dictionary<UInt160, decimal> gasOutByAccount = new Dictionary<UInt160, decimal>();
                        Dictionary<UInt160, uint> firstTsByAccount = new Dictionary<UInt160, uint>();

                        for (uint index = fromHeight; index < toHeight; index++)
                        {
                            //Console.WriteLine($"getaccountlist 2  fromHeight:{fromHeight}; toHeight:{toHeight}; index:{index};");

                            Block block = Blockchain.Default.GetBlock(index);
                            //Console.WriteLine("getaccountlist 2.1");
                            foreach (Transaction t in block.Transactions)
                            {
                                //Console.WriteLine("getaccountlist 3");

                                Dictionary<UInt160, Dictionary<UInt256, Fixed8>> friendAssetMap = new Dictionary<UInt160, Dictionary<UInt256, Fixed8>>();

                                foreach (CoinReference cr in t.Inputs)
                                {
                                    TransactionOutput ti = t.References[cr];
                                    UInt160 input = ti.ScriptHash;
                                    if ((ti.AssetId == Blockchain.SystemShare.Hash) || (ti.AssetId == Blockchain.SystemCoin.Hash))
                                    {
                                        increment(friendAssetMap, input, ti.AssetId, ti.Value);
                                    }
                                }


                                foreach (TransactionOutput to in t.Outputs)
                                {
                                    UInt160 output = to.ScriptHash;
                                    if ((to.AssetId == Blockchain.SystemShare.Hash) || (to.AssetId == Blockchain.SystemCoin.Hash))
                                    {
                                        increment(friendAssetMap, output, to.AssetId, -to.Value);
                                    }
                                }

                                foreach (UInt160 friend in friendAssetMap.Keys)
                                {
                                    if (!firstTsByAccount.ContainsKey(friend))
                                    {
                                        firstTsByAccount[friend] = block.Timestamp;
                                    }
                                    if (friendAssetMap[friend].ContainsKey(Blockchain.SystemShare.Hash))
                                    {
                                        increment(neoTxByAccount, friend, Fixed8.One);
                                        Fixed8 value = friendAssetMap[friend][Blockchain.SystemShare.Hash];
                                        if (value < Fixed8.Zero)
                                        {
                                            increment(neoInByAccount, friend, -value);
                                        }
                                        else
                                        {
                                            increment(neoOutByAccount, friend, value);
                                        }
                                    }
                                    if (friendAssetMap[friend].ContainsKey(Blockchain.SystemCoin.Hash))
                                    {
                                        increment(gasTxByAccount, friend, Fixed8.One);
                                        Fixed8 value = friendAssetMap[friend][Blockchain.SystemCoin.Hash];
                                        if (value < Fixed8.Zero)
                                        {
                                            increment(gasInByAccount, friend, -value);
                                        }
                                        else
                                        {
                                            increment(gasOutByAccount, friend, value);
                                        }
                                    }
                                }
                            }
                        }
                        errorTrace["3"] = $"accountStateCache";

                        DataCache<UInt160, AccountState> accountStateCache = Blockchain.Default.GetTable<UInt160, AccountState>();

                        errorTrace["4"] = $"addressByAccount";
                        Dictionary<UInt160, String> addressByAccount = new Dictionary<UInt160, String>();
                        foreach (KeyValuePair<UInt160, AccountState> accountStateEntry in accountStateCache.GetEnumerator())
                        {
                            UInt160 key = accountStateEntry.Value.ScriptHash;
                            String address = Wallet.ToAddress(key);
                            addressByAccount[key] = address;
                        }

                        errorTrace["5"] = $"returnList";
                        JArray returnList = new JArray();

                        foreach (KeyValuePair<UInt160, AccountState> accountStateEntry in accountStateCache.GetEnumerator())
                        {
                            UInt160 key = accountStateEntry.Value.ScriptHash;
                            if (addressByAccount.ContainsKey(key))
                            {
                                String address = addressByAccount[key];

                                //Console.WriteLine($"getaccountlist 7 key:{key}; address:{address};");
                                JObject entry = new JObject();
                                entry["account"] = address;

                                if (accountStateEntry.Value.Balances.ContainsKey(Blockchain.SystemShare.Hash))
                                {
                                    entry["neo"] = (long)accountStateEntry.Value.Balances[Blockchain.SystemShare.Hash];
                                }
                                else
                                {
                                    entry["neo"] = 0;
                                }

                                if (accountStateEntry.Value.Balances.ContainsKey(Blockchain.SystemCoin.Hash))
                                {
                                    entry["gas"] = toRoundedDouble((decimal)accountStateEntry.Value.Balances[Blockchain.SystemCoin.Hash], roundPrecision);
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
                                    entry["gas_in"] = toRoundedDouble(gasInByAccount[key], roundPrecision);
                                }
                                else
                                {
                                    entry["gas_in"] = 0;
                                }

                                if (gasOutByAccount.ContainsKey(key))
                                {
                                    entry["gas_out"] = toRoundedDouble(gasOutByAccount[key], roundPrecision);
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

								if (firstTsByAccount.ContainsKey(key))
								{
									entry["first_ts"] = firstTsByAccount[key];
								}
								else
								{
									entry["first_ts"] = 0;
								}

                                returnList.Add(entry);
                            }
                        }
                        errorTrace["6"] = $"return";
                        //Console.WriteLine($"getaccountlist 8 {returnList.Count()}");

                        return returnList;
                    }
                default:
                    throw new RpcException(-32601, $"Method not found \"{method}\"");
            }
        }

        private static double toRoundedDouble(decimal value, int precision)
        {
            double dbl = (double)value;
            return Math.Round(dbl, precision);
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

        private static void increment(Dictionary<UInt160, Dictionary<UInt256, Fixed8>> map, UInt160 key1, UInt256 key2, Fixed8 value)
        {
            if (!map.ContainsKey(key1))
            {
                map[key1] = new Dictionary<UInt256, Fixed8>();
            }
            if (!map[key1].ContainsKey(key2))
            {
                map[key1][key2] = Fixed8.Zero;
            }
            map[key1][key2] += value;
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
            JObject errorTrace = new JObject();
            JObject result = null;
            try
            {
                result = Process(request["method"].AsString(), (JArray)request["params"], errorTrace);
            }
            catch (Exception ex)
            {
#if DEBUG
                return CreateErrorResponse(request["id"], ex.HResult, ex.Message, ex.StackTrace);
#else
                return CreateErrorResponse(request["id"], ex.HResult, ex.Message, errorTrace);
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
