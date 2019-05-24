﻿#pragma warning disable IDE0051
#pragma warning disable IDE0060

using Neo.IO;
using Neo.Ledger;
using Neo.Persistence;
using Neo.SmartContract.Manifest;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Linq;
using VMArray = Neo.VM.Types.Array;

namespace Neo.SmartContract.Native
{
    public sealed class PolicyContract : NativeContract
    {
        public override string ServiceName => "Neo.Native.Policy";

        private const byte Prefix_MaxTransactionsPerBlock = 23;
        private const byte Prefix_MaxLowPriorityTransactionsPerBlock = 34;
        private const byte Prefix_MaxLowPriorityTransactionSize = 29;
        private const byte Prefix_FeePerByte = 10;
        private const byte Prefix_BlockedAccounts = 15;

        public PolicyContract()
        {
            Manifest.Features = ContractFeatures.HasStorage;
        }

        private bool CheckValidators(ApplicationEngine engine)
        {
            UInt256 prev_hash = engine.Snapshot.PersistingBlock.PrevHash;
            TrimmedBlock prev_block = engine.Snapshot.Blocks[prev_hash];
            return InteropService.CheckWitness(engine, prev_block.NextConsensus);
        }

        protected override long GetPriceForMethod(string method)
        {
            switch (method)
            {
                case "getMaxTransactionsPerBlock":
                case "getMaxLowPriorityTransactionsPerBlock":
                case "getMaxLowPriorityTransactionSize":
                case "getFeePerByte":
                case "getBlockedAccounts":
                    return 0_01000000;
                case "setMaxTransactionsPerBlock":
                case "setMaxLowPriorityTransactionsPerBlock":
                case "setMaxLowPriorityTransactionSize":
                case "setFeePerByte":
                case "blockAccount":
                case "unblockAccount":
                    return 0_03000000;
                default:
                    return base.GetPriceForMethod(method);
            }
        }

        internal override bool Initialize(ApplicationEngine engine)
        {
            if (!base.Initialize(engine)) return false;
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_MaxTransactionsPerBlock), new StorageItem
            {
                Value = BitConverter.GetBytes(512u)
            });
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_MaxLowPriorityTransactionsPerBlock), new StorageItem
            {
                Value = BitConverter.GetBytes(20u)
            });
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_MaxLowPriorityTransactionSize), new StorageItem
            {
                Value = BitConverter.GetBytes(256u)
            });
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_FeePerByte), new StorageItem
            {
                Value = BitConverter.GetBytes(1000L)
            });
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_BlockedAccounts), new StorageItem
            {
                Value = new UInt160[0].ToByteArray()
            });
            return true;
        }

        [ContractMethod(ContractParameterType.Integer)]
        private StackItem GetMaxTransactionsPerBlock(ApplicationEngine engine, VMArray args)
        {
            return GetMaxTransactionsPerBlock(engine.Snapshot);
        }

        public uint GetMaxTransactionsPerBlock(Snapshot snapshot)
        {
            return BitConverter.ToUInt32(snapshot.Storages[CreateStorageKey(Prefix_MaxTransactionsPerBlock)].Value, 0);
        }

        [ContractMethod(ContractParameterType.Integer)]
        private StackItem GetMaxLowPriorityTransactionsPerBlock(ApplicationEngine engine, VMArray args)
        {
            return GetMaxLowPriorityTransactionsPerBlock(engine.Snapshot);
        }

        public uint GetMaxLowPriorityTransactionsPerBlock(Snapshot snapshot)
        {
            return BitConverter.ToUInt32(snapshot.Storages[CreateStorageKey(Prefix_MaxLowPriorityTransactionsPerBlock)].Value, 0);
        }

        [ContractMethod(ContractParameterType.Integer)]
        private StackItem GetMaxLowPriorityTransactionSize(ApplicationEngine engine, VMArray args)
        {
            return GetMaxLowPriorityTransactionSize(engine.Snapshot);
        }

        public uint GetMaxLowPriorityTransactionSize(Snapshot snapshot)
        {
            return BitConverter.ToUInt32(snapshot.Storages[CreateStorageKey(Prefix_MaxLowPriorityTransactionSize)].Value, 0);
        }

        [ContractMethod(ContractParameterType.Integer)]
        private StackItem GetFeePerByte(ApplicationEngine engine, VMArray args)
        {
            return GetFeePerByte(engine.Snapshot);
        }

        public long GetFeePerByte(Snapshot snapshot)
        {
            return BitConverter.ToInt64(snapshot.Storages[CreateStorageKey(Prefix_FeePerByte)].Value, 0);
        }

        [ContractMethod(ContractParameterType.Array)]
        private StackItem GetBlockedAccounts(ApplicationEngine engine, VMArray args)
        {
            return GetBlockedAccounts(engine.Snapshot).Select(p => (StackItem)p.ToArray()).ToList();
        }

        public UInt160[] GetBlockedAccounts(Snapshot snapshot)
        {
            return snapshot.Storages[CreateStorageKey(Prefix_BlockedAccounts)].Value.AsSerializableArray<UInt160>();
        }

        [ContractMethod(ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.Integer }, ParameterNames = new[] { "value" })]
        private StackItem SetMaxTransactionsPerBlock(ApplicationEngine engine, VMArray args)
        {
            if (engine.Trigger != TriggerType.Application) return false;
            if (!CheckValidators(engine)) return false;
            uint value = (uint)args[0].GetBigInteger();
            StorageItem storage = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_MaxTransactionsPerBlock));
            storage.Value = BitConverter.GetBytes(value);
            return true;
        }

        [ContractMethod(ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.Integer }, ParameterNames = new[] { "value" })]
        private StackItem SetMaxLowPriorityTransactionsPerBlock(ApplicationEngine engine, VMArray args)
        {
            if (engine.Trigger != TriggerType.Application) return false;
            if (!CheckValidators(engine)) return false;
            uint value = (uint)args[0].GetBigInteger();
            StorageItem storage = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_MaxLowPriorityTransactionsPerBlock));
            storage.Value = BitConverter.GetBytes(value);
            return true;
        }

        [ContractMethod(ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.Integer }, ParameterNames = new[] { "value" })]
        private StackItem SetMaxLowPriorityTransactionSize(ApplicationEngine engine, VMArray args)
        {
            if (engine.Trigger != TriggerType.Application) return false;
            if (!CheckValidators(engine)) return false;
            uint value = (uint)args[0].GetBigInteger();
            StorageItem storage = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_MaxLowPriorityTransactionSize));
            storage.Value = BitConverter.GetBytes(value);
            return true;
        }

        [ContractMethod(ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.Integer }, ParameterNames = new[] { "value" })]
        private StackItem SetFeePerByte(ApplicationEngine engine, VMArray args)
        {
            if (engine.Trigger != TriggerType.Application) return false;
            if (!CheckValidators(engine)) return false;
            long value = (long)args[0].GetBigInteger();
            StorageItem storage = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_FeePerByte));
            storage.Value = BitConverter.GetBytes(value);
            return true;
        }

        [ContractMethod(ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.Hash160 }, ParameterNames = new[] { "account" })]
        private StackItem BlockAccount(ApplicationEngine engine, VMArray args)
        {
            if (engine.Trigger != TriggerType.Application) return false;
            if (!CheckValidators(engine)) return false;
            UInt160 account = new UInt160(args[0].GetByteArray());
            StorageKey key = CreateStorageKey(Prefix_BlockedAccounts);
            StorageItem storage = engine.Snapshot.Storages[key];
            HashSet<UInt160> accounts = new HashSet<UInt160>(storage.Value.AsSerializableArray<UInt160>());
            if (!accounts.Add(account)) return false;
            storage = engine.Snapshot.Storages.GetAndChange(key);
            storage.Value = accounts.ToArray().ToByteArray();
            return true;
        }

        [ContractMethod(ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.Hash160 }, ParameterNames = new[] { "account" })]
        private StackItem UnblockAccount(ApplicationEngine engine, VMArray args)
        {
            if (engine.Trigger != TriggerType.Application) return false;
            if (!CheckValidators(engine)) return false;
            UInt160 account = new UInt160(args[0].GetByteArray());
            StorageKey key = CreateStorageKey(Prefix_BlockedAccounts);
            StorageItem storage = engine.Snapshot.Storages[key];
            HashSet<UInt160> accounts = new HashSet<UInt160>(storage.Value.AsSerializableArray<UInt160>());
            if (!accounts.Remove(account)) return false;
            storage = engine.Snapshot.Storages.GetAndChange(key);
            storage.Value = accounts.ToArray().ToByteArray();
            return true;
        }
    }
}
