﻿#pragma warning disable IDE0060

using Neo.IO;
using Neo.Ledger;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native.Tokens;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VMArray = Neo.VM.Types.Array;

namespace Neo.SmartContract.Native
{
    public abstract class NativeContract
    {
        private static readonly List<NativeContract> contracts = new List<NativeContract>();
        private readonly Dictionary<string, Func<ApplicationEngine, VMArray, StackItem>> methods = new Dictionary<string, Func<ApplicationEngine, VMArray, StackItem>>();

        public static IReadOnlyCollection<NativeContract> Contracts { get; } = contracts;
        public static NeoToken NEO { get; } = new NeoToken();
        public static GasToken GAS { get; } = new GasToken();
        public static PolicyContract Policy { get; } = new PolicyContract();

        public abstract string ServiceName { get; }
        public uint ServiceHash { get; }
        public byte[] Script { get; }
        public UInt160 Hash { get; }
        public ContractManifest Manifest { get; }
        public virtual string[] SupportedStandards { get; } = { "NEP-10" };

        protected NativeContract()
        {
            this.ServiceHash = ServiceName.ToInteropMethodHash();
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitSysCall(ServiceHash);
                this.Script = sb.ToArray();
            }
            this.Hash = Script.ToScriptHash();
            this.Manifest = ContractManifest.CreateDefault(this.Hash);
            List<ContractMethodDescriptor> descriptors = new List<ContractMethodDescriptor>();
            foreach (MethodInfo method in GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                ContractMethodAttribute attribute = method.GetCustomAttribute<ContractMethodAttribute>();
                if (attribute is null) continue;
                string name = attribute.Name ?? (method.Name.ToLower()[0] + method.Name.Substring(1));
                methods.Add(name, (Func<ApplicationEngine, VMArray, StackItem>)method.CreateDelegate(typeof(Func<ApplicationEngine, VMArray, StackItem>), this));
                descriptors.Add(new ContractMethodDescriptor
                {
                    Name = name,
                    ReturnType = attribute.ReturnType,
                    Parameters = attribute.ParameterTypes.Zip(attribute.ParameterNames, (t, n) => new ContractParameterDefinition { Type = t, Name = n }).ToArray()
                });
            }
            this.Manifest.Abi.Methods = descriptors.ToArray();
            contracts.Add(this);
        }

        protected StorageKey CreateStorageKey(byte prefix, byte[] key = null)
        {
            StorageKey storageKey = new StorageKey
            {
                ScriptHash = Hash,
                Key = new byte[sizeof(byte) + (key?.Length ?? 0)]
            };
            storageKey.Key[0] = prefix;
            if (key != null)
                Buffer.BlockCopy(key, 0, storageKey.Key, 1, key.Length);
            return storageKey;
        }

        protected StorageKey CreateStorageKey(byte prefix, ISerializable key)
        {
            return CreateStorageKey(prefix, key.ToArray());
        }

        internal bool Invoke(ApplicationEngine engine)
        {
            if (!engine.CurrentScriptHash.Equals(Hash))
                return false;
            string operation = engine.CurrentContext.EvaluationStack.Pop().GetString();
            VMArray args = (VMArray)engine.CurrentContext.EvaluationStack.Pop();
            if (!methods.TryGetValue(operation, out var method))
                return false;
            StackItem result = method(engine, args);
            engine.CurrentContext.EvaluationStack.Push(result);
            return true;
        }

        internal virtual long GetPrice(RandomAccessStack<StackItem> stack)
        {
            return GetPriceForMethod(stack.Peek().GetString());
        }

        protected virtual long GetPriceForMethod(string method)
        {
            return 0;
        }

        internal virtual bool Initialize(ApplicationEngine engine)
        {
            if (engine.Trigger != TriggerType.Application)
                throw new InvalidOperationException();
            return true;
        }

        [ContractMethod(ContractParameterType.Boolean)]
        protected StackItem OnPersist(ApplicationEngine engine, VMArray args)
        {
            return OnPersist(engine);
        }

        protected virtual bool OnPersist(ApplicationEngine engine)
        {
            if (engine.Trigger != TriggerType.System)
                throw new InvalidOperationException();
            return true;
        }

        [ContractMethod(ContractParameterType.Array, Name = "supportedStandards")]
        protected StackItem SupportedStandardsMethod(ApplicationEngine engine, VMArray args)
        {
            return SupportedStandards.Select(p => (StackItem)p).ToList();
        }

        public ApplicationEngine TestCall(string operation, params object[] args)
        {
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitAppCall(Hash, operation, args);
                return ApplicationEngine.Run(sb.ToArray(), testMode: true);
            }
        }
    }
}
