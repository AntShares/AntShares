using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Oracle;
using Neo.Oracle.Protocols.Https;
using Neo.Persistence;
using Neo.SmartContract.Manifest;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Array = Neo.VM.Types.Array;

namespace Neo.SmartContract.Native.Oracle
{
    public sealed class OracleContract : NativeContract
    {
        public override string ServiceName => "Neo.Native.Oracle";

        public override int Id => -4;

        internal const byte Prefix_Validator = 24;
        internal const byte Prefix_Config = 25;
        internal const byte Prefix_PerRequestFee = 26;
        internal const byte Prefix_OracleResponse = 27;

        public OracleContract()
        {
            Manifest.Features = ContractFeatures.HasStorage;
        }

        internal override bool Initialize(ApplicationEngine engine)
        {
            if (!base.Initialize(engine)) return false;
            if (GetPerRequestFee(engine.Snapshot) != 0) return false;

            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_Config, Encoding.UTF8.GetBytes(HttpConfig.Timeout)), new StorageItem
            {
                Value = new ByteString(BitConverter.GetBytes(5000)).GetSpan().ToArray()
            });
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_PerRequestFee), new StorageItem
            {
                Value = BitConverter.GetBytes(1000)
            });
            return true;
        }

        /// <summary>
        /// Set Oracle Response Only
        /// </summary>
        [ContractMethod(0_03000000, ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.ByteArray, ContractParameterType.ByteArray }, ParameterNames = new[] { "transactionHash", "oracleResponse" })]
        private StackItem SetOracleResponse(ApplicationEngine engine, Array args)
        {
            if (args.Count != 2) return false;

            UInt160 account = GetOracleMultiSigAddress(engine.Snapshot);
            if (!InteropService.Runtime.CheckWitnessInternal(engine, account)) return false;

            // This only can be called by the oracle's multi signature

            var txHash = args[0].GetSpan().AsSerializable<UInt256>();
            var response = args[1].GetSpan().AsSerializable<OracleExecutionCache>();

            StorageItem storage = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_OracleResponse, txHash.ToArray()), () => new StorageItem());
            storage.Value = IO.Helper.ToArray(response);
            return false;
        }

        /// <summary>
        /// Check if the response it's already stored
        /// </summary>
        /// <param name="snapshot">Snapshot</param>
        /// <param name="txHash">Transaction Hash</param>
        public bool ContainsResponse(StoreView snapshot, UInt256 txHash)
        {
            StorageKey key = CreateStorageKey(Prefix_OracleResponse, txHash.ToArray());
            return snapshot.Storages.TryGet(key) != null;
        }

        /// <summary>
        /// Consume Oracle Response
        /// </summary>
        /// <param name="snapshot">Snapshot</param>
        /// <param name="txHash">Transaction Hash</param>
        public OracleExecutionCache ConsumeOracleResponse(StoreView snapshot, UInt256 txHash)
        {
            StorageKey key = CreateStorageKey(Prefix_OracleResponse, txHash.ToArray());
            StorageItem storage = snapshot.Storages.TryGet(key);
            if (storage == null) return null;

            OracleExecutionCache ret = storage.Value.AsSerializable<OracleExecutionCache>();

            // It should be cached by the ApplicationEngine so we can save space removing it

            snapshot.Storages.Delete(key);
            return ret;
        }

        /// <summary>
        /// Consensus node can delegate third party to operate Oracle nodes
        /// </summary>
        /// <param name="engine">VM</param>
        /// <param name="args">Parameter Array</param>
        /// <returns>Returns true if the execution is successful, otherwise returns false</returns>
        [ContractMethod(0_03000000, ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.ByteArray, ContractParameterType.ByteArray }, ParameterNames = new[] { "consignorPubKey", "consigneePubKey" })]
        private StackItem DelegateOracleValidator(ApplicationEngine engine, Array args)
        {
            StoreView snapshot = engine.Snapshot;
            ECPoint consignorPubKey = args[0].GetSpan().AsSerializable<ECPoint>();
            ECPoint consigneePubKey = args[1].GetSpan().AsSerializable<ECPoint>();
            ECPoint[] cnPubKeys = NEO.GetValidators(snapshot);
            if (!cnPubKeys.Contains(consignorPubKey)) return false;
            UInt160 account = Contract.CreateSignatureRedeemScript(consignorPubKey).ToScriptHash();
            if (!InteropService.Runtime.CheckWitnessInternal(engine, account)) return false;
            StorageKey key = CreateStorageKey(Prefix_Validator, consignorPubKey);
            StorageItem item = snapshot.Storages.GetAndChange(key, () => new StorageItem());
            item.Value = consigneePubKey.ToArray();

            byte[] prefixKey = StorageKey.CreateSearchPrefix(Id, new[] { Prefix_Validator });
            List<ECPoint> delegatedOracleValidators = snapshot.Storages.Find(prefixKey).Select(p =>
              (
                  p.Key.Key.AsSerializable<ECPoint>(1)
              )).ToList();
            foreach (var validator in delegatedOracleValidators)
            {
                if (!cnPubKeys.Contains(validator))
                {
                    snapshot.Storages.Delete(CreateStorageKey(Prefix_Validator, validator));
                }
            }
            return true;
        }

        /// <summary>
        /// Get current authorized Oracle validator.
        /// </summary>
        /// <param name="engine">VM</param>
        /// <param name="args">Parameter Array</param>
        /// <returns>Authorized Oracle validator</returns>
        [ContractMethod(0_01000000, ContractParameterType.Array)]
        private StackItem GetOracleValidators(ApplicationEngine engine, Array args)
        {
            return new Array(engine.ReferenceCounter, GetOracleValidators(engine.Snapshot).Select(p => (StackItem)p.ToArray()));
        }

        /// <summary>
        /// Get current authorized Oracle validator
        /// </summary>
        /// <param name="snapshot">snapshot</param>
        /// <returns>Authorized Oracle validator</returns>
        public ECPoint[] GetOracleValidators(StoreView snapshot)
        {
            ECPoint[] cnPubKeys = NEO.GetValidators(snapshot);
            ECPoint[] oraclePubKeys = new ECPoint[cnPubKeys.Length];
            System.Array.Copy(cnPubKeys, oraclePubKeys, cnPubKeys.Length);
            for (int index = 0; index < oraclePubKeys.Length; index++)
            {
                var oraclePubKey = oraclePubKeys[index];
                StorageKey key = CreateStorageKey(Prefix_Validator, oraclePubKey);
                ECPoint delegatePubKey = snapshot.Storages.TryGet(key)?.Value.AsSerializable<ECPoint>();
                if (delegatePubKey != null) { oraclePubKeys[index] = delegatePubKey; }
            }
            return oraclePubKeys.Distinct().ToArray();
        }

        /// <summary>
        /// Get number of current authorized Oracle validator
        /// </summary>
        /// <param name="engine">VM</param>
        /// <param name="args">Parameter Array</param>
        /// <returns>The number of authorized Oracle validator</returns>
        [ContractMethod(0_01000000, ContractParameterType.Integer)]
        private StackItem GetOracleValidatorsCount(ApplicationEngine engine, Array args)
        {
            return GetOracleValidatorsCount(engine.Snapshot);
        }

        /// <summary>
        /// Get number of current authorized Oracle validator
        /// </summary>
        /// <param name="snapshot">snapshot</param>
        /// <returns>The number of authorized Oracle validator</returns>
        public BigInteger GetOracleValidatorsCount(StoreView snapshot)
        {
            return GetOracleValidators(snapshot).Length;
        }

        /// <summary>
        /// Create a Oracle multisignature contract
        /// </summary>
        /// <param name="snapshot">snapshot</param>
        /// <returns>Oracle multisignature address</returns>
        public Contract GetOracleMultiSigContract(StoreView snapshot)
        {
            ECPoint[] oracleValidators = GetOracleValidators(snapshot);
            return Contract.CreateMultiSigContract(oracleValidators.Length - (oracleValidators.Length - 1) / 3, oracleValidators);
        }

        /// <summary>
        /// Create a Oracle multisignature address
        /// </summary>
        /// <param name="snapshot">snapshot</param>
        /// <returns>Oracle multisignature address</returns>
        public UInt160 GetOracleMultiSigAddress(StoreView snapshot)
        {
            return GetOracleMultiSigContract(snapshot).ScriptHash;
        }

        /// <summary>
        /// Set HttpConfig
        /// </summary>
        /// <param name="engine">VM</param>
        /// <param name="args">Parameter Array</param>
        /// <returns>Returns true if the execution is successful, otherwise returns false</returns>
        [ContractMethod(0_03000000, ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.String, ContractParameterType.ByteArray }, ParameterNames = new[] { "configKey", "configValue" })]
        private StackItem SetConfig(ApplicationEngine engine, Array args)
        {
            StoreView snapshot = engine.Snapshot;
            UInt160 account = GetOracleMultiSigAddress(snapshot);
            if (!InteropService.Runtime.CheckWitnessInternal(engine, account)) return false;
            string key = args[0].GetString();
            ByteString value = args[1].GetSpan().ToArray();
            StorageItem storage = snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_Config, Encoding.UTF8.GetBytes(key)));
            storage.Value = value.GetSpan().ToArray();
            return true;
        }

        /// <summary>
        /// Get HttpConfig
        /// </summary>
        /// <param name="engine">VM</param>
        /// <returns>value</returns>
        [ContractMethod(0_01000000, ContractParameterType.Array, ParameterTypes = new[] { ContractParameterType.String }, ParameterNames = new[] { "configKey" })]
        private StackItem GetConfig(ApplicationEngine engine, Array args)
        {
            StoreView snapshot = engine.Snapshot;
            string key = args[0].GetString();
            return GetConfig(snapshot, key);
        }

        /// <summary>
        /// Get HttpConfig
        /// </summary>
        /// <param name="snapshot">snapshot</param>
        /// <param name="key">key</param>
        /// <returns>value</returns>
        public ByteString GetConfig(StoreView snapshot, string key)
        {
            StorageItem storage = snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_Config, Encoding.UTF8.GetBytes(key)));
            return storage.Value;
        }

        /// <summary>
        /// Set PerRequestFee
        /// </summary>
        /// <param name="engine">VM</param>
        /// <param name="args">Parameter Array</param>
        /// <returns>Returns true if the execution is successful, otherwise returns false</returns>
        [ContractMethod(0_03000000, ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.Integer }, ParameterNames = new[] { "fee" })]
        private StackItem SetPerRequestFee(ApplicationEngine engine, Array args)
        {
            StoreView snapshot = engine.Snapshot;
            UInt160 account = GetOracleMultiSigAddress(snapshot);
            if (!InteropService.Runtime.CheckWitnessInternal(engine, account)) return false;
            int perRequestFee = (int)args[0].GetBigInteger();
            if (perRequestFee <= 0) return false;
            StorageItem storage = snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_PerRequestFee));
            storage.Value = BitConverter.GetBytes(perRequestFee);
            return true;
        }

        /// <summary>
        /// Get PerRequestFee
        /// </summary>
        /// <param name="engine">VM</param>
        /// <param name="args">Parameter Array</param>
        /// <returns>Value</returns>
        [ContractMethod(0_01000000, ContractParameterType.Integer, SafeMethod = true)]
        private StackItem GetPerRequestFee(ApplicationEngine engine, Array args)
        {
            return new Integer(GetPerRequestFee(engine.Snapshot));
        }

        /// <summary>
        /// Get PerRequestFee
        /// </summary>
        /// <param name="snapshot">snapshot</param>
        /// <returns>Value</returns>
        public int GetPerRequestFee(StoreView snapshot)
        {
            StorageItem storage = snapshot.Storages.TryGet(CreateStorageKey(Prefix_PerRequestFee));
            if (storage is null) return 0;
            return BitConverter.ToInt32(storage.Value);
        }

        /// <summary>
        /// Oracle get the hash of the current OracleFlow [Request/Response]
        /// </summary>
        [ContractMethod(0_01000000, ContractParameterType.Boolean, SafeMethod = true)]
        private StackItem GetHash(ApplicationEngine engine, Array args)
        {
            if (engine.OracleCache == null)
            {
                return StackItem.Null;
            }
            else
            {
                return engine.OracleCache.Hash.ToArray();
            }
        }

        /// <summary>
        /// Oracle Get
        ///     string url, [UInt160 filter], [string filterMethod], [string filterArgs]
        /// </summary>
        [ContractMethod(0_01000000, ContractParameterType.ByteArray,
            ParameterTypes = new[] { ContractParameterType.String, ContractParameterType.ByteArray, ContractParameterType.String, ContractParameterType.String },
            ParameterNames = new[] { "url", "filterContract", "filterMethod", "filterArgs" })]
        private StackItem Get(ApplicationEngine engine, Array args)
        {
            if (args.Count != 4)
            {
                throw new ArgumentException($"Provided arguments must be 4 instead of {args.Count}");
            }

            if (engine.OracleCache == null)
            {
                // We should enter here only during OnPersist with the OracleRequestTx

                if (engine.ScriptContainer is Transaction tx)
                {
                    // Read Oracle Response

                    engine.OracleCache = NativeContract.Oracle.ConsumeOracleResponse(engine.Snapshot, tx.Hash);

                    // If it doesn't exist, fault

                    if (engine.OracleCache == null)
                    {
                        throw new ArgumentException();
                    }
                }
                else
                {
                    throw new ArgumentException();
                }
            }
            if (!(args[0] is PrimitiveType urlItem) || !Uri.TryCreate(urlItem.GetString(), UriKind.Absolute, out var url) ||
                !(args[1] is StackItem filterContractItem) ||
                !(args[2] is StackItem filterMethodItem) ||
                !(args[3] is StackItem filterArgsItem)
                ) throw new ArgumentException();

            // Create filter

            OracleFilter filter = null;

            if (!filterContractItem.IsNull)
            {
                if (filterContractItem is PrimitiveType filterContract &&
                    filterMethodItem is PrimitiveType filterMethod &&
                    filterArgsItem is PrimitiveType filterArgs)
                {
                    filter = new OracleFilter()
                    {
                        ContractHash = new UInt160(filterContract.Span),
                        FilterMethod = Encoding.UTF8.GetString(filterMethod.Span),
                        FilterArgs = Encoding.UTF8.GetString(filterArgs.Span)
                    };
                }
                else
                {
                    throw new ArgumentException("If the filter it's defined, the values can't be null");
                }
            }

            // Create request

            OracleRequest request;
            switch (url.Scheme.ToLowerInvariant())
            {
                case "https":
                    {
                        request = new OracleHttpsRequest()
                        {
                            Method = HttpMethod.GET,
                            URL = url,
                            Filter = filter
                        };
                        break;
                    }
                default: throw new ArgumentException($"The scheme '{url.Scheme}' it's not allowed");
            }

            // Execute the oracle request

            if (engine.OracleCache.TryGet(request, out var response))
            {
                return response.Result ?? StackItem.Null;
            }

            throw new ArgumentException();
        }
    }
}
