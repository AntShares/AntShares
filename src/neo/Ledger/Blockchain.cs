using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Neo.IO.Actors;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace Neo.Ledger
{
    public sealed partial class Blockchain : UntypedActor
    {
        public partial class ApplicationExecuted { }
        public class PersistCompleted { public Block Block; }
        public class Import { public IEnumerable<Block> Blocks; public bool Verify = true; }
        public class ImportCompleted { }
        public class FillMemoryPool { public IEnumerable<Transaction> Transactions; }
        public class FillCompleted { }
        public class Reverify { public IInventory[] Inventories; }
        public class RelayResult { public IInventory Inventory; public VerifyResult Result; }
        internal class Initialize { }
        private class UnverifiedBlocksList { public LinkedList<Block> Blocks = new LinkedList<Block>(); public HashSet<IActorRef> Nodes = new HashSet<IActorRef>(); }

        private readonly static Script onPersistScript, postPersistScript;
        private const int MaxTxToReverifyPerIdle = 10;
        private readonly NeoSystem system;
        private readonly Dictionary<UInt256, Block> block_cache = new Dictionary<UInt256, Block>();
        private readonly Dictionary<uint, UnverifiedBlocksList> block_cache_unverified = new Dictionary<uint, UnverifiedBlocksList>();
        private ImmutableHashSet<UInt160> extensibleWitnessWhiteList;

        static Blockchain()
        {
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitSysCall(ApplicationEngine.System_Contract_NativeOnPersist);
                onPersistScript = sb.ToArray();
            }
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitSysCall(ApplicationEngine.System_Contract_NativePostPersist);
                postPersistScript = sb.ToArray();
            }
        }

        public Blockchain(NeoSystem system)
        {
            this.system = system;
        }

        private void OnImport(IEnumerable<Block> blocks, bool verify)
        {
            uint currentHeight = NativeContract.Ledger.CurrentIndex(system.StoreView);
            foreach (Block block in blocks)
            {
                if (block.Index <= currentHeight) continue;
                if (block.Index != currentHeight + 1)
                    throw new InvalidOperationException();
                if (verify && !block.Verify(system.Settings, system.StoreView))
                    throw new InvalidOperationException();
                Persist(block);
                ++currentHeight;
            }
            Sender.Tell(new ImportCompleted());
        }

        private void AddUnverifiedBlockToCache(Block block)
        {
            // Check if any block proposal for height `block.Index` exists
            if (!block_cache_unverified.TryGetValue(block.Index, out var list))
            {
                // There are no blocks, a new UnverifiedBlocksList is created and, consequently, the current block is added to the list
                list = new UnverifiedBlocksList();
                block_cache_unverified.Add(block.Index, list);
            }
            else
            {
                // Check if any block with the hash being added already exists on possible candidates to be processed
                foreach (var unverifiedBlock in list.Blocks)
                {
                    if (block.Hash == unverifiedBlock.Hash)
                        return;
                }

                if (!list.Nodes.Add(Sender))
                {
                    // Same index with different hash
                    Sender.Tell(Tcp.Abort.Instance);
                    return;
                }
            }

            list.Blocks.AddLast(block);
        }

        private void OnFillMemoryPool(IEnumerable<Transaction> transactions)
        {
            // Invalidate all the transactions in the memory pool, to avoid any failures when adding new transactions.
            system.MemPool.InvalidateAllTransactions();

            DataCache snapshot = system.StoreView;

            // Add the transactions to the memory pool
            foreach (var tx in transactions)
            {
                if (NativeContract.Ledger.ContainsTransaction(snapshot, tx.Hash))
                    continue;
                // First remove the tx if it is unverified in the pool.
                system.MemPool.TryRemoveUnVerified(tx.Hash, out _);
                // Add to the memory pool
                system.MemPool.TryAdd(tx, snapshot);
            }
            // Transactions originally in the pool will automatically be reverified based on their priority.

            Sender.Tell(new FillCompleted());
        }

        private void OnInitialize()
        {
            if (!NativeContract.Ledger.Initialized(system.StoreView))
                Persist(system.GenesisBlock);
            Sender.Tell(new object());
        }

        private void OnInventory(IInventory inventory, bool relay = true)
        {
            VerifyResult result = inventory switch
            {
                Block block => OnNewBlock(block),
                Transaction transaction => OnNewTransaction(transaction),
                ExtensiblePayload payload => OnNewExtensiblePayload(payload),
                _ => throw new NotSupportedException()
            };
            if (result == VerifyResult.Succeed && relay)
            {
                system.LocalNode.Tell(new LocalNode.RelayDirectly { Inventory = inventory });
            }
            SendRelayResult(inventory, result);
        }

        private VerifyResult OnNewBlock(Block block)
        {
            DataCache snapshot = system.StoreView;
            uint currentHeight = NativeContract.Ledger.CurrentIndex(snapshot);
            uint headerHeight = system.HeaderCache.Last?.Index ?? currentHeight;
            if (block.Index <= currentHeight)
                return VerifyResult.AlreadyExists;
            if (block.Index - 1 > headerHeight)
            {
                AddUnverifiedBlockToCache(block);
                return VerifyResult.UnableToVerify;
            }
            if (block.Index == headerHeight + 1)
            {
                if (!block.Verify(system.Settings, snapshot, system.HeaderCache))
                    return VerifyResult.Invalid;
            }
            else
            {
                if (!block.Hash.Equals(system.HeaderCache[block.Index].Hash))
                    return VerifyResult.Invalid;
            }
            block_cache.TryAdd(block.Hash, block);
            if (block.Index == currentHeight + 1)
            {
                Block block_persist = block;
                List<Block> blocksToPersistList = new List<Block>();
                while (true)
                {
                    blocksToPersistList.Add(block_persist);
                    if (block_persist.Index + 1 > headerHeight) break;
                    UInt256 hash = system.HeaderCache[block_persist.Index + 1].Hash;
                    if (!block_cache.TryGetValue(hash, out block_persist)) break;
                }

                int blocksPersisted = 0;
                // 15000 is the default among of seconds per block, while MilliSecondsPerBlock is the current
                uint extraBlocks = (15000 - system.Settings.MillisecondsPerBlock) / 1000;
                foreach (Block blockToPersist in blocksToPersistList)
                {
                    block_cache_unverified.Remove(blockToPersist.Index);
                    Persist(blockToPersist);

                    if (blocksPersisted++ < blocksToPersistList.Count - (2 + Math.Max(0, extraBlocks))) continue;
                    // Empirically calibrated for relaying the most recent 2 blocks persisted with 15s network
                    // Increase in the rate of 1 block per second in configurations with faster blocks

                    if (blockToPersist.Index + 99 >= headerHeight)
                        system.LocalNode.Tell(new LocalNode.RelayDirectly { Inventory = blockToPersist });
                }
                if (block_cache_unverified.TryGetValue(currentHeight + 1, out var unverifiedBlocks))
                {
                    foreach (var unverifiedBlock in unverifiedBlocks.Blocks)
                        Self.Tell(unverifiedBlock, ActorRefs.NoSender);
                    block_cache_unverified.Remove(block.Index + 1);
                }
            }
            else
            {
                if (block.Index + 99 >= headerHeight)
                    system.LocalNode.Tell(new LocalNode.RelayDirectly { Inventory = block });
                if (block.Index == headerHeight + 1)
                    system.HeaderCache.Add(block.Header);
            }
            return VerifyResult.Succeed;
        }

        private void OnNewHeaders(Header[] headers)
        {
            if (system.HeaderCache.Full) return;
            DataCache snapshot = system.StoreView;
            uint headerHeight = system.HeaderCache.Last?.Index ?? NativeContract.Ledger.CurrentIndex(snapshot);
            foreach (Header header in headers)
            {
                if (header.Index > headerHeight + 1) break;
                if (header.Index < headerHeight + 1) continue;
                if (!header.Verify(system.Settings, snapshot, system.HeaderCache)) break;
                system.HeaderCache.Add(header);
                ++headerHeight;
            }
        }

        private VerifyResult OnNewExtensiblePayload(ExtensiblePayload payload)
        {
            DataCache snapshot = system.StoreView;
            extensibleWitnessWhiteList ??= UpdateExtensibleWitnessWhiteList(system.Settings, snapshot);
            if (!payload.Verify(system.Settings, snapshot, extensibleWitnessWhiteList)) return VerifyResult.Invalid;
            system.RelayCache.Add(payload);
            return VerifyResult.Succeed;
        }

        private VerifyResult OnNewTransaction(Transaction transaction)
        {
            if (system.ContainsTransaction(transaction.Hash)) return VerifyResult.AlreadyExists;
            return system.MemPool.TryAdd(transaction, system.StoreView);
        }

        private void OnPreverifyCompleted(TransactionRouter.PreverifyCompleted task)
        {
            if (task.Result == VerifyResult.Succeed)
                OnInventory(task.Transaction, task.Relay);
            else
                SendRelayResult(task.Transaction, task.Result);
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Initialize:
                    OnInitialize();
                    break;
                case Import import:
                    OnImport(import.Blocks, import.Verify);
                    break;
                case FillMemoryPool fill:
                    OnFillMemoryPool(fill.Transactions);
                    break;
                case Header[] headers:
                    OnNewHeaders(headers);
                    break;
                case Block block:
                    OnInventory(block, false);
                    break;
                case Transaction tx:
                    OnTransaction(tx);
                    break;
                case IInventory inventory:
                    OnInventory(inventory);
                    break;
                case TransactionRouter.PreverifyCompleted task:
                    OnPreverifyCompleted(task);
                    break;
                case Reverify reverify:
                    foreach (IInventory inventory in reverify.Inventories)
                        OnInventory(inventory, false);
                    break;
                case Idle _:
                    if (system.MemPool.ReVerifyTopUnverifiedTransactionsIfNeeded(MaxTxToReverifyPerIdle, system.StoreView))
                        Self.Tell(Idle.Instance, ActorRefs.NoSender);
                    break;
            }
        }

        private void OnTransaction(Transaction tx)
        {
            if (system.ContainsTransaction(tx.Hash))
                SendRelayResult(tx, VerifyResult.AlreadyExists);
            else
                system.TxRouter.Forward(new TransactionRouter.Preverify(tx, true));
        }

        private void Persist(Block block)
        {
            using (SnapshotCache snapshot = system.GetSnapshot())
            {
                List<ApplicationExecuted> all_application_executed = new List<ApplicationExecuted>();
                using (ApplicationEngine engine = ApplicationEngine.Create(TriggerType.OnPersist, null, snapshot, block, system.Settings, 0))
                {
                    engine.LoadScript(onPersistScript);
                    if (engine.Execute() != VMState.HALT) throw new InvalidOperationException();
                    ApplicationExecuted application_executed = new ApplicationExecuted(engine);
                    Context.System.EventStream.Publish(application_executed);
                    all_application_executed.Add(application_executed);
                }
                bool isGasCalled = false;
                DataCache clonedSnapshot = snapshot.CreateSnapshot();
                // Warning: Do not write into variable snapshot directly. Write into variable clonedSnapshot and commit instead.
                foreach (Transaction tx in block.Transactions)
                {
                    using (ApplicationEngine engine = ApplicationEngine.Create(TriggerType.Application, tx, clonedSnapshot, block, system.Settings, tx.SystemFee))
                    {
                        engine.LoadScript(tx.Script);
                        if (engine.Execute() == VMState.HALT)
                        {
                            clonedSnapshot.Commit();
                            if (!isGasCalled && engine.InvokedContracts.Contains(NativeContract.GAS.Hash)) isGasCalled = true;
                        }
                        else
                        {
                            clonedSnapshot = snapshot.CreateSnapshot();
                        }
                        ApplicationExecuted application_executed = new ApplicationExecuted(engine);
                        Context.System.EventStream.Publish(application_executed);
                        all_application_executed.Add(application_executed);
                    }
                }
                using (ApplicationEngine engine = ApplicationEngine.Create(TriggerType.PostPersist, null, snapshot, block, system.Settings, 0))
                {
                    engine.LoadScript(postPersistScript);
                    if (engine.Execute() != VMState.HALT) throw new InvalidOperationException();
                    ApplicationExecuted application_executed = new ApplicationExecuted(engine);
                    Context.System.EventStream.Publish(application_executed);
                    all_application_executed.Add(application_executed);
                }
                foreach (IPersistencePlugin plugin in Plugin.PersistencePlugins)
                    plugin.OnPersist(system, block, snapshot, all_application_executed);
                snapshot.Commit();
                List<Exception> commitExceptions = null;
                foreach (IPersistencePlugin plugin in Plugin.PersistencePlugins)
                {
                    try
                    {
                        plugin.OnCommit(system, block, snapshot);
                    }
                    catch (Exception ex)
                    {
                        if (plugin.ShouldThrowExceptionFromCommit(ex))
                        {
                            if (commitExceptions == null)
                                commitExceptions = new List<Exception>();

                            commitExceptions.Add(ex);
                        }
                    }
                }
                if (commitExceptions != null) throw new AggregateException(commitExceptions);
                system.MemPool.UpdatePoolForBlockPersisted(block, snapshot, isGasCalled);
            }
            extensibleWitnessWhiteList = null;
            block_cache.Remove(block.PrevHash);
            Context.System.EventStream.Publish(new PersistCompleted { Block = block });
            if (system.HeaderCache.TryRemoveFirst(out Header header))
                Debug.Assert(header.Index == block.Index);
        }

        public static Props Props(NeoSystem system)
        {
            return Akka.Actor.Props.Create(() => new Blockchain(system)).WithMailbox("blockchain-mailbox");
        }

        private void SendRelayResult(IInventory inventory, VerifyResult result)
        {
            RelayResult rr = new RelayResult
            {
                Inventory = inventory,
                Result = result
            };
            Sender.Tell(rr);
            Context.System.EventStream.Publish(rr);
        }

        private static ImmutableHashSet<UInt160> UpdateExtensibleWitnessWhiteList(ProtocolSettings settings, DataCache snapshot)
        {
            uint currentHeight = NativeContract.Ledger.CurrentIndex(snapshot);
            var builder = ImmutableHashSet.CreateBuilder<UInt160>();
            builder.Add(NativeContract.NEO.GetCommitteeAddress(snapshot));
            var validators = NativeContract.NEO.GetNextBlockValidators(snapshot, settings.ValidatorsCount);
            builder.Add(Contract.GetBFTAddress(validators));
            builder.UnionWith(validators.Select(u => Contract.CreateSignatureRedeemScript(u).ToScriptHash()));
            var oracles = NativeContract.RoleManagement.GetDesignatedByRole(snapshot, Role.Oracle, currentHeight);
            if (oracles.Length > 0)
            {
                builder.Add(Contract.GetBFTAddress(oracles));
                builder.UnionWith(oracles.Select(u => Contract.CreateSignatureRedeemScript(u).ToScriptHash()));
            }
            var stateValidators = NativeContract.RoleManagement.GetDesignatedByRole(snapshot, Role.StateValidator, currentHeight);
            if (stateValidators.Length > 0)
            {
                builder.Add(Contract.GetBFTAddress(stateValidators));
                builder.UnionWith(stateValidators.Select(u => Contract.CreateSignatureRedeemScript(u).ToScriptHash()));
            }
            return builder.ToImmutable();
        }
    }

    internal class BlockchainMailbox : PriorityMailbox
    {
        public BlockchainMailbox(Akka.Actor.Settings settings, Config config)
            : base(settings, config)
        {
        }

        internal protected override bool IsHighPriority(object message)
        {
            switch (message)
            {
                case Header[] _:
                case Block _:
                case ExtensiblePayload _:
                case Terminated _:
                    return true;
                default:
                    return false;
            }
        }
    }
}
