#pragma warning disable IDE0051

using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract.Manifest;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo.SmartContract.Native.Oracle
{
    public sealed partial class OracleContract : NativeContract
    {
        private const int MaxUrlLength = 256;
        private const int MaxFilterLength = 128;
        private const int MaxCallbackLength = 32;
        private const int MaxUserDataLength = 512;

        private const byte Prefix_NodeList = 8;
        private const byte Prefix_RequestId = 9;
        private const byte Prefix_Request = 7;
        private const byte Prefix_IdList = 6;

        private const long OracleRequestPrice = 0_50000000;

        public override int Id => -4;
        public override string Name => "Oracle";

        internal OracleContract()
        {
            Manifest.Features = ContractFeatures.HasStorage;
        }

        [ContractMethod(0_01000000, CallFlags.AllowStates)]
        public ECPoint[] GetOracleNodes(StoreView snapshot)
        {
            return snapshot.Storages[CreateStorageKey(Prefix_NodeList)].GetInteroperable<NodeList>().ToArray();
        }

        private UInt256 GetOriginalTxid(ApplicationEngine engine)
        {
            Transaction tx = (Transaction)engine.ScriptContainer;
            OracleResponse response = tx.Attributes.OfType<OracleResponse>().FirstOrDefault();
            if (response is null) return tx.Hash;
            OracleRequest request = GetRequest(engine.Snapshot, response.Id);
            return request.OriginalTxid;
        }

        public OracleRequest GetRequest(StoreView snapshot, ulong id)
        {
            return snapshot.Storages.TryGet(CreateStorageKey(Prefix_Request).Add(id))?.GetInteroperable<OracleRequest>();
        }

        public IEnumerable<OracleRequest> GetRequests(StoreView snapshot)
        {
            return snapshot.Storages.Find(new byte[] { Prefix_Request }).Select(p => p.Value.GetInteroperable<OracleRequest>());
        }

        public IEnumerable<OracleRequest> GetRequestsByUrl(StoreView snapshot, string url)
        {
            IdList list = snapshot.Storages.TryGet(CreateStorageKey(Prefix_IdList).Add(GetUrlHash(url)))?.GetInteroperable<IdList>();
            if (list is null) yield break;
            foreach (ulong id in list)
                yield return snapshot.Storages[CreateStorageKey(Prefix_Request).Add(id)].GetInteroperable<OracleRequest>();
        }

        private static byte[] GetUrlHash(string url)
        {
            return Crypto.Hash160(Utility.StrictUTF8.GetBytes(url));
        }

        internal override void Initialize(ApplicationEngine engine)
        {
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_NodeList), new StorageItem(new NodeList()));
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_RequestId), new StorageItem(BitConverter.GetBytes(0ul)));
        }

        [ContractMethod(0, CallFlags.AllowModifyStates)]
        private void Finish(ApplicationEngine engine)
        {
            Transaction tx = (Transaction)engine.ScriptContainer;
            OracleResponse response = tx.Attributes.OfType<OracleResponse>().First();
            engine.OracleResponses.Add(response);
            OracleRequest request = GetRequest(engine.Snapshot, response.Id);
            StackItem userData = BinarySerializer.Deserialize(request.UserData, engine.MaxStackSize, engine.MaxItemSize, engine.ReferenceCounter);
            engine.CallFromNativeContract(null, request.CallbackContract, request.CallbackMethod, request.Url, userData, response.Success, response.Result);
        }

        protected override void PostPersist(ApplicationEngine engine)
        {
            base.PostPersist(engine);
            foreach (OracleResponse response in engine.OracleResponses)
            {
                //Remove the request from storage
                StorageKey key = CreateStorageKey(Prefix_Request).Add(response.Id);
                OracleRequest request = engine.Snapshot.Storages[key].GetInteroperable<OracleRequest>();
                engine.Snapshot.Storages.Delete(key);

                //Remove the id from IdList
                key = CreateStorageKey(Prefix_IdList).Add(GetUrlHash(request.Url));
                IdList list = engine.Snapshot.Storages.GetAndChange(key).GetInteroperable<IdList>();
                if (!list.Remove(response.Id)) throw new InvalidOperationException();
                if (list.Count == 0) engine.Snapshot.Storages.Delete(key);

                //Mint GAS for oracle nodes
                ECPoint[] nodes = GetOracleNodes(engine.Snapshot);
                int index = (int)(response.Id % (ulong)nodes.Length);
                UInt160 account = Contract.CreateSignatureRedeemScript(nodes[index]).ToScriptHash();
                GAS.Mint(engine, account, OracleRequestPrice);
            }
        }

        [ContractMethod(OracleRequestPrice, CallFlags.AllowModifyStates)]
        private void Request(ApplicationEngine engine, string url, string filter, string callback, StackItem userData, long gasForRepsonse)
        {
            //Check the arguments
            if (Utility.StrictUTF8.GetByteCount(url) > MaxUrlLength
                || (filter != null && Utility.StrictUTF8.GetByteCount(filter) > MaxFilterLength)
                || Utility.StrictUTF8.GetByteCount(callback) > MaxCallbackLength
                || gasForRepsonse <= 0)
                throw new ArgumentException();

            //Mint gas for the response
            engine.AddGas(gasForRepsonse);
            GAS.Mint(engine, Hash, gasForRepsonse);

            //Increase the request id
            StorageItem item_id = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_RequestId));
            ulong id = BitConverter.ToUInt64(item_id.Value) + 1;
            item_id.Value = BitConverter.GetBytes(id);

            //Put the request to storage
            if (engine.Snapshot.Contracts.TryGet(engine.CallingScriptHash) is null)
                throw new InvalidOperationException();
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_Request).Add(item_id.Value), new StorageItem(new OracleRequest
            {
                OriginalTxid = GetOriginalTxid(engine),
                GasForResponse = gasForRepsonse,
                Url = url,
                Filter = filter,
                CallbackContract = engine.CallingScriptHash,
                CallbackMethod = callback,
                UserData = BinarySerializer.Serialize(userData, MaxUserDataLength)
            }));

            //Add the id to the IdList
            engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_IdList).Add(GetUrlHash(url)), () => new StorageItem(new IdList())).GetInteroperable<IdList>().Add(id);
        }

        [ContractMethod(0, CallFlags.AllowModifyStates)]
        private void SetOracleNodes(ApplicationEngine engine, ECPoint[] nodes)
        {
            if (!CheckCommittee(engine)) throw new InvalidOperationException();
            NodeList list = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_NodeList)).GetInteroperable<NodeList>();
            list.Clear();
            list.AddRange(nodes);
        }

        [ContractMethod(0_01000000, CallFlags.AllowStates)]
        private bool Verify(ApplicationEngine engine)
        {
            Transaction tx = (Transaction)engine.ScriptContainer;
            OracleResponse response = tx.Attributes.OfType<OracleResponse>().FirstOrDefault();
            if (response is null) return false;
            StorageKey key = CreateStorageKey(Prefix_Request).Add(response.Id);
            OracleRequest request = engine.Snapshot.Storages.TryGet(key)?.GetInteroperable<OracleRequest>();
            if (request is null) return false;
            return tx.NetworkFee + tx.SystemFee <= request.GasForResponse;
        }
    }
}
