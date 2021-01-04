using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Neo.Consensus
{
    public class ConsensusContext : IDisposable, ISerializable
    {
        /// <summary>
        /// Key for saving consensus state.
        /// </summary>
        private const byte ConsensusStatePrefix = 0xf4;

        public Block Block;
        public byte ViewNumber;
        public ECPoint[] Validators;
        public int MyIndex;
        public UInt256[] TransactionHashes;
        public Dictionary<UInt256, Transaction> Transactions;
        public ConsensusPayload[] PreparationPayloads;
        public ConsensusPayload[] CommitPayloads;
        public ConsensusPayload[] ChangeViewPayloads;
        public ConsensusPayload[] LastChangeViewPayloads;
        // LastSeenMessage array stores the height of the last seen message, for each validator.
        // if this node never heard from validator i, LastSeenMessage[i] will be -1.
        public Dictionary<ECPoint, uint> LastSeenMessage { get; private set; }

        /// <summary>
        /// Store all verified unsorted transactions' senders' fee currently in the consensus context.
        /// </summary>
        public TransactionVerificationContext VerificationContext = new TransactionVerificationContext();
        
        /// <summary>
        /// Future payloads used when CN are probably lagged
        /// Verified ConsensusPayloads are cached and the lastest height are stored
        /// They are possibly used when the nodes syncs to the last height
        /// </summary>
        public ConsensusPayload[] FuturePreparationPayloads;
        public ConsensusPayload[] FutureCommitPayloads;
        public ConsensusPayload[] FutureChangeViewPayloads;
        public ConsensusPayload[] FutureRecoveryPayloads;
        public uint CountFuturePayloads = 0;

        public SnapshotView Snapshot { get; private set; }
        private KeyPair keyPair;
        private int _witnessSize;
        private readonly Wallet wallet;
        private readonly IStore store;

        public int F => (Validators.Length - 1) / 3;
        public int M => Validators.Length - F;
        public bool IsPrimary => MyIndex == Block.ConsensusData.PrimaryIndex;
        public bool IsBackup => MyIndex >= 0 && MyIndex != Block.ConsensusData.PrimaryIndex;
        public bool WatchOnly => MyIndex < 0;
        public Header PrevHeader => Snapshot.GetHeader(Block.PrevHash);
        public int CountCommitted => CommitPayloads.Count(p => p != null);
        public int CountFailed
        {
            get
            {
                if (LastSeenMessage == null) return 0;
                return Validators.Count(p => !LastSeenMessage.TryGetValue(p, out var value) || value < (Block.Index - 1));
            }
        }
        public bool ValidatorsChanged
        {
            get
            {
                if (Snapshot.Height == 0) return false;
                TrimmedBlock currentBlock = Snapshot.Blocks[Snapshot.CurrentBlockHash];
                TrimmedBlock previousBlock = Snapshot.Blocks[currentBlock.PrevHash];
                return currentBlock.NextConsensus != previousBlock.NextConsensus;
            }
        }

        #region Consensus States
        public bool RequestSentOrReceived => PreparationPayloads[Block.ConsensusData.PrimaryIndex] != null;
        public bool ResponseSent => !WatchOnly && PreparationPayloads[MyIndex] != null;
        public bool CommitSent => !WatchOnly && CommitPayloads[MyIndex] != null;
        public bool BlockSent => Block.Transactions != null;
        public bool ViewChanging => !WatchOnly && ChangeViewPayloads[MyIndex]?.GetDeserializedMessage<ChangeView>().NewViewNumber > ViewNumber;
        public bool NotAcceptingPayloadsDueToViewChanging => ViewChanging && !MoreThanFNodesCommittedOrLost;
        // A possible attack can happen if the last node to commit is malicious and either sends change view after his
        // commit to stall nodes in a higher view, or if he refuses to send recovery messages. In addition, if a node
        // asking change views loses network or crashes and comes back when nodes are committed in more than one higher
        // numbered view, it is possible for the node accepting recovery to commit in any of the higher views, thus
        // potentially splitting nodes among views and stalling the network.
        public bool MoreThanFNodesCommittedOrLost => (CountCommitted + CountFailed) > F;
        #endregion

        public int Size => throw new NotImplementedException();

        public ConsensusContext(Wallet wallet, IStore store)
        {
            this.wallet = wallet;
            this.store = store;
        }

        public Block CreateBlock()
        {
            EnsureHeader();
            Contract contract = Contract.CreateMultiSigContract(M, Validators);
            ContractParametersContext sc = new ContractParametersContext(Block);
            for (int i = 0, j = 0; i < Validators.Length && j < M; i++)
            {
                if (CommitPayloads[i]?.ConsensusMessage.ViewNumber != ViewNumber) continue;
                sc.AddSignature(contract, Validators[i], CommitPayloads[i].GetDeserializedMessage<Commit>().Signature);
                j++;
            }
            Block.Witness = sc.GetWitnesses()[0];
            Block.Transactions = TransactionHashes.Select(p => Transactions[p]).ToArray();
            return Block;
        }

        public void Deserialize(BinaryReader reader)
        {
            Reset(0);
            if (reader.ReadUInt32() != Block.Version) throw new FormatException();
            if (reader.ReadUInt32() != Block.Index) throw new InvalidOperationException();
            Block.Timestamp = reader.ReadUInt64();
            Block.NextConsensus = reader.ReadSerializable<UInt160>();
            if (Block.NextConsensus.Equals(UInt160.Zero))
                Block.NextConsensus = null;
            Block.ConsensusData = reader.ReadSerializable<ConsensusData>();
            ViewNumber = reader.ReadByte();
            TransactionHashes = reader.ReadSerializableArray<UInt256>();
            Transaction[] transactions = reader.ReadSerializableArray<Transaction>(Block.MaxTransactionsPerBlock);
            PreparationPayloads = reader.ReadNullableArray<ConsensusPayload>(ProtocolSettings.Default.ValidatorsCount);
            CommitPayloads = reader.ReadNullableArray<ConsensusPayload>(ProtocolSettings.Default.ValidatorsCount);
            ChangeViewPayloads = reader.ReadNullableArray<ConsensusPayload>(ProtocolSettings.Default.ValidatorsCount);
            LastChangeViewPayloads = reader.ReadNullableArray<ConsensusPayload>(ProtocolSettings.Default.ValidatorsCount);
            if (TransactionHashes.Length == 0 && !RequestSentOrReceived)
                TransactionHashes = null;
            Transactions = transactions.Length == 0 && !RequestSentOrReceived ? null : transactions.ToDictionary(p => p.Hash);
            VerificationContext = new TransactionVerificationContext();
            if (Transactions != null)
            {
                foreach (Transaction tx in Transactions.Values)
                    VerificationContext.AddTransaction(tx);
            }
        }

        public void Dispose()
        {
            Snapshot?.Dispose();
        }

        public Block EnsureHeader()
        {
            if (TransactionHashes == null) return null;
            if (Block.MerkleRoot is null)
                Block.MerkleRoot = Block.CalculateMerkleRoot(Block.ConsensusData.Hash, TransactionHashes);
            return Block;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetPrimaryIndex(byte viewNumber)
        {
            int p = ((int)Block.Index - viewNumber) % Validators.Length;
            return p >= 0 ? (byte)p : (byte)(p + Validators.Length);
        }

        public bool Load()
        {
            byte[] data = store.TryGet(ConsensusStatePrefix, null);
            if (data is null || data.Length == 0) return false;
            using (MemoryStream ms = new MemoryStream(data, false))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                try
                {
                    Deserialize(reader);
                }
                catch
                {
                    return false;
                }
                return true;
            }
        }

        public ConsensusPayload MakeChangeView(ChangeViewReason reason)
        {
            return ChangeViewPayloads[MyIndex] = MakeSignedPayload(new ChangeView
            {
                Reason = reason,
                Timestamp = TimeProvider.Current.UtcNow.ToTimestampMS()
            });
        }

        public ConsensusPayload MakeCommit()
        {
            return CommitPayloads[MyIndex] ?? (CommitPayloads[MyIndex] = MakeSignedPayload(new Commit
            {
                Signature = EnsureHeader().Sign(keyPair)
            }));
        }

        private ConsensusPayload MakeSignedPayload(ConsensusMessage message)
        {
            message.ViewNumber = ViewNumber;
            ConsensusPayload payload = new ConsensusPayload
            {
                Version = Block.Version,
                PrevHash = Block.PrevHash,
                BlockIndex = Block.Index,
                ValidatorIndex = (byte)MyIndex,
                ConsensusMessage = message
            };
            SignPayload(payload);
            return payload;
        }

        private void SignPayload(ConsensusPayload payload)
        {
            ContractParametersContext sc;
            try
            {
                sc = new ContractParametersContext(payload);
                wallet.Sign(sc);
            }
            catch (InvalidOperationException)
            {
                return;
            }
            payload.Witness = sc.GetWitnesses()[0];
        }

        /// <summary>
        /// Return the expected block size
        /// </summary>
        internal int GetExpectedBlockSize()
        {
            return GetExpectedBlockSizeWithoutTransactions(Transactions.Count) + // Base size
                Transactions.Values.Sum(u => u.Size);   // Sum Txs
        }

        /// <summary>
        /// Return the expected block system fee
        /// </summary>
        internal long GetExpectedBlockSystemFee()
        {
            return Transactions.Values.Sum(u => u.SystemFee);  // Sum Txs
        }

        /// <summary>
        /// Return the expected block size without txs
        /// </summary>
        /// <param name="expectedTransactions">Expected transactions</param>
        internal int GetExpectedBlockSizeWithoutTransactions(int expectedTransactions)
        {
            var blockSize =
                // BlockBase
                sizeof(uint) +       //Version
                UInt256.Length +     //PrevHash
                UInt256.Length +     //MerkleRoot
                sizeof(ulong) +      //Timestamp
                sizeof(uint) +       //Index
                UInt160.Length +     //NextConsensus
                1 +                  //
                _witnessSize;        //Witness

            blockSize +=
                // Block
                Block.ConsensusData.Size +                      //ConsensusData
                IO.Helper.GetVarSize(expectedTransactions + 1); //Transactions count

            return blockSize;
        }

        /// <summary>
        /// Prevent that block exceed the max size
        /// </summary>
        /// <param name="txs">Ordered transactions</param>
        internal void EnsureMaxBlockLimitation(IEnumerable<Transaction> txs)
        {
            uint maxBlockSize = NativeContract.Policy.GetMaxBlockSize(Snapshot);
            long maxBlockSystemFee = NativeContract.Policy.GetMaxBlockSystemFee(Snapshot);
            uint maxTransactionsPerBlock = NativeContract.Policy.GetMaxTransactionsPerBlock(Snapshot);

            // Limit Speaker proposal to the limit `MaxTransactionsPerBlock` or all available transactions of the mempool
            txs = txs.Take((int)maxTransactionsPerBlock);
            List<UInt256> hashes = new List<UInt256>();
            Transactions = new Dictionary<UInt256, Transaction>();
            VerificationContext = new TransactionVerificationContext();

            // Expected block size
            var blockSize = GetExpectedBlockSizeWithoutTransactions(txs.Count());
            var blockSystemFee = 0L;

            // Iterate transaction until reach the size or maximum system fee
            foreach (Transaction tx in txs)
            {
                // Check if maximum block size has been already exceeded with the current selected set
                blockSize += tx.Size;
                if (blockSize > maxBlockSize) break;

                // Check if maximum block system fee has been already exceeded with the current selected set
                blockSystemFee += tx.SystemFee;
                if (blockSystemFee > maxBlockSystemFee) break;

                hashes.Add(tx.Hash);
                Transactions.Add(tx.Hash, tx);
                VerificationContext.AddTransaction(tx);
            }

            TransactionHashes = hashes.ToArray();
        }

        public ConsensusPayload MakePrepareRequest()
        {
            var random = new Random();
            Span<byte> buffer = stackalloc byte[sizeof(ulong)];
            random.NextBytes(buffer);
            Block.ConsensusData.Nonce = BitConverter.ToUInt64(buffer);
            EnsureMaxBlockLimitation(Blockchain.Singleton.MemPool.GetSortedVerifiedTransactions());
            Block.Timestamp = Math.Max(TimeProvider.Current.UtcNow.ToTimestampMS(), PrevHeader.Timestamp + 1);

            return PreparationPayloads[MyIndex] = MakeSignedPayload(new PrepareRequest
            {
                Timestamp = Block.Timestamp,
                Nonce = Block.ConsensusData.Nonce,
                TransactionHashes = TransactionHashes
            });
        }

        public ConsensusPayload MakeRecoveryRequest()
        {
            return MakeSignedPayload(new RecoveryRequest
            {
                Timestamp = TimeProvider.Current.UtcNow.ToTimestampMS()
            });
        }

        public ConsensusPayload MakeRecoveryMessage()
        {
            PrepareRequest prepareRequestMessage = null;
            if (TransactionHashes != null)
            {
                prepareRequestMessage = new PrepareRequest
                {
                    ViewNumber = ViewNumber,
                    Timestamp = Block.Timestamp,
                    Nonce = Block.ConsensusData.Nonce,
                    TransactionHashes = TransactionHashes
                };
            }
            return MakeSignedPayload(new RecoveryMessage()
            {
                ChangeViewMessages = LastChangeViewPayloads.Where(p => p != null).Select(p => RecoveryMessage.ChangeViewPayloadCompact.FromPayload(p)).Take(M).ToDictionary(p => (int)p.ValidatorIndex),
                PrepareRequestMessage = prepareRequestMessage,
                // We only need a PreparationHash set if we don't have the PrepareRequest information.
                PreparationHash = TransactionHashes == null ? PreparationPayloads.Where(p => p != null).GroupBy(p => p.GetDeserializedMessage<PrepareResponse>().PreparationHash, (k, g) => new { Hash = k, Count = g.Count() }).OrderByDescending(p => p.Count).Select(p => p.Hash).FirstOrDefault() : null,
                PreparationMessages = PreparationPayloads.Where(p => p != null).Select(p => RecoveryMessage.PreparationPayloadCompact.FromPayload(p)).ToDictionary(p => (int)p.ValidatorIndex),
                CommitMessages = CommitSent
                    ? CommitPayloads.Where(p => p != null).Select(p => RecoveryMessage.CommitPayloadCompact.FromPayload(p)).ToDictionary(p => (int)p.ValidatorIndex)
                    : new Dictionary<int, RecoveryMessage.CommitPayloadCompact>()
            });
        }

        public ConsensusPayload MakePrepareResponse()
        {
            return PreparationPayloads[MyIndex] = MakeSignedPayload(new PrepareResponse
            {
                PreparationHash = PreparationPayloads[Block.ConsensusData.PrimaryIndex].Hash
            });
        }

        public void Reset(byte viewNumber, bool resetFutures = true)
        {
            if (viewNumber == 0)
            {
                Snapshot?.Dispose();
                Snapshot = Blockchain.Singleton.GetSnapshot();
                Block = new Block
                {
                    PrevHash = Snapshot.CurrentBlockHash,
                    Index = Snapshot.Height + 1,
                    NextConsensus = Blockchain.GetConsensusAddress(
                        NativeContract.NEO.ShouldRefreshCommittee(Snapshot.Height + 1) ?
                        NativeContract.NEO.ComputeNextBlockValidators(Snapshot) :
                        NativeContract.NEO.GetNextBlockValidators(Snapshot))
                };
                var pv = Validators;
                Validators = NativeContract.NEO.GetNextBlockValidators(Snapshot);
                if (_witnessSize == 0 || (pv != null && pv.Length != Validators.Length))
                {
                    // Compute the expected size of the witness
                    using (ScriptBuilder sb = new ScriptBuilder())
                    {
                        for (int x = 0; x < M; x++)
                        {
                            sb.EmitPush(new byte[64]);
                        }
                        _witnessSize = new Witness
                        {
                            InvocationScript = sb.ToArray(),
                            VerificationScript = Contract.CreateMultiSigRedeemScript(M, Validators)
                        }.Size;
                    }
                }
                MyIndex = -1;
                ChangeViewPayloads = new ConsensusPayload[Validators.Length];
                LastChangeViewPayloads = new ConsensusPayload[Validators.Length];
                CommitPayloads = new ConsensusPayload[Validators.Length];
                if (ValidatorsChanged || LastSeenMessage is null)
                {
                    var previous_last_seen_message = LastSeenMessage;
                    LastSeenMessage = new Dictionary<ECPoint, uint>();
                    foreach (var validator in Validators)
                    {
                        if (previous_last_seen_message != null && previous_last_seen_message.TryGetValue(validator, out var value))
                            LastSeenMessage[validator] = value;
                        else
                            LastSeenMessage[validator] = Snapshot.Height;
                    }
                }
                keyPair = null;
                for (int i = 0; i < Validators.Length; i++)
                {
                    WalletAccount account = wallet?.GetAccount(Validators[i]);
                    if (account?.HasKey != true) continue;
                    MyIndex = i;
                    keyPair = account.GetKey();
                    break;
                }
            }
            else
            {
                for (int i = 0; i < LastChangeViewPayloads.Length; i++)
                    if (ChangeViewPayloads[i]?.GetDeserializedMessage<ChangeView>().NewViewNumber >= viewNumber)
                        LastChangeViewPayloads[i] = ChangeViewPayloads[i];
                    else
                        LastChangeViewPayloads[i] = null;
            }
            ViewNumber = viewNumber;
            Block.ConsensusData = new ConsensusData
            {
                PrimaryIndex = GetPrimaryIndex(viewNumber)
            };
            Block.MerkleRoot = null;
            Block.Timestamp = 0;
            Block.Transactions = null;
            TransactionHashes = null;
            PreparationPayloads = new ConsensusPayload[Validators.Length];

            if (MyIndex >= 0) LastSeenMessage[Validators[MyIndex]] = Block.Index;

            if (resetFutures || (FutureCommitPayloads.Count() != Validators.Length))
                ResetFuturePayloads();
        }

        public void ResetFuturePayloads()
        {
            FutureCommitPayloads = new ConsensusPayload[Validators.Length];
            FuturePreparationPayloads = new ConsensusPayload[Validators.Length];
            FutureChangeViewPayloads = new ConsensusPayload[Validators.Length];
            FutureRecoveryPayloads = new ConsensusPayload[Validators.Length];
            CountFuturePayloads = 0;
        }

        public bool HasFuturePayloads()
        {
            return CountFuturePayloads > 0;
        }

        public void Save()
        {
            store.PutSync(ConsensusStatePrefix, null, this.ToArray());
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Block.Version);
            writer.Write(Block.Index);
            writer.Write(Block.Timestamp);
            writer.Write(Block.NextConsensus ?? UInt160.Zero);
            writer.Write(Block.ConsensusData);
            writer.Write(ViewNumber);
            writer.Write(TransactionHashes ?? new UInt256[0]);
            writer.Write(Transactions?.Values.ToArray() ?? new Transaction[0]);
            writer.WriteNullableArray(PreparationPayloads);
            writer.WriteNullableArray(CommitPayloads);
            writer.WriteNullableArray(ChangeViewPayloads);
            writer.WriteNullableArray(LastChangeViewPayloads);
        }
    }
}
