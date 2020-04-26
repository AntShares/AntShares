using Akka.Actor;
using Akka.Configuration;
using Neo.IO.Actors;
using Neo.IO.Caching;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Neo.Network.P2P
{
    internal partial class TaskManager : UntypedActor
    {
        public class Register { public RemoteNode Node; }
        public class NewTasks { public InvPayload Payload; }
        public class TaskCompleted { public UInt256 Hash; }
        public class RestartTasks { public InvPayload Payload; }
        private class Timer { }

        private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(1);

        private readonly NeoSystem system;
        private const int MaxConncurrentTasks = 3;

        /// <summary>
        /// A set of known hashes, of inventories or payloads, already received.
        /// </summary>        
        private readonly HashSetCache<UInt256> knownHashes;
        private readonly Dictionary<UInt256, int> globalTasks = new Dictionary<UInt256, int>();
        private readonly Dictionary<IActorRef, RemoteNode> nodes = new Dictionary<IActorRef, RemoteNode>();
        private readonly ICancelable timer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(TimerInterval, TimerInterval, Context.Self, new Timer(), ActorRefs.NoSender);

        public TaskManager(NeoSystem system)
        {
            this.system = system;
            this.knownHashes = new HashSetCache<UInt256>(Blockchain.Singleton.MemPool.Capacity * 2 / 5);
            this.lastTaskIndex = Blockchain.Singleton.Height;
            Context.System.EventStream.Subscribe(Self, typeof(Blockchain.PersistCompleted));
        }

        private void OnNewTasks(InvPayload payload)
        {
            if (!nodes.TryGetValue(Sender, out RemoteNode remoteNode))
                return;
            // Do not accept payload of type InventoryType.TX if not synced on best known HeaderHeight
            if (payload.Type == InventoryType.TX && Blockchain.Singleton.Height < nodes.Values.Max(p => p.LastBlockIndex))
                return;
            HashSet<UInt256> hashes = new HashSet<UInt256>(payload.Hashes);
            // Remove all previously processed knownHashes from the list that is being requested
            hashes.Remove(knownHashes);

            // Remove those that are already in process by other sessions
            hashes.Remove(globalTasks);
            if (hashes.Count == 0)
                return;

            // Update globalTasks with the ones that will be requested within this current session
            foreach (UInt256 hash in hashes)
            {
                IncrementGlobalTask(hash);
                remoteNode.session.InvTasks[hash] = DateTime.UtcNow;
            }

            foreach (InvPayload group in InvPayload.CreateGroup(payload.Type, hashes.ToArray()))
                Sender.Tell(Message.Create(MessageCommand.GetData, group));
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Register register:
                    OnRegister(register.Node);
                    break;
                case NewTasks tasks:
                    OnNewTasks(tasks.Payload);
                    break;
                case TaskCompleted completed:
                    OnTaskCompleted(completed.Hash);
                    break;
                case RestartTasks restart:
                    OnRestartTasks(restart.Payload);
                    break;
                case Block block:
                    OnReceiveBlock(block);
                    break;
                case Blockchain.PersistCompleted persistBlock:
                    OnReceivePersistedBlockIndex(persistBlock.Block.Index);
                    break;
                case Blockchain.RelayResult rr:
                    if (rr.Inventory is Block invalidBlock && rr.Result == VerifyResult.Invalid)
                        OnReceiveInvalidBlockIndex(invalidBlock.Index);
                    break;
                case StartSync _:
                    RequestSync();
                    break;
                case Timer _:
                    OnTimer();
                    break;
                case Terminated terminated:
                    OnTerminated(terminated.ActorRef);
                    break;
            }
        }

        private void OnRegister(RemoteNode node)
        {
            Context.Watch(Sender);
            nodes.Add(Sender, node);
            RequestSync();
        }

        private void OnRestartTasks(InvPayload payload)
        {
            knownHashes.ExceptWith(payload.Hashes);
            foreach (UInt256 hash in payload.Hashes)
                globalTasks.Remove(hash);
            foreach (InvPayload group in InvPayload.CreateGroup(payload.Type, payload.Hashes))
                system.LocalNode.Tell(Message.Create(MessageCommand.GetData, group));
        }

        private void OnTaskCompleted(UInt256 hash)
        {
            knownHashes.Add(hash);
            globalTasks.Remove(hash);
            if (nodes.TryGetValue(Sender, out RemoteNode remoteNode))
                remoteNode.session.InvTasks.Remove(hash);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DecrementGlobalTask(UInt256 hash)
        {
            if (globalTasks.TryGetValue(hash, out var value))
            {
                if (value == 1)
                    globalTasks.Remove(hash);
                else
                    globalTasks[hash] = value - 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IncrementGlobalTask(UInt256 hash)
        {
            if (!globalTasks.TryGetValue(hash, out var value))
            {
                globalTasks[hash] = 1;
                return true;
            }
            if (value >= MaxConncurrentTasks)
                return false;

            globalTasks[hash] = value + 1;

            return true;
        }

        private void OnTerminated(IActorRef actor)
        {
            if (!nodes.TryGetValue(actor, out RemoteNode remoteNode))
                return;
            NodeSession session = remoteNode.session;
            foreach (uint index in session.IndexTasks.Keys)
                AssignTask(index, session);

            foreach (UInt256 hash in session.InvTasks.Keys)
                DecrementGlobalTask(hash);
            nodes.Remove(actor);
        }

        private void OnTimer()
        {
            foreach (RemoteNode node in nodes.Values)
            {
                NodeSession session = node.session;
                foreach (var task in session.InvTasks.ToArray())
                {
                    if (DateTime.UtcNow - task.Value > TaskTimeout)
                    {
                        if (session.InvTasks.Remove(task.Key))
                            DecrementGlobalTask(task.Key);
                    }
                }

                foreach (KeyValuePair<uint, DateTime> kvp in session.IndexTasks)
                {
                    if (TimeProvider.Current.UtcNow - kvp.Value > TaskTimeout)
                    {
                        session.IndexTasks.Remove(kvp.Key);
                        session.TimeoutTimes++;
                        AssignTask(kvp.Key, session);
                    }
                }
            }
            RequestSync();
        }

        protected override void PostStop()
        {
            timer.CancelIfNotNull();
            base.PostStop();
        }

        public static Props Props(NeoSystem system)
        {
            return Akka.Actor.Props.Create(() => new TaskManager(system)).WithMailbox("task-manager-mailbox");
        }
    }

    internal class TaskManagerMailbox : PriorityMailbox
    {
        public TaskManagerMailbox(Akka.Actor.Settings settings, Config config)
            : base(settings, config)
        {
        }

        internal protected override bool IsHighPriority(object message)
        {
            switch (message)
            {
                case TaskManager.Register _:
                case TaskManager.RestartTasks _:
                    return true;
                case TaskManager.NewTasks tasks:
                    if (tasks.Payload.Type == InventoryType.Consensus)
                        return true;
                    return false;
                default:
                    return false;
            }
        }

        internal protected override bool ShallDrop(object message, IEnumerable queue)
        {
            if (!(message is TaskManager.NewTasks tasks)) return false;
            // Remove duplicate tasks
            if (queue.OfType<TaskManager.NewTasks>().Any(x => x.Payload.Type == tasks.Payload.Type && x.Payload.Hashes.SequenceEqual(tasks.Payload.Hashes))) return true;
            return false;
        }
    }
}
