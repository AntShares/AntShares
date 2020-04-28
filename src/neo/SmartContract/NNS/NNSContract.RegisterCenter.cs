using Neo.IO;
using Neo.Ledger;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Array = Neo.VM.Types.Array;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Persistence;
using Neo.SmartContract.Manifest;
using System.Numerics;

namespace Neo.SmartContract.NNS
{
    partial class NNSContract
    {
        public NNSContract()
        {
            Manifest.Features = ContractFeatures.HasStorage;

            var events = new List<ContractEventDescriptor>(Manifest.Abi.Events)
            {
                new ContractMethodDescriptor()
                {
                    Name = "Transfer",
                    Parameters = new ContractParameterDefinition[]
                    {
                        new ContractParameterDefinition()
                        {
                            Name = "from",
                            Type = ContractParameterType.Hash160
                        },
                        new ContractParameterDefinition()
                        {
                            Name = "to",
                            Type = ContractParameterType.Hash160
                        },
                        new ContractParameterDefinition()
                        {
                            Name = "amount",
                            Type = ContractParameterType.Integer
                        },
                        new ContractParameterDefinition()
                        {
                            Name = "name",
                            Type = ContractParameterType.String
                        }
                    },
                    ReturnType = ContractParameterType.Boolean
                }
            };

            Manifest.Abi.Events = events.ToArray();
        }

        [ContractMethod(0, ContractParameterType.String, CallFlags.None, Name = "name")]
        protected StackItem NameMethod(ApplicationEngine engine, Array args)
        {
            return Name;
        }

        [ContractMethod(0, ContractParameterType.String, CallFlags.None, Name = "symbol")]
        protected StackItem SymbolMethod(ApplicationEngine engine, Array args)
        {
            return Symbol;
        }

        [ContractMethod(0, ContractParameterType.Integer, CallFlags.None)]
        protected StackItem TotalSupply(ApplicationEngine engine, Array args)
        {
            return 0;
        }

        [ContractMethod(0, ContractParameterType.Integer, CallFlags.None, Name = "decimals")]
        protected StackItem DecimalsMethod(ApplicationEngine engine, Array args)
        {
            return 0;
        }

        [ContractMethod(0_01000000, ContractParameterType.Integer, CallFlags.None, ParameterTypes = new[] { ContractParameterType.Hash160 }, ParameterNames = new[] { "owner" })]
        protected StackItem BalanceOf(ApplicationEngine engine, Array args)
        {
            return 0;
        }

        [ContractMethod(0_01000000, ContractParameterType.Array, CallFlags.None, ParameterTypes = new[] { ContractParameterType.Hash160 }, ParameterNames = new[] { "owner" })]
        private StackItem OwnerOf(ApplicationEngine engine, Array args)
        {
            return new Array(engine.ReferenceCounter, System.Array.Empty<StackItem>());
        }

        [ContractMethod(0_01000000, ContractParameterType.Array, CallFlags.AllowStates, ParameterTypes = new[] { ContractParameterType.Hash160 }, ParameterNames = new[] { "owner" })]
        private StackItem TokensOf(ApplicationEngine engine, Array args)
        {
            return new Array(engine.ReferenceCounter, GetTokensOf(engine.Snapshot, args).Select(p => (StackItem)p.ToArray()));
        }

        public DomainInfo[] GetTokensOf(StoreView snapshot, Array args)
        {
            UInt160 owner = new UInt160(args[0].GetSpan());
            return snapshot.Storages[CreateStorageKey(Prefix_OwnershipMapping, owner)].Value.AsSerializableArray<DomainInfo>();
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

        //register root name, only can be called by admin
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.String}, ParameterNames = new[] { "name"})]
        public StackItem RegisterRootName(ApplicationEngine engine, Array args)
        {
            string name = args[0].GetString().ToLower();
            UInt256 nameHash = ComputeNameHash(name);

            if (IsRootDomain(name))
            {
                if (!IsAdminCalling(engine)) return false;

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

        //update ttl of first level name, can by called by anyone
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.String, ContractParameterType.Integer }, ParameterNames = new[] { "name", "ttl" })]
        private StackItem RenewName(ApplicationEngine engine, Array args)
        {
            string name = args[0].GetString().ToLower();
            UInt256 nameHash = ComputeNameHash(name);
            uint validUntilBlock = (uint)args[1].GetBigInteger();
            ulong duration = validUntilBlock - engine.Snapshot.Height;
            if (duration < 0) return false;

            if (!InteropService.Runtime.CheckWitnessInternal(engine, engine.CallingScriptHash)) return false;

            if (IsDomain(name))
            {
                StorageKey key = CreateStorageKey(Prefix_Domain, nameHash);
                StorageItem storage = engine.Snapshot.Storages[key];
                DomainInfo domainInfo = storage.Value.AsSerializable<DomainInfo>();
                if (domainInfo is null) return false;
                domainInfo.ValidUntilBlock = validUntilBlock;
                storage = engine.Snapshot.Storages.GetAndChange(key);
                storage.Value = domainInfo.ToArray();

                byte[] script;
                using (ScriptBuilder sb = new ScriptBuilder())
                {
                    ulong year = 365 * 24 * 60 * 60 * 1000L;
                    BigInteger amount = duration * Blockchain.MillisecondsPerBlock * GetRentalPrice(engine.Snapshot) / year;
                    sb.EmitAppCall(GAS.Hash, "transfer", engine.CallingScriptHash, GetReceiptAddress(engine.Snapshot), (new BigDecimal(amount, 8)).Value);
                    script = sb.ToArray();
                }
                using ApplicationEngine engine2 = ApplicationEngine.Run(script, engine.Snapshot.Clone());
                if (engine2.State.HasFlag(VMState.FAULT))
                    return false;

                return true;
            }
            return false;
        }

        // transfer ownership to the specified owner
        [ContractMethod(0_03000000, ContractParameterType.Boolean, CallFlags.AllowModifyStates, ParameterTypes = new[] { ContractParameterType.Hash160, ContractParameterType.Hash160, ContractParameterType.Integer, ContractParameterType.String }, ParameterNames = new[] { "from", "to", "name" })]
        private StackItem Transfer(ApplicationEngine engine, Array args)
        {
            UInt160 from = new UInt160(args[0].GetSpan());
            UInt160 to = new UInt160(args[1].GetSpan());
            string name = args[2].GetString().ToLower();
            UInt256 nameHash = ComputeNameHash(name);
            if (IsRootDomain(name) || !IsDomain(name)) return false;

            // check whether the registration is cross-level 
            string[] names = name.Split(".");
            if (names.Length >= 5) return false;
            string secondLevel = names.Length >= 3 ? string.Join(".", names[^3..]) : null;
            string thirdLevel = names.Length == 4 ? name : null;
            if (isCrossLevel(engine.Snapshot, secondLevel) || isCrossLevel(engine.Snapshot, thirdLevel))
                return false;

            var domainInfo = GetDomainInfo(engine.Snapshot, nameHash);
            if (domainInfo != null)
            {
                UInt160 owner = domainInfo.Owner;
                if ((engine.Snapshot.Height - domainInfo.ValidUntilBlock < 0) && !owner.Equals(engine.CallingScriptHash) && !InteropService.Runtime.CheckWitnessInternal(engine, from))
                    return false;
            }
            SetOwner(engine, name, to);
            UpdateOwnerShip(engine, name, to);
            UpdateOwnerShip(engine, name, from, false);

            engine.SendNotification(Hash, new Array(new StackItem[] { "Transfer", from.ToArray(), to.ToArray(), 0, name }));
            return true;
        }

        private DomainInfo GetDomainInfo(StoreView snapshot, UInt256 nameHash)
        {
            StorageKey key = CreateStorageKey(Prefix_Domain, nameHash);
            StorageItem storage = snapshot.Storages.TryGet(key);
            if (storage is null) return null;
            return storage.Value.AsSerializable<DomainInfo>();
        }

        private bool isCrossLevel(StoreView snapshot, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string fatherLevel = string.Join(".", name.Split(".")[1..]);
            UInt256 nameHash = ComputeNameHash(fatherLevel);
            var domainInfo = GetDomainInfo(snapshot, nameHash);
            if (domainInfo is null) return true;
            return false;               
        }

        private UInt256 ComputeNameHash(string name)
        {
            return new UInt256(Crypto.Hash256(Encoding.UTF8.GetBytes(name)));
        }

        private bool IsAdminCalling(ApplicationEngine engine)
        {
            ECPoint[] admins = GetAdmin(engine.Snapshot);
            UInt160 script = Contract.CreateMultiSigRedeemScript(admins.Length - (admins.Length - 1) / 3, admins).ToScriptHash();
            if (!InteropService.Runtime.CheckWitnessInternal(engine, script)) return false;
            return true;
        }
    }
}
