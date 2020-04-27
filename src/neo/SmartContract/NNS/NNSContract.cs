using Neo.IO;
using Neo.Ledger;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Array = Neo.VM.Types.Array;
using Neo.Cryptography;
using System.Text.RegularExpressions;
using Neo.Cryptography.ECC;
using Neo.Persistence;

namespace Neo.SmartContract.NNS
{
    public partial class NNSContract : NativeContract
    {
        public override string ServiceName => "Neo.Native.NNS";
        public override int Id => -5;
        public string Name => "NNS";
        public string Symbol => "nns";
        public byte Decimals => 0;
        public const string DomainRegex = @"^(?=^.{3,255}$)[a-zA-Z0-9][-a-zA-Z0-9]{0,62}(\.[a-zA-Z0-9][-a-zA-Z0-9]{0,62}){1,3}$";
        public const string RootRegex = @"^[a-zA-Z]{0,62}$";

        protected const byte Prefix_Root = 22;
        protected const byte Prefix_Domain = 23;
        protected const byte Prefix_Record = 24;
        protected const byte Prefix_OwnershipMapping = 25;
        protected const byte Prefix_Admin = 26;
        protected const byte Prefix_RentalPrice = 27;

        internal override bool Initialize(ApplicationEngine engine)
        {
            if (!base.Initialize(engine)) return false;

            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_Root), new StorageItem
            {
                Value = new UInt256[0].ToByteArray()
            });

            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_Admin), new StorageItem
            {
                Value = new UInt160[0].ToByteArray()
            });
            return true;
        }

        //Get root name
        [ContractMethod(0_01000000, ContractParameterType.Array, CallFlags.AllowStates)]
        private StackItem GetRootName(ApplicationEngine engine, Array args)
        {
            return new Array(engine.ReferenceCounter, GetRootName(engine.Snapshot).Select(p => (StackItem)p.ToArray()));
        }

        public UInt256[] GetRootName(StoreView snapshot)
        {
            return snapshot.Storages[CreateStorageKey(Prefix_Root)].Value.AsSerializableArray<UInt256>();
        }

        // Get Admin List
        [ContractMethod(0_01000000, ContractParameterType.Array, CallFlags.AllowStates)]
        private StackItem GetAdmin(ApplicationEngine engine, Array args)
        {
            return new Array(engine.ReferenceCounter, GetAdmin(engine.Snapshot).Select(p => (StackItem)p.ToArray()));
        }

        public ECPoint[] GetAdmin(StoreView snapshot)
        {
            return snapshot.Storages[CreateStorageKey(Prefix_Admin)].Value.AsSerializableArray<ECPoint>();
        }

        //register root name, only can be called by admin
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.String}, ParameterNames = new[] { "name"})]
        public StackItem RegisterRootName(ApplicationEngine engine, Array args)
        {
            string name = args[0].GetString().ToLower();
            UInt256 nameHash = new UInt256(Crypto.Hash256(Encoding.UTF8.GetBytes(name)));
            
            if (IsRootDomain(name))
            {
                ECPoint[] admins = GetAdmin(engine.Snapshot);
                UInt160 script = Contract.CreateMultiSigRedeemScript(admins.Length - (admins.Length - 1) / 3, admins).ToScriptHash();
                if (!InteropService.Runtime.CheckWitnessInternal(engine, script)) return false;

                StorageKey key = CreateStorageKey(Prefix_Root);
                StorageItem storage = engine.Snapshot.Storages[key];
                SortedSet<UInt256> roots = new SortedSet<UInt256>(storage.Value.AsSerializableArray<UInt256>());
                if (!roots.Add(nameHash)) return false;
                storage = engine.Snapshot.Storages.GetAndChange(key);
                storage.Value = roots.ToByteArray();
                return true;
            }
            return false;
        }

        //register new name
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.String, ContractParameterType.Hash160, ContractParameterType.Hash160, ContractParameterType.Integer }, ParameterNames = new[] { "name", "owner", "admin", "ttl" })]
        private StackItem RegisterNewName(ApplicationEngine engine, Array args)
        {
            string name = args[0].GetString().ToLower();
            UInt256 nameHash = new UInt256(Crypto.Hash256(Encoding.UTF8.GetBytes(name)));
            UInt160 owner = new UInt160(args[1].GetSpan());
            UInt160 admin = new UInt160(args[2].GetSpan());
            ulong ttl = (ulong)args[3].GetBigInteger();

            if (IsRootDomain(name) || !IsDomain(name)) return false;

            var levels = name.Split(".");

            // check whether the root name exists 
            string root = levels[^1];
            UInt256 rootHash = new UInt256(Crypto.Hash256(Encoding.UTF8.GetBytes(root)));
            if (!GetRootName(engine.Snapshot).Contains(rootHash)) return false;

            // check whether the ttl of the first level is expired
            UInt256 firstLevel = new UInt256(Crypto.Hash256(Encoding.UTF8.GetBytes(levels[^2])));
            var domainInfo = GetDomainInfo(engine, firstLevel);
            if (domainInfo != null && (TimeProvider.Current.UtcNow.ToTimestampMS() - domainInfo.TimeToLive) < 0)
                return false;

            StorageKey key = CreateStorageKey(Prefix_Domain, nameHash);
            StorageItem storage = engine.Snapshot.Storages[key];

            if (storage is null)
            {
                domainInfo = new DomainInfo { Admin = admin, Owner = owner, TimeToLive = ttl, Name = name };
                engine.Snapshot.Storages.Add(key, new StorageItem
                {
                    Value = domainInfo.ToArray()
                });

                UpdateOwnerShip(engine, name, owner);
                return true;
            }
            return false;
        }

        //update ttl of first level name, can by called by anyone
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.String, ContractParameterType.Integer }, ParameterNames = new[] { "name", "ttl" })]
        private StackItem RenewName(ApplicationEngine engine, Array args)
        {
            string name = args[0].GetString().ToLower();
            UInt256 nameHash = new UInt256(Crypto.Hash256(Encoding.UTF8.GetBytes(name)));
            ulong ttl = (ulong)args[1].GetBigInteger();
            if (IsDomain(name))
            {
                StorageKey key = CreateStorageKey(Prefix_Domain, nameHash);
                StorageItem storage = engine.Snapshot.Storages[key];
                DomainInfo domainInfo = storage.Value.AsSerializable<DomainInfo>();
                if (domainInfo is null) return false;
                domainInfo.TimeToLive = ttl;
                storage = engine.Snapshot.Storages.GetAndChange(key);
                storage.Value = domainInfo.ToArray();
                return true;
            }
            return false;
        }

        // set addmin, only can be called by committees
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.PublicKey }, ParameterNames = new[] { "address" })]
        private StackItem SetAdmin(ApplicationEngine engine, Array args)
        {
            //verify multi-signature of committees
            ECPoint[] committees = NEO.GetCommittee(engine.Snapshot);
            UInt160 script = Contract.CreateMultiSigRedeemScript(committees.Length - (committees.Length - 1) / 3, committees).ToScriptHash();
            if (!InteropService.Runtime.CheckWitnessInternal(engine, script))
                return false;

            ECPoint pubkey = args[0].GetSpan().AsSerializable<ECPoint>();
            StorageKey key = CreateStorageKey(Prefix_Admin);
            StorageItem storage = engine.Snapshot.Storages[key];
            SortedSet<ECPoint> admins = new SortedSet<ECPoint>(storage.Value.AsSerializableArray<ECPoint>());
            if (!admins.Add(pubkey)) return false;
            storage = engine.Snapshot.Storages.GetAndChange(key);
            storage.Value = admins.ToByteArray();
            return true;
        }

        // set rental price
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.Integer }, ParameterNames = new[] { "value" })]
        private StackItem SetRentalPrice(ApplicationEngine engine, Array args)
        {
            uint value = (uint)args[0].GetBigInteger();
            StorageItem storage = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_RentalPrice));
            storage.Value = BitConverter.GetBytes(value);
            return true;
        }

        // transfer name to other owner, only can be called by the current owner
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.Hash160, ContractParameterType.Hash160, ContractParameterType.String }, ParameterNames = new[] { "from", "to", "name" })]
        private StackItem Transfer(ApplicationEngine engine, Array args)
        {
            UInt160 from = new UInt160(args[0].GetSpan());
            UInt160 to = new UInt160(args[1].GetSpan());
            string name = args[2].GetString().ToLower();
            UInt256 nameHash = new UInt256(Crypto.Hash256(Encoding.UTF8.GetBytes(name)));

            var domainInfo = GetDomainInfo(engine, nameHash);
            UInt160 owner = domainInfo.Owner;
            if (!owner.Equals(engine.CallingScriptHash) && !InteropService.Runtime.CheckWitnessInternal(engine, from))
                return false;

            SetOwner(engine, name, to);
            UpdateOwnerShip(engine, name, to);
            UpdateOwnerShip(engine, name, from, false);
            return true;
        }

        private DomainInfo GetDomainInfo(ApplicationEngine engine, UInt256 nameHash)
        {
            StorageKey key = CreateStorageKey(Prefix_Domain, nameHash);
            StorageItem storage = engine.Snapshot.Storages.TryGet(key);
            if (storage is null) return null;
            return storage.Value.AsSerializable<DomainInfo>();
        }

        public bool IsDomain(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            Regex regex = new Regex(DomainRegex);
            return regex.Match(name).Success;
        }

        public bool IsRootDomain(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            Regex regex = new Regex(RootRegex);
            return regex.Match(name).Success;
        }
    }
}
