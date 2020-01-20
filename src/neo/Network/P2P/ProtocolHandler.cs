using Akka.Actor;
using Akka.Configuration;
using Neo.Cryptography;
using Neo.IO;
using Neo.IO.Actors;
using Neo.IO.Caching;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;

namespace Neo.Network.P2P
{
    internal class ProtocolHandler : UntypedActor
    {
        public class SetFilter { public BloomFilter Filter; }
        internal class Timer { }

        private class PendingKnownHashesCollection : KeyedCollection<UInt256, (UInt256, DateTime)>
        {
            protected override UInt256 GetKeyForItem((UInt256, DateTime) item)
            {
                return item.Item1;
            }
        }

        private readonly NeoSystem system;
        private readonly PendingKnownHashesCollection pendingKnownHashes;
        private readonly HashSetCache<UInt256> knownHashes;
        private readonly HashSetCache<UInt256> sentHashes;
        private VersionPayload version;
        private bool verack = false;
        private BloomFilter bloom_filter;

        private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(1);

        private readonly ICancelable timer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(TimerInterval, TimerInterval, Context.Self, new Timer(), ActorRefs.NoSender);

        public ProtocolHandler(NeoSystem system)
        {
            this.system = system;
            this.pendingKnownHashes = new PendingKnownHashesCollection();
            this.knownHashes = new HashSetCache<UInt256>(Blockchain.Singleton.MemPool.Capacity * 2 / 5);
            this.sentHashes = new HashSetCache<UInt256>(Blockchain.Singleton.MemPool.Capacity * 2 / 5);
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Message msg:
                    OnMessage(msg);
                    break;
                case Timer _:
                    OnTimer();
                    break;
            }
        }

        private void OnMessage(Message msg)
        {
            foreach (IP2PPlugin plugin in Plugin.P2PPlugins)
                if (!plugin.OnP2PMessage(msg))
                    return;
            if (version == null)
            {
                if (msg.Command != MessageCommand.Version)
                    throw new ProtocolViolationException();
                OnVersionMessageReceived((VersionPayload)msg.Payload);
                return;
            }
            if (!verack)
            {
                if (msg.Command != MessageCommand.Verack)
                    throw new ProtocolViolationException();
                OnVerackMessageReceived();
                return;
            }
            switch (msg.Command)
            {
                case MessageCommand.Addr:
                    OnAddrMessageReceived((AddrPayload)msg.Payload);
                    break;
                case MessageCommand.Block:
                    OnInventoryReceived((Block)msg.Payload);
                    break;
                case MessageCommand.Consensus:
                    OnInventoryReceived((ConsensusPayload)msg.Payload);
                    break;
                case MessageCommand.FilterAdd:
                    OnFilterAddMessageReceived((FilterAddPayload)msg.Payload);
                    break;
                case MessageCommand.FilterClear:
                    OnFilterClearMessageReceived();
                    break;
                case MessageCommand.FilterLoad:
                    OnFilterLoadMessageReceived((FilterLoadPayload)msg.Payload);
                    break;
                case MessageCommand.GetAddr:
                    OnGetAddrMessageReceived();
                    break;
                case MessageCommand.GetBlocks:
                    OnGetBlocksMessageReceived((GetBlocksPayload)msg.Payload);
                    break;
                case MessageCommand.GetBlockData:
                    OnGetBlockDataMessageReceived((GetBlockDataPayload)msg.Payload);
                    break;
                case MessageCommand.GetData:
                    OnGetDataMessageReceived((InvPayload)msg.Payload);
                    break;
                case MessageCommand.GetHeaders:
                    OnGetHeadersMessageReceived((GetBlocksPayload)msg.Payload);
                    break;
                case MessageCommand.Headers:
                    OnHeadersMessageReceived((HeadersPayload)msg.Payload);
                    break;
                case MessageCommand.Inv:
                    OnInvMessageReceived((InvPayload)msg.Payload);
                    break;
                case MessageCommand.Mempool:
                    OnMemPoolMessageReceived();
                    break;
                case MessageCommand.Ping:
                    OnPingMessageReceived((PingPayload)msg.Payload);
                    break;
                case MessageCommand.Pong:
                    OnPongMessageReceived((PingPayload)msg.Payload);
                    break;
                case MessageCommand.Transaction:
                    if (msg.Payload.Size <= Transaction.MaxTransactionSize)
                        OnInventoryReceived((Transaction)msg.Payload);
                    break;
                case MessageCommand.Verack:
                case MessageCommand.Version:
                    throw new ProtocolViolationException();
                case MessageCommand.Alert:
                case MessageCommand.MerkleBlock:
                case MessageCommand.NotFound:
                case MessageCommand.Reject:
                default: break;
            }
        }

        private void OnAddrMessageReceived(AddrPayload payload)
        {
            system.LocalNode.Tell(new Peer.Peers
            {
                EndPoints = payload.AddressList.Select(p => p.EndPoint).Where(p => p.Port > 0)
            });
        }

        private void OnFilterAddMessageReceived(FilterAddPayload payload)
        {
            if (bloom_filter != null)
                bloom_filter.Add(payload.Data);
        }

        private void OnFilterClearMessageReceived()
        {
            bloom_filter = null;
            Context.Parent.Tell(new SetFilter { Filter = null });
        }

        private void OnFilterLoadMessageReceived(FilterLoadPayload payload)
        {
            bloom_filter = new BloomFilter(payload.Filter.Length * 8, payload.K, payload.Tweak, payload.Filter);
            Context.Parent.Tell(new SetFilter { Filter = bloom_filter });
        }

        /// <summary>
        /// Will be triggered when a MessageCommand.GetAddr message is received.
        /// Randomly select nodes from the local RemoteNodes and tells to RemoteNode actors a MessageCommand.Addr message.
        /// The message contains a list of networkAddresses from those selected random peers.
        /// </summary>
        private void OnGetAddrMessageReceived()
        {
            Random rand = new Random();
            IEnumerable<RemoteNode> peers = LocalNode.Singleton.RemoteNodes.Values
                .Where(p => p.ListenerTcpPort > 0)
                .GroupBy(p => p.Remote.Address, (k, g) => g.First())
                .OrderBy(p => rand.Next())
                .Take(AddrPayload.MaxCountToSend);
            NetworkAddressWithTime[] networkAddresses = peers.Select(p => NetworkAddressWithTime.Create(p.Listener.Address, p.Version.Timestamp, p.Version.Capabilities)).ToArray();
            if (networkAddresses.Length == 0) return;
            Context.Parent.Tell(Message.Create(MessageCommand.Addr, AddrPayload.Create(networkAddresses)));
        }

        /// <summary>
        /// Will be triggered when a MessageCommand.GetBlocks message is received.
        /// Tell the specified number of blocks' hashes starting with the requested HashStart until payload.Count or MaxHashesCount
        /// Responses are sent to RemoteNode actor as MessageCommand.Inv Message.
        /// </summary>
        /// <param name="payload">A GetBlocksPayload including start block Hash and number of blocks requested.</param>
        private void OnGetBlocksMessageReceived(GetBlocksPayload payload)
        {
            UInt256 hash = payload.HashStart;
            // The default value of payload.Count is -1
            int count = payload.Count < 0 || payload.Count > InvPayload.MaxHashesCount ? InvPayload.MaxHashesCount : payload.Count;
            TrimmedBlock state = Blockchain.Singleton.View.Blocks.TryGet(hash);
            if (state == null) return;
            List<UInt256> hashes = new List<UInt256>();
            for (uint i = 1; i <= count; i++)
            {
                uint index = state.Index + i;
                if (index > Blockchain.Singleton.Height)
                    break;
                hash = Blockchain.Singleton.GetBlockHash(index);
                if (hash == null) break;
                hashes.Add(hash);
            }
            if (hashes.Count == 0) return;
            Context.Parent.Tell(Message.Create(MessageCommand.Inv, InvPayload.Create(InventoryType.Block, hashes.ToArray())));
        }

        private void OnGetBlockDataMessageReceived(GetBlockDataPayload payload)
        {
            for (uint i = payload.IndexStart, max = payload.IndexStart + payload.Count; i < max; i++)
            {
                Block block = Blockchain.Singleton.GetBlock(i);
                if (block == null)
                    break;

                if (bloom_filter == null)
                {
                    Context.Parent.Tell(Message.Create(MessageCommand.Block, block));
                }
                else
                {
                    BitArray flags = new BitArray(block.Transactions.Select(p => bloom_filter.Test(p)).ToArray());
                    Context.Parent.Tell(Message.Create(MessageCommand.MerkleBlock, MerkleBlockPayload.Create(block, flags)));
                }
            }
        }

        /// <summary>
        /// Will be triggered when a MessageCommand.GetData message is received.
        /// The payload includes an array of hash values.
        /// For different payload.Type (Tx, Block, Consensus), get the corresponding (Txs, Blocks, Consensus) and tell them to RemoteNode actor.
        /// </summary>
        /// <param name="payload">The payload containing the requested information.</param>
        private void OnGetDataMessageReceived(InvPayload payload)
        {
            UInt256[] hashes = payload.Hashes.Where(p => sentHashes.Add(p)).ToArray();
            foreach (UInt256 hash in hashes)
            {
                switch (payload.Type)
                {
                    case InventoryType.TX:
                        Transaction tx = Blockchain.Singleton.GetTransaction(hash);
                        if (tx != null)
                            Context.Parent.Tell(Message.Create(MessageCommand.Transaction, tx));
                        break;
                    case InventoryType.Block:
                        Block block = Blockchain.Singleton.GetBlock(hash);
                        if (block != null)
                        {
                            if (bloom_filter == null)
                            {
                                Context.Parent.Tell(Message.Create(MessageCommand.Block, block));
                            }
                            else
                            {
                                BitArray flags = new BitArray(block.Transactions.Select(p => bloom_filter.Test(p)).ToArray());
                                Context.Parent.Tell(Message.Create(MessageCommand.MerkleBlock, MerkleBlockPayload.Create(block, flags)));
                            }
                        }
                        break;
                    case InventoryType.Consensus:
                        if (Blockchain.Singleton.ConsensusRelayCache.TryGet(hash, out IInventory inventoryConsensus))
                            Context.Parent.Tell(Message.Create(MessageCommand.Consensus, inventoryConsensus));
                        break;
                }
            }
        }

        /// <summary>
        /// Will be triggered when a MessageCommand.GetHeaders message is received.
        /// Tell the specified number of blocks' headers starting with the requested HashStart to RemoteNode actor.
        /// A limit set by HeadersPayload.MaxHeadersCount is also applied to the number of requested Headers, namely payload.Count.
        /// </summary>
        /// <param name="payload">A GetBlocksPayload including start block Hash and number of blocks' headers requested.</param>
        private void OnGetHeadersMessageReceived(GetBlocksPayload payload)
        {
            UInt256 hash = payload.HashStart;
            int count = payload.Count < 0 || payload.Count > HeadersPayload.MaxHeadersCount ? HeadersPayload.MaxHeadersCount : payload.Count;
            DataCache<UInt256, TrimmedBlock> cache = Blockchain.Singleton.View.Blocks;
            TrimmedBlock state = cache.TryGet(hash);
            if (state == null) return;
            List<Header> headers = new List<Header>();
            for (uint i = 1; i <= count; i++)
            {
                uint index = state.Index + i;
                hash = Blockchain.Singleton.GetBlockHash(index);
                if (hash == null) break;
                Header header = cache.TryGet(hash)?.Header;
                if (header == null) break;
                headers.Add(header);
            }
            if (headers.Count == 0) return;
            Context.Parent.Tell(Message.Create(MessageCommand.Headers, HeadersPayload.Create(headers.ToArray())));
        }

        private void OnHeadersMessageReceived(HeadersPayload payload)
        {
            if (payload.Headers.Length == 0) return;
            system.Blockchain.Tell(payload.Headers, Context.Parent);
        }

        private void OnInventoryReceived(IInventory inventory)
        {
            system.TaskManager.Tell(new TaskManager.TaskCompleted { Hash = inventory.Hash }, Context.Parent);
            system.LocalNode.Tell(new LocalNode.Relay { Inventory = inventory });
            pendingKnownHashes.Remove(inventory.Hash);
            knownHashes.Add(inventory.Hash);
        }

        private void OnInvMessageReceived(InvPayload payload)
        {
            UInt256[] hashes = payload.Hashes.Where(p => !pendingKnownHashes.Contains(p) && !knownHashes.Contains(p) && !sentHashes.Contains(p)).ToArray();
            if (hashes.Length == 0) return;
            switch (payload.Type)
            {
                case InventoryType.Block:
                    using (SnapshotView snapshot = Blockchain.Singleton.GetSnapshot())
                        hashes = hashes.Where(p => !snapshot.ContainsBlock(p)).ToArray();
                    break;
                case InventoryType.TX:
                    using (SnapshotView snapshot = Blockchain.Singleton.GetSnapshot())
                        hashes = hashes.Where(p => !snapshot.ContainsTransaction(p)).ToArray();
                    break;
            }
            if (hashes.Length == 0) return;
            foreach (UInt256 hash in hashes)
                pendingKnownHashes.Add((hash, DateTime.UtcNow));
            system.TaskManager.Tell(new TaskManager.NewTasks { Payload = InvPayload.Create(payload.Type, hashes) }, Context.Parent);
        }

        private void OnMemPoolMessageReceived()
        {
            foreach (InvPayload payload in InvPayload.CreateGroup(InventoryType.TX, Blockchain.Singleton.MemPool.GetVerifiedTransactions().Select(p => p.Hash).ToArray()))
                Context.Parent.Tell(Message.Create(MessageCommand.Inv, payload));
        }

        private void OnPingMessageReceived(PingPayload payload)
        {
            Context.Parent.Tell(payload);
            Context.Parent.Tell(Message.Create(MessageCommand.Pong, PingPayload.Create(Blockchain.Singleton.Height, payload.Nonce)));
        }

        private void OnPongMessageReceived(PingPayload payload)
        {
            Context.Parent.Tell(payload);
        }

        private void OnVerackMessageReceived()
        {
            verack = true;
            Context.Parent.Tell(MessageCommand.Verack);
        }

        private void OnVersionMessageReceived(VersionPayload payload)
        {
            version = payload;
            Context.Parent.Tell(payload);
        }

        private void OnTimer()
        {
            RefreshPendingKnownHashes();
        }

        protected override void PostStop()
        {
            timer.CancelIfNotNull();
            base.PostStop();
        }

        private void RefreshPendingKnownHashes()
        {
            while (pendingKnownHashes.Count > 0)
            {
                var (_, time) = pendingKnownHashes[0];
                if (DateTime.UtcNow - time <= PendingTimeout)
                    break;
                pendingKnownHashes.RemoveAt(0);
            }
        }

        public static Props Props(NeoSystem system)
        {
            return Akka.Actor.Props.Create(() => new ProtocolHandler(system)).WithMailbox("protocol-handler-mailbox");
        }
    }

    internal class ProtocolHandlerMailbox : PriorityMailbox
    {
        public ProtocolHandlerMailbox(Settings settings, Config config)
            : base(settings, config)
        {
        }

        internal protected override bool IsHighPriority(object message)
        {
            if (!(message is Message msg)) return false;
            switch (msg.Command)
            {
                case MessageCommand.Consensus:
                case MessageCommand.FilterAdd:
                case MessageCommand.FilterClear:
                case MessageCommand.FilterLoad:
                case MessageCommand.Verack:
                case MessageCommand.Version:
                case MessageCommand.Alert:
                    return true;
                default:
                    return false;
            }
        }

        internal protected override bool ShallDrop(object message, IEnumerable queue)
        {
            if (message is ProtocolHandler.Timer) return false;
            if (!(message is Message msg)) return true;
            switch (msg.Command)
            {
                case MessageCommand.GetAddr:
                case MessageCommand.GetBlocks:
                case MessageCommand.GetHeaders:
                case MessageCommand.Mempool:
                    return queue.OfType<Message>().Any(p => p.Command == msg.Command);
                default:
                    return false;
            }
        }
    }
}
