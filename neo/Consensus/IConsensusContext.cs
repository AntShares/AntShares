using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using System;
using System.Collections.Generic;

namespace Neo.Consensus
{
    public interface IConsensusContext : IDisposable, ISerializable
    {
        //public const uint Version = 0;
        UInt256 PrevHash { get; }
        uint BlockIndex { get; }
        byte ViewNumber { get; }
        ECPoint[] Validators { get; }
        int MyIndex { get; }
        uint PrimaryIndex { get; }
        uint Timestamp { get; set; }
        ulong Nonce { get; set; }
        UInt160 NextConsensus { get; set; }
        UInt256[] TransactionHashes { get; set; }
        Dictionary<UInt256, Transaction> Transactions { get; set; }
        ConsensusPayload[] PreparationPayloads { get; set; }
        ConsensusPayload[] CommitPayloads { get; set; }
        ConsensusPayload[] ChangeViewPayloads { get; set; }
        Block Block { get; set; }
        Snapshot Snapshot { get; }
        Store Store { get; }
        bool IsRecovering { get; set; }

        Block CreateBlock();

        //void Dispose();

        ConsensusPayload MakeChangeView(byte newViewNumber);

        ConsensusPayload MakeCommit();

        Block MakeHeader();

        ConsensusPayload MakePrepareRequest();

        ConsensusPayload MakeRecoveryMessage();

        ConsensusPayload MakePrepareResponse();

        void Reset(byte viewNumber);
    }
}
