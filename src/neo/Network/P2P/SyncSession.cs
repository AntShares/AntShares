using Akka.Actor;
using Neo.Network.P2P.Capabilities;
using Neo.Network.P2P.Payloads;
using System.Collections.Generic;
using System.Linq;
using static Neo.Network.P2P.SyncManager;

namespace Neo.Network.P2P
{
    internal class SyncSession
    {
        public readonly IActorRef RemoteNode;
        public readonly VersionPayload Version;
        public List<Task> Tasks;
        public uint TimeoutTimes = 0;
        public uint InvalidBlockCount = 0;

        public bool HasTask => Tasks != null;
        public uint StartHeight { get; }
        public uint LastBlockIndex { get; set; }

        public SyncSession(IActorRef node, VersionPayload version)
        {
            this.RemoteNode = node;
            this.Version = version;
            this.StartHeight = version.Capabilities
                .OfType<FullNodeCapability>()
                .FirstOrDefault()?.StartHeight ?? 0;
            this.LastBlockIndex = this.StartHeight;
            this.Tasks = new List<Task>();
        }
    }
}
