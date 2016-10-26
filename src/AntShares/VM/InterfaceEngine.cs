﻿using AntShares.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AntShares.VM
{
    internal class InterfaceEngine : IApiService
    {
        public static readonly InterfaceEngine Default = new InterfaceEngine();

        public bool Invoke(string method, ScriptEngine engine)
        {
            switch (method)
            {
                case "System.now":
                    return SystemNow(engine);
                case "System.currentTx":
                    return SystemCurrentTx(engine);
                case "System.currentScriptHash":
                    return SystemCurrentScriptHash(engine);
                case "AntShares.Chain.height":
                    return ChainHeight(engine);
                case "AntShares.Chain.getHeader":
                    return ChainGetHeader(engine);
                case "AntShares.Chain.getBlock":
                    return ChainGetBlock(engine);
                case "AntShares.Chain.getTx":
                    return ChainGetTx(engine);
                case "AntShares.Header.hash":
                    return HeaderHash(engine);
                case "AntShares.Header.version":
                    return HeaderVersion(engine);
                case "AntShares.Header.prevHash":
                    return HeaderPrevHash(engine);
                case "AntShares.Header.merkleRoot":
                    return HeaderMerkleRoot(engine);
                case "AntShares.Header.timestamp":
                    return HeaderTimestamp(engine);
                case "AntShares.Header.nonce":
                    return HeaderNonce(engine);
                case "AntShares.Header.nextMiner":
                    return HeaderNextMiner(engine);
                case "AntShares.Block.txCount":
                    return BlockTxCount(engine);
                case "AntShares.Block.tx":
                    return BlockTx(engine);
                case "AntShares.Block.getTx":
                    return BlockGetTx(engine);
                case "AntShares.TX.hash":
                    return TxHash(engine);
                case "AntShares.TX.type":
                    return TxType(engine);
                case "AntShares.Asset.type":
                    return AssetType(engine);
                case "AntShares.Asset.amount":
                    return AssetAmount(engine);
                case "AntShares.Asset.issuer":
                    return AssetIssuer(engine);
                case "AntShares.Asset.admin":
                    return AssetAdmin(engine);
                case "AntShares.Enroll.pubkey":
                    return EnrollPubkey(engine);
                case "AntShares.TX.attributes":
                    return TxAttributes(engine);
                case "AntShares.TX.inputs":
                    return TxInputs(engine);
                case "AntShares.TX.outputs":
                    return TxOutputs(engine);
                case "AntShares.Attribute.usage":
                    return AttrUsage(engine);
                case "AntShares.Attribute.data":
                    return AttrData(engine);
                case "AntShares.Input.hash":
                    return TxInHash(engine);
                case "AntShares.Input.index":
                    return TxInIndex(engine);
                case "AntShares.Output.asset":
                    return TxOutAsset(engine);
                case "AntShares.Output.value":
                    return TxOutValue(engine);
                case "AntShares.Output.scriptHash":
                    return TxOutScriptHash(engine);
                default:
                    return false;
            }
        }

        private bool SystemNow(ScriptEngine engine)
        {
            engine.Stack.Push(DateTime.Now.ToTimestamp());
            return true;
        }

        private bool SystemCurrentTx(ScriptEngine engine)
        {
            engine.Stack.Push(new StackItem(engine.Signable as Transaction));
            return true;
        }

        private bool SystemCurrentScriptHash(ScriptEngine engine)
        {
            engine.Stack.Push(new StackItem(engine.ExecutingScript.ToScriptHash().ToArray()));
            return true;
        }

        private bool ChainHeight(ScriptEngine engine)
        {
            if (Blockchain.Default == null)
                engine.Stack.Push(0);
            else
                engine.Stack.Push(Blockchain.Default.Height);
            return true;
        }

        private bool ChainGetHeader(ScriptEngine engine)
        {
            if (engine.Stack.Count < 1) return false;
            StackItem x = engine.Stack.Pop();
            byte[][] data = x.GetBytesArray();
            List<Header> r = new List<Header>();
            foreach (byte[] d in data)
            {
                switch (d.Length)
                {
                    case sizeof(uint):
                        uint height = BitConverter.ToUInt32(d, 0);
                        if (Blockchain.Default != null)
                            r.Add(Blockchain.Default.GetHeader(height));
                        else if (height == 0)
                            r.Add(Blockchain.GenesisBlock.Header);
                        else
                            r.Add(null);
                        break;
                    case 32:
                        UInt256 hash = new UInt256(d);
                        if (Blockchain.Default != null)
                            r.Add(Blockchain.Default.GetHeader(hash));
                        else if (hash == Blockchain.GenesisBlock.Hash)
                            r.Add(Blockchain.GenesisBlock.Header);
                        else
                            r.Add(null);
                        break;
                    default:
                        return false;
                }
            }
            engine.Stack.Push(new StackItem(r.ToArray()));
            return true;
        }

        private bool ChainGetBlock(ScriptEngine engine)
        {
            if (engine.Stack.Count < 1) return false;
            StackItem x = engine.Stack.Pop();
            byte[][] data = x.GetBytesArray();
            List<Block> r = new List<Block>();
            foreach (byte[] d in data)
            {
                switch (d.Length)
                {
                    case sizeof(uint):
                        uint height = BitConverter.ToUInt32(d, 0);
                        if (Blockchain.Default != null)
                            r.Add(Blockchain.Default.GetBlock(height));
                        else if (height == 0)
                            r.Add(Blockchain.GenesisBlock);
                        else
                            r.Add(null);
                        break;
                    case 32:
                        UInt256 hash = new UInt256(d);
                        if (Blockchain.Default != null)
                            r.Add(Blockchain.Default.GetBlock(hash));
                        else if (hash == Blockchain.GenesisBlock.Hash)
                            r.Add(Blockchain.GenesisBlock);
                        else
                            r.Add(null);
                        break;
                    default:
                        return false;
                }
            }
            engine.Stack.Push(new StackItem(r.ToArray()));
            return true;
        }

        private bool ChainGetTx(ScriptEngine engine)
        {
            if (engine.Stack.Count < 1) return false;
            StackItem x = engine.Stack.Pop();
            Transaction[] r = x.GetBytesArray().Select(p => Blockchain.Default?.GetTransaction(new UInt256(p))).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool HeaderHash(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            BlockBase[] headers = x.GetArray<BlockBase>();
            if (headers.Any(p => p == null)) return false;
            byte[][] r = headers.Select(p => p.Hash.ToArray()).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool HeaderVersion(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            BlockBase[] headers = x.GetArray<BlockBase>();
            if (headers.Any(p => p == null)) return false;
            uint[] r = headers.Select(p => p.Version).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool HeaderPrevHash(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            BlockBase[] headers = x.GetArray<BlockBase>();
            if (headers.Any(p => p == null)) return false;
            byte[][] r = headers.Select(p => p.PrevBlock.ToArray()).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool HeaderMerkleRoot(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            BlockBase[] headers = x.GetArray<BlockBase>();
            if (headers.Any(p => p == null)) return false;
            byte[][] r = headers.Select(p => p.MerkleRoot.ToArray()).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool HeaderTimestamp(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            BlockBase[] headers = x.GetArray<BlockBase>();
            if (headers.Any(p => p == null)) return false;
            uint[] r = headers.Select(p => p.Timestamp).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool HeaderNonce(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            BlockBase[] headers = x.GetArray<BlockBase>();
            if (headers.Any(p => p == null)) return false;
            ulong[] r = headers.Select(p => p.Nonce).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool HeaderNextMiner(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            BlockBase[] headers = x.GetArray<BlockBase>();
            if (headers.Any(p => p == null)) return false;
            byte[][] r = headers.Select(p => p.NextMiner.ToArray()).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool BlockTxCount(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            Block[] blocks = x.GetArray<Block>();
            if (blocks.Any(p => p == null)) return false;
            int[] r = blocks.Select(p => p.Transactions.Length).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool BlockTx(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            Block block = engine.AltStack.Peek().GetInterface<Block>();
            if (block == null) return false;
            engine.Stack.Push(new StackItem(block.Transactions));
            return true;
        }

        private bool BlockGetTx(ScriptEngine engine)
        {
            if (engine.Stack.Count < 1 || engine.AltStack.Count < 1) return false;
            StackItem block_item = engine.AltStack.Peek();
            Block[] blocks = block_item.GetArray<Block>();
            if (blocks.Any(p => p == null)) return false;
            StackItem index_item = engine.Stack.Pop();
            BigInteger[] indexes = index_item.GetIntArray();
            if (blocks.Length != 1 && indexes.Length != 1 && blocks.Length != indexes.Length)
                return false;
            if (blocks.Length == 1)
                blocks = Enumerable.Repeat(blocks[0], indexes.Length).ToArray();
            else if (indexes.Length == 1)
                indexes = Enumerable.Repeat(indexes[0], blocks.Length).ToArray();
            Transaction[] tx = blocks.Zip(indexes, (b, i) => i >= b.Transactions.Length ? null : b.Transactions[(int)i]).ToArray();
            engine.Stack.Push(new StackItem(tx));
            return true;
        }

        private bool TxHash(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            Transaction[] tx = x.GetArray<Transaction>();
            if (tx.Any(p => p == null)) return false;
            byte[][] r = tx.Select(p => p.Hash.ToArray()).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool TxType(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            Transaction[] tx = x.GetArray<Transaction>();
            if (tx.Any(p => p == null)) return false;
            byte[][] r = tx.Select(p => new[] { (byte)p.Type }).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool AssetType(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            RegisterTransaction[] tx = x.GetArray<RegisterTransaction>();
            if (tx.Any(p => p == null)) return false;
            byte[][] r = tx.Select(p => new[] { (byte)p.AssetType }).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool AssetAmount(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            RegisterTransaction[] tx = x.GetArray<RegisterTransaction>();
            if (tx.Any(p => p == null)) return false;
            long[] r = tx.Select(p => p.Amount.GetData()).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool AssetIssuer(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            RegisterTransaction[] tx = x.GetArray<RegisterTransaction>();
            if (tx.Any(p => p == null)) return false;
            byte[][] r = tx.Select(p => p.Issuer.EncodePoint(true)).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool AssetAdmin(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            RegisterTransaction[] tx = x.GetArray<RegisterTransaction>();
            if (tx.Any(p => p == null)) return false;
            byte[][] r = tx.Select(p => p.Admin.ToArray()).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool EnrollPubkey(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            EnrollmentTransaction[] tx = x.GetArray<EnrollmentTransaction>();
            if (tx.Any(p => p == null)) return false;
            byte[][] r = tx.Select(p => p.PublicKey.EncodePoint(true)).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool TxAttributes(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            Transaction tx = engine.AltStack.Pop().GetInterface<Transaction>();
            if (tx == null) return false;
            engine.Stack.Push(new StackItem(tx.Attributes));
            return true;
        }

        private bool TxInputs(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            Transaction tx = engine.AltStack.Pop().GetInterface<Transaction>();
            if (tx == null) return false;
            engine.Stack.Push(new StackItem(tx.Inputs));
            return true;
        }

        private bool TxOutputs(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            Transaction tx = engine.AltStack.Pop().GetInterface<Transaction>();
            if (tx == null) return false;
            engine.Stack.Push(new StackItem(tx.Outputs));
            return true;
        }

        private bool AttrUsage(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            TransactionAttribute[] attr = x.GetArray<TransactionAttribute>();
            if (attr.Any(p => p == null)) return false;
            byte[][] r = attr.Select(p => new[] { (byte)p.Usage }).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool AttrData(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            TransactionAttribute[] attr = x.GetArray<TransactionAttribute>();
            if (attr.Any(p => p == null)) return false;
            byte[][] r = attr.Select(p => p.Data).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool TxInHash(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            TransactionInput[] inputs = x.GetArray<TransactionInput>();
            if (inputs.Any(p => p == null)) return false;
            byte[][] r = inputs.Select(p => p.PrevHash.ToArray()).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool TxInIndex(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            TransactionInput[] inputs = x.GetArray<TransactionInput>();
            if (inputs.Any(p => p == null)) return false;
            uint[] r = inputs.Select(p => (uint)p.PrevIndex).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool TxOutAsset(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            TransactionOutput[] outputs = x.GetArray<TransactionOutput>();
            if (outputs.Any(p => p == null)) return false;
            byte[][] r = outputs.Select(p => p.AssetId.ToArray()).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }

        private bool TxOutValue(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            TransactionOutput[] outputs = x.GetArray<TransactionOutput>();
            if (outputs.Any(p => p == null)) return false;
            long[] r = outputs.Select(p => p.Value.GetData()).ToArray();
            engine.Stack.Push(r);
            return true;
        }

        private bool TxOutScriptHash(ScriptEngine engine)
        {
            if (engine.AltStack.Count < 1) return false;
            StackItem x = engine.AltStack.Peek();
            TransactionOutput[] outputs = x.GetArray<TransactionOutput>();
            if (outputs.Any(p => p == null)) return false;
            byte[][] r = outputs.Select(p => p.ScriptHash.ToArray()).ToArray();
            engine.Stack.Push(new StackItem(r));
            return true;
        }
    }
}
