using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Neo.Consensus;
using Neo.Cryptography;
using Neo.UnitTests.Cryptography;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using ECPoint = Neo.Cryptography.ECC.ECPoint;

namespace Neo.UnitTests.Consensus
{
    [TestClass]
    public class ConsensusTests : TestKit
    {
        [TestInitialize]
        public void TestSetup()
        {
            TestBlockchain.InitializeMockNeoSystem();
        }

        [TestCleanup]
        public void Cleanup()
        {
            Shutdown();
        }

        [TestMethod]
        public void ConsensusService_SingleNodeActors_OnStart_PrepReq_PrepResponses_Commits()
        {
            var mockWallet = new Mock<Wallet>();
            mockWallet.Setup(p => p.GetAccount(It.IsAny<UInt160>())).Returns<UInt160>(p => new TestWalletAccount(p));
            Console.WriteLine($"\n(UT-Consensus) Wallet is: {mockWallet.Object.GetAccount(UInt160.Zero).GetKey().PublicKey}");

            var mockContext = new Mock<ConsensusContext>(mockWallet.Object, Blockchain.Singleton.Store);
            mockContext.Object.LastSeenMessage = new int[] { 0, 0, 0, 0, 0, 0, 0 };

            var timeValues = new[] {
              new DateTime(1980, 06, 01, 0, 0, 1, 001, DateTimeKind.Utc),  // For tests, used below
              new DateTime(1980, 06, 01, 0, 0, 3, 001, DateTimeKind.Utc),  // For receiving block
              new DateTime(1980, 05, 01, 0, 0, 5, 001, DateTimeKind.Utc), // For Initialize
              new DateTime(1980, 06, 01, 0, 0, 15, 001, DateTimeKind.Utc), // unused
            };
            for (int i = 0; i < timeValues.Length; i++)
                Console.WriteLine($"time {i}: {timeValues[i].ToString()} ");

            int timeIndex = 0;
            var timeMock = new Mock<TimeProvider>();
            timeMock.SetupGet(tp => tp.UtcNow).Returns(() => timeValues[timeIndex]);
            //.Callback(() => timeIndex = timeIndex + 1); //Comment while index is not fixed

            TimeProvider.Current = timeMock.Object;
            TimeProvider.Current.UtcNow.ToTimestampMS().Should().Be(328665601001); //1980-06-01 00:00:15:001

            //public void Log(string message, LogLevel level)
            //create ILogPlugin for Tests
            /*
            mockConsensusContext.Setup(mr => mr.Log(It.IsAny<string>(), It.IsAny<LogLevel>()))
                         .Callback((string message, LogLevel level) => {
                                         Console.WriteLine($"CONSENSUS LOG: {message}");
                                                                   }
                                  );
             */

            // Creating a test block
            Header header = new Header();
            TestUtils.SetupHeaderWithValues(header, UInt256.Zero, out UInt256 merkRootVal, out UInt160 val160, out ulong timestampVal, out uint indexVal, out Witness scriptVal);
            header.Size.Should().Be(105);
            Console.WriteLine($"header {header} hash {header.Hash} {header.PrevHash} timestamp {timestampVal}");
            timestampVal.Should().Be(328665601001);    // GMT: Sunday, June 1, 1980 12:00:01.001 AM
                                                       // check basic ConsensusContext
                                                       // mockConsensusContext.Object.block_received_time.ToTimestamp().Should().Be(4244941697); //1968-06-01 00:00:01
                                                       // ============================================================================
                                                       //                      creating ConsensusService actor
                                                       // ============================================================================
            TestProbe subscriber = CreateTestProbe();
            TestActorRef<ConsensusService> actorConsensus = ActorOfAsTestActorRef<ConsensusService>(
                                     Akka.Actor.Props.Create(() => (ConsensusService)Activator.CreateInstance(typeof(ConsensusService), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { subscriber, subscriber, mockContext.Object }, null))
                                     );

            var testPersistCompleted = new Blockchain.PersistCompleted
            {
                Block = new Block
                {
                    Version = header.Version,
                    PrevHash = header.PrevHash,
                    MerkleRoot = header.MerkleRoot,
                    Timestamp = header.Timestamp,
                    Index = header.Index,
                    NextConsensus = header.NextConsensus,
                    Transactions = new Transaction[0]
                }
            };
            Console.WriteLine("\n==========================");
            Console.WriteLine("Telling a new block to actor consensus...");
            Console.WriteLine("will trigger OnPersistCompleted without OnStart flag!");
            // OnPersist will not launch timer, we need OnStart
            actorConsensus.Tell(testPersistCompleted);
            Console.WriteLine("\n==========================");

            Console.WriteLine("\n==========================");
            Console.WriteLine("will start consensus!");
            actorConsensus.Tell(new ConsensusService.Start
            {
                IgnoreRecoveryLogs = true
            });

            Console.WriteLine("Waiting for subscriber recovery message...");
            // The next line force a waits, then, subscriber keeps running its thread
            // In the next case it waits for a Msg of type LocalNode.SendDirectly
            // As we may expect, as soon as consensus start it sends a RecoveryRequest of this aforementioned type
            var askingForInitialRecovery = subscriber.ExpectMsg<LocalNode.SendDirectly>();
            Console.WriteLine($"Recovery Message I: {askingForInitialRecovery}");
            // Ensuring cast of type ConsensusPayload from the received message from subscriber
            ConsensusPayload initialRecoveryPayload = (ConsensusPayload)askingForInitialRecovery.Inventory;
            // Ensuring casting of type RecoveryRequest
            RecoveryRequest rrm = (RecoveryRequest)initialRecoveryPayload.ConsensusMessage;
            rrm.Timestamp.Should().Be(328665601001);

            Console.WriteLine("Waiting for backupChange View... ");
            var backupOnAskingChangeView = subscriber.ExpectMsg<LocalNode.SendDirectly>();
            var changeViewPayload = (ConsensusPayload)backupOnAskingChangeView.Inventory;
            ChangeView cvm = (ChangeView)changeViewPayload.ConsensusMessage;
            cvm.Timestamp.Should().Be(328665601001);
            cvm.ViewNumber.Should().Be(0);
            cvm.Reason.Should().Be(ChangeViewReason.Timeout);

            Console.WriteLine("\n==========================");
            Console.WriteLine("will trigger OnPersistCompleted again with OnStart flag!");
            actorConsensus.Tell(testPersistCompleted);
            Console.WriteLine("\n==========================");

            // Disabling flag ViewChanging by reverting cache of changeview that was sent
            mockContext.Object.ChangeViewPayloads[mockContext.Object.MyIndex] = null;
            Console.WriteLine("Forcing Failed nodes for recovery request... ");
            mockContext.Object.CountFailed.Should().Be(0);
            mockContext.Object.LastSeenMessage = new int[] { -1, -1, -1, -1, -1, -1, -1 };
            mockContext.Object.CountFailed.Should().Be(7);
            Console.WriteLine("\nWaiting for recovery due to failed nodes... ");
            var backupOnRecoveryDueToFailedNodes = subscriber.ExpectMsg<LocalNode.SendDirectly>();
            var recoveryPayload = (ConsensusPayload)backupOnRecoveryDueToFailedNodes.Inventory;
            rrm = (RecoveryRequest)recoveryPayload.ConsensusMessage;
            rrm.Timestamp.Should().Be(328665601001);

            Console.WriteLine("will tell PrepRequest!");
            mockContext.Object.PrevHeader.Timestamp = 328665601000;
            mockContext.Object.PrevHeader.NextConsensus.Should().Be(UInt160.Parse("0xbdbe3ca30e9d74df12ce57ebc95a302dfaa0828c"));
            var prepReq = mockContext.Object.MakePrepareRequest();
            var ppToSend = (PrepareRequest)prepReq.ConsensusMessage;

            // Forcing hashes to 0 because mempool is currently shared
            ppToSend.TransactionHashes = new UInt256[0];
            ppToSend.TransactionHashes.Length.Should().Be(0);

            actorConsensus.Tell(prepReq);
            Console.WriteLine("Waiting for something related to the PrepRequest...\nNothing happens...Recovery will come due to failed nodes");
            var backupOnRecoveryDueToFailedNodesII = subscriber.ExpectMsg<LocalNode.SendDirectly>();
            var recoveryPayloadII = (ConsensusPayload)backupOnRecoveryDueToFailedNodesII.Inventory;
            rrm = (RecoveryRequest)recoveryPayloadII.ConsensusMessage;

            Console.WriteLine("\nFailed because it is not primary and it created the prereq...Time to adjust");
            prepReq.ValidatorIndex = 1; //simulating primary as prepreq creator (signature is skip, no problem)
            // cleaning old try with Self ValidatorIndex
            mockContext.Object.PreparationPayloads[mockContext.Object.MyIndex] = null;
            actorConsensus.Tell(prepReq);
            var OnPrepResponse = subscriber.ExpectMsg<LocalNode.SendDirectly>();
            var prepResponsePayload = (ConsensusPayload)OnPrepResponse.Inventory;
            PrepareResponse prm = (PrepareResponse)prepResponsePayload.ConsensusMessage;
            prm.PreparationHash.Should().Be(prepReq.Hash);

            // Simulating CN 3
            actorConsensus.Tell(GetPayloadAndModifyValidator(prepResponsePayload, 2));

            // Simulating CN 5
            actorConsensus.Tell(GetPayloadAndModifyValidator(prepResponsePayload, 4));

            // Simulating CN 4
            actorConsensus.Tell(GetPayloadAndModifyValidator(prepResponsePayload, 3));

            var onCommitPayload = subscriber.ExpectMsg<LocalNode.SendDirectly>();
            var commitPayload = (ConsensusPayload)onCommitPayload.Inventory;
            Commit cm = (Commit)commitPayload.ConsensusMessage;

            // Original Contract
            Contract originalContract = Contract.CreateMultiSigContract(mockContext.Object.M, mockContext.Object.Validators);
            Console.WriteLine($"\nORIGINAL Contract is: {originalContract.ScriptHash}");
            originalContract.ScriptHash.Should().Be(UInt160.Parse("0xbdbe3ca30e9d74df12ce57ebc95a302dfaa0828c"));
            mockContext.Object.Block.NextConsensus.Should().Be(UInt160.Parse("0xbdbe3ca30e9d74df12ce57ebc95a302dfaa0828c"));

            Console.WriteLine($"ORIGINAL BlockHash: {mockContext.Object.Block.Hash}");
            Console.WriteLine($"ORIGINAL Block NextConsensus: {mockContext.Object.Block.NextConsensus}");
            //Console.WriteLine($"VALIDATOR[0] {mockContext.Object.Validators[0]}");
            //Console.WriteLine($"VALIDATOR[0]ScriptHash: {mockWallet.Object.GetAccount(mockContext.Object.Validators[0]).ScriptHash}");

            KeyPair[] kp_array = new KeyPair[7]
                {
                    UT_Crypto.generateKey(32), // not used, kept for index consistency, didactically
                    UT_Crypto.generateKey(32),
                    UT_Crypto.generateKey(32),
                    UT_Crypto.generateKey(32),
                    UT_Crypto.generateKey(32),
                    UT_Crypto.generateKey(32),
                    UT_Crypto.generateKey(32)
                };
            for (int i = 0; i < mockContext.Object.Validators.Length; i++)
                Console.WriteLine($"{mockContext.Object.Validators[i]}");
            mockContext.Object.Validators = new ECPoint[7]
                {
                    mockContext.Object.Validators[0],
                    kp_array[1].PublicKey,
                    kp_array[2].PublicKey,
                    kp_array[3].PublicKey,
                    kp_array[4].PublicKey,
                    kp_array[5].PublicKey,
                    kp_array[6].PublicKey
                };
            Console.WriteLine($"Generated keypairs PKey:");
            for (int i = 0; i < mockContext.Object.Validators.Length; i++)
                Console.WriteLine($"{mockContext.Object.Validators[i]}");

            // update Contract with some random validators
            var updatedContract = Contract.CreateMultiSigContract(mockContext.Object.M, mockContext.Object.Validators);
            Console.WriteLine($"\nContract updated: {updatedContract.ScriptHash}");

            // Forcing next consensus
            var originalBlockHashData = mockContext.Object.Block.GetHashData();
            mockContext.Object.Block.NextConsensus = updatedContract.ScriptHash;
            mockContext.Object.Block.Header.NextConsensus = updatedContract.ScriptHash;
            mockContext.Object.PrevHeader.NextConsensus = updatedContract.ScriptHash;
            var originalBlockMerkleRoot = mockContext.Object.Block.MerkleRoot;
            Console.WriteLine($"\noriginalBlockMerkleRoot: {originalBlockMerkleRoot}");
            var updatedBlockHashData = mockContext.Object.Block.GetHashData();
            Console.WriteLine($"originalBlockHashData: {originalBlockHashData.ToScriptHash()}");
            Console.WriteLine($"updatedBlockHashData: {updatedBlockHashData.ToScriptHash()}");

            Console.WriteLine("\n\n==========================");
            Console.WriteLine("\nBasic commits Signatures verification");
            // Basic tests for understanding signatures and ensuring signatures of commits are correct on tests
            var cmPayloadTemp = GetCommitPayloadModifiedAndSignedCopy(commitPayload, 1, kp_array[1], updatedBlockHashData);
            Crypto.Default.VerifySignature(originalBlockHashData, cm.Signature, mockContext.Object.Validators[0].EncodePoint(false)).Should().BeFalse();
            Crypto.Default.VerifySignature(updatedBlockHashData, cm.Signature, mockContext.Object.Validators[0].EncodePoint(false)).Should().BeFalse();
            Crypto.Default.VerifySignature(originalBlockHashData, ((Commit)cmPayloadTemp.ConsensusMessage).Signature, mockContext.Object.Validators[1].EncodePoint(false)).Should().BeFalse();
            Crypto.Default.VerifySignature(updatedBlockHashData, ((Commit)cmPayloadTemp.ConsensusMessage).Signature, mockContext.Object.Validators[1].EncodePoint(false)).Should().BeTrue();
            Console.WriteLine("\n==========================");

            Console.WriteLine("\n==========================");
            Console.WriteLine("\nCN2 simulation time");
            actorConsensus.Tell(cmPayloadTemp);

            Console.WriteLine("\nCN3 simulation time");
            actorConsensus.Tell(GetCommitPayloadModifiedAndSignedCopy(commitPayload, 2, kp_array[2], updatedBlockHashData));

            Console.WriteLine("\nCN4 simulation time");
            actorConsensus.Tell(GetCommitPayloadModifiedAndSignedCopy(commitPayload, 3, kp_array[3], updatedBlockHashData));

            // =============================================
            // Testing commit with wrong signature not valid
            // It will be invalid signature because we did not change ECPoint
            Console.WriteLine("\nCN7 simulation time. Wrong signature, KeyPair is not known");
            actorConsensus.Tell(GetPayloadAndModifyValidator(commitPayload, 6));

            Console.WriteLine("\nWaiting for recovery due to failed nodes... ");
            var backupOnRecoveryMessageAfterCommit = subscriber.ExpectMsg<LocalNode.SendDirectly>();
            var rmPayload = (ConsensusPayload)backupOnRecoveryMessageAfterCommit.Inventory;
            RecoveryMessage rmm = (RecoveryMessage)rmPayload.ConsensusMessage;
            // =============================================

            mockContext.Object.CommitPayloads[0] = null;
            Console.WriteLine("\nCN5 simulation time");
            actorConsensus.Tell(GetCommitPayloadModifiedAndSignedCopy(commitPayload, 4, kp_array[4], updatedBlockHashData));


            Console.WriteLine($"\nFocing block PrevHash to UInt256.Zero {mockContext.Object.Block.GetHashData().ToScriptHash()}");
            mockContext.Object.Block.PrevHash = UInt256.Zero;
            // Payload should also be forced, otherwise OnConsensus will not pass
            commitPayload.PrevHash = UInt256.Zero;
            Console.WriteLine($"\nNew Hash is {mockContext.Object.Block.GetHashData().ToScriptHash()}");

            Console.WriteLine($"\nForcing block VerificationScript to {updatedContract.Script.ToScriptHash()}");
            mockContext.Object.Block.Witness = new Witness { };
            mockContext.Object.Block.Witness.VerificationScript = updatedContract.Script;
            Console.WriteLine($"\nUpdating BlockBase Witness scripthash  is {mockContext.Object.Block.Witness.ScriptHash}");
            Console.WriteLine($"\nNew Hash is {mockContext.Object.Block.GetHashData().ToScriptHash()}");

            Console.WriteLine("\nCN6 simulation time");
            // Here we used modified mockContext.Object.Block.GetHashData().ToScriptHash() for blockhash
            actorConsensus.Tell(GetCommitPayloadModifiedAndSignedCopy(commitPayload, 5, kp_array[5], mockContext.Object.Block.GetHashData()));

            Console.WriteLine("\nWait for subscriber Local.Node Relay");
            var onBlockRelay = subscriber.ExpectMsg<LocalNode.Relay>();
            Console.WriteLine("\nAsserting time was Block...");
            var utBlock = (Block)onBlockRelay.Inventory;

            Console.WriteLine($"\nAsserting block NextConsensus..{utBlock.NextConsensus}");
            utBlock.NextConsensus.Should().Be(updatedContract.ScriptHash);

            Console.WriteLine("\n==========================");
            // ============================================================================
            //                      finalize ConsensusService actor
            // ============================================================================
            Console.WriteLine("Finalizing consensus service actor and returning states.");
            TimeProvider.ResetToDefault();

            //Sys.Stop(actorConsensus);
        }

        public ConsensusPayload GetCommitPayloadModifiedAndSignedCopy(ConsensusPayload cpToCopy, ushort vI, KeyPair kp, byte[] blockHashToSign)
        {
            var cpCommitTemp = cpToCopy.ToArray().AsSerializable<ConsensusPayload>();
            cpCommitTemp.ValidatorIndex = vI;
            cpCommitTemp.ConsensusMessage = cpToCopy.ConsensusMessage.ToArray().AsSerializable<Commit>();
            ((Commit)cpCommitTemp.ConsensusMessage).Signature = Crypto.Default.Sign(blockHashToSign, kp.PrivateKey, kp.PublicKey.EncodePoint(false).Skip(1).ToArray());
            // Payload is not being signed by vI, since we are bypassing this check as directly talking to subscriber
            return cpCommitTemp;
        }

        public ConsensusPayload GetPayloadAndModifyValidator(ConsensusPayload cpToCopy, ushort vI)
        {
            var cpTemp = cpToCopy.ToArray().AsSerializable<ConsensusPayload>();
            cpTemp.ValidatorIndex = vI;
            return cpTemp;
        }

        [TestMethod]
        public void TestSerializeAndDeserializeConsensusContext()
        {
            var consensusContext = new ConsensusContext(null, null)
            {
                Block = new Block
                {
                    PrevHash = Blockchain.GenesisBlock.Hash,
                    Index = 1,
                    Timestamp = 4244941711,
                    NextConsensus = UInt160.Parse("5555AAAA5555AAAA5555AAAA5555AAAA5555AAAA"),
                    ConsensusData = new ConsensusData
                    {
                        PrimaryIndex = 6
                    }
                },
                ViewNumber = 2,
                Validators = new ECPoint[7]
                {
                    ECPoint.Parse("02486fd15702c4490a26703112a5cc1d0923fd697a33406bd5a1c00e0013b09a70", Neo.Cryptography.ECC.ECCurve.Secp256r1),
                    ECPoint.Parse("024c7b7fb6c310fccf1ba33b082519d82964ea93868d676662d4a59ad548df0e7d", Neo.Cryptography.ECC.ECCurve.Secp256r1),
                    ECPoint.Parse("02aaec38470f6aad0042c6e877cfd8087d2676b0f516fddd362801b9bd3936399e", Neo.Cryptography.ECC.ECCurve.Secp256r1),
                    ECPoint.Parse("02ca0e27697b9c248f6f16e085fd0061e26f44da85b58ee835c110caa5ec3ba554", Neo.Cryptography.ECC.ECCurve.Secp256r1),
                    ECPoint.Parse("02df48f60e8f3e01c48ff40b9b7f1310d7a8b2a193188befe1c2e3df740e895093", Neo.Cryptography.ECC.ECCurve.Secp256r1),
                    ECPoint.Parse("03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c", Neo.Cryptography.ECC.ECCurve.Secp256r1),
                    ECPoint.Parse("03b8d9d5771d8f513aa0869b9cc8d50986403b78c6da36890638c3d46a5adce04a", Neo.Cryptography.ECC.ECCurve.Secp256r1)
                },
                MyIndex = -1
            };
            var testTx1 = TestUtils.CreateRandomHashTransaction();
            var testTx2 = TestUtils.CreateRandomHashTransaction();

            int txCountToInlcude = 256;
            consensusContext.TransactionHashes = new UInt256[txCountToInlcude];

            Transaction[] txs = new Transaction[txCountToInlcude];
            for (int i = 0; i < txCountToInlcude; i++)
            {
                txs[i] = TestUtils.CreateRandomHashTransaction();
                consensusContext.TransactionHashes[i] = txs[i].Hash;
            }
            // consensusContext.TransactionHashes = new UInt256[2] {testTx1.Hash, testTx2.Hash};
            consensusContext.Transactions = txs.ToDictionary(p => p.Hash);

            consensusContext.PreparationPayloads = new ConsensusPayload[consensusContext.Validators.Length];
            var prepareRequestMessage = new PrepareRequest
            {
                TransactionHashes = consensusContext.TransactionHashes,
                Timestamp = 23
            };
            consensusContext.PreparationPayloads[6] = MakeSignedPayload(consensusContext, prepareRequestMessage, 6, new[] { (byte)'3', (byte)'!' });
            consensusContext.PreparationPayloads[0] = MakeSignedPayload(consensusContext, new PrepareResponse { PreparationHash = consensusContext.PreparationPayloads[6].Hash }, 0, new[] { (byte)'t', (byte)'e' });
            consensusContext.PreparationPayloads[1] = MakeSignedPayload(consensusContext, new PrepareResponse { PreparationHash = consensusContext.PreparationPayloads[6].Hash }, 1, new[] { (byte)'s', (byte)'t' });
            consensusContext.PreparationPayloads[2] = null;
            consensusContext.PreparationPayloads[3] = MakeSignedPayload(consensusContext, new PrepareResponse { PreparationHash = consensusContext.PreparationPayloads[6].Hash }, 3, new[] { (byte)'1', (byte)'2' });
            consensusContext.PreparationPayloads[4] = null;
            consensusContext.PreparationPayloads[5] = null;

            consensusContext.CommitPayloads = new ConsensusPayload[consensusContext.Validators.Length];
            using (SHA256 sha256 = SHA256.Create())
            {
                consensusContext.CommitPayloads[3] = MakeSignedPayload(consensusContext, new Commit { Signature = sha256.ComputeHash(testTx1.Hash.ToArray()) }, 3, new[] { (byte)'3', (byte)'4' });
                consensusContext.CommitPayloads[6] = MakeSignedPayload(consensusContext, new Commit { Signature = sha256.ComputeHash(testTx2.Hash.ToArray()) }, 3, new[] { (byte)'6', (byte)'7' });
            }

            consensusContext.Block.Timestamp = TimeProvider.Current.UtcNow.ToTimestampMS();

            consensusContext.ChangeViewPayloads = new ConsensusPayload[consensusContext.Validators.Length];
            consensusContext.ChangeViewPayloads[0] = MakeSignedPayload(consensusContext, new ChangeView { ViewNumber = 1, Timestamp = 6 }, 0, new[] { (byte)'A' });
            consensusContext.ChangeViewPayloads[1] = MakeSignedPayload(consensusContext, new ChangeView { ViewNumber = 1, Timestamp = 5 }, 1, new[] { (byte)'B' });
            consensusContext.ChangeViewPayloads[2] = null;
            consensusContext.ChangeViewPayloads[3] = MakeSignedPayload(consensusContext, new ChangeView { ViewNumber = 1, Timestamp = uint.MaxValue }, 3, new[] { (byte)'C' });
            consensusContext.ChangeViewPayloads[4] = null;
            consensusContext.ChangeViewPayloads[5] = null;
            consensusContext.ChangeViewPayloads[6] = MakeSignedPayload(consensusContext, new ChangeView { ViewNumber = 1, Timestamp = 1 }, 6, new[] { (byte)'D' });

            consensusContext.LastChangeViewPayloads = new ConsensusPayload[consensusContext.Validators.Length];

            var copiedContext = TestUtils.CopyMsgBySerialization(consensusContext, new ConsensusContext(null, null));

            copiedContext.Block.PrevHash.Should().Be(consensusContext.Block.PrevHash);
            copiedContext.Block.Index.Should().Be(consensusContext.Block.Index);
            copiedContext.ViewNumber.Should().Be(consensusContext.ViewNumber);
            copiedContext.Validators.Should().BeEquivalentTo(consensusContext.Validators);
            copiedContext.MyIndex.Should().Be(consensusContext.MyIndex);
            copiedContext.Block.ConsensusData.PrimaryIndex.Should().Be(consensusContext.Block.ConsensusData.PrimaryIndex);
            copiedContext.Block.Timestamp.Should().Be(consensusContext.Block.Timestamp);
            copiedContext.Block.NextConsensus.Should().Be(consensusContext.Block.NextConsensus);
            copiedContext.TransactionHashes.Should().BeEquivalentTo(consensusContext.TransactionHashes);
            copiedContext.Transactions.Should().BeEquivalentTo(consensusContext.Transactions);
            copiedContext.Transactions.Values.Should().BeEquivalentTo(consensusContext.Transactions.Values);
            copiedContext.PreparationPayloads.Should().BeEquivalentTo(consensusContext.PreparationPayloads);
            copiedContext.CommitPayloads.Should().BeEquivalentTo(consensusContext.CommitPayloads);
            copiedContext.ChangeViewPayloads.Should().BeEquivalentTo(consensusContext.ChangeViewPayloads);
        }

        [TestMethod]
        public void TestSerializeAndDeserializeRecoveryMessageWithChangeViewsAndNoPrepareRequest()
        {
            var msg = new RecoveryMessage
            {
                ChangeViewMessages = new Dictionary<int, RecoveryMessage.ChangeViewPayloadCompact>()
                {
                    {
                        0,
                        new RecoveryMessage.ChangeViewPayloadCompact
                        {
                            ValidatorIndex = 0,
                            OriginalViewNumber = 9,
                            Timestamp = 6,
                            InvocationScript = new[] { (byte)'A' }
                        }
                    },
                    {
                        1,
                        new RecoveryMessage.ChangeViewPayloadCompact
                        {
                            ValidatorIndex = 1,
                            OriginalViewNumber = 7,
                            Timestamp = 5,
                            InvocationScript = new[] { (byte)'B' }
                        }
                    },
                    {
                        3,
                        new RecoveryMessage.ChangeViewPayloadCompact
                        {
                            ValidatorIndex = 3,
                            OriginalViewNumber = 5,
                            Timestamp = 3,
                            InvocationScript = new[] { (byte)'C' }
                        }
                    },
                    {
                        6,
                        new RecoveryMessage.ChangeViewPayloadCompact
                        {
                            ValidatorIndex = 6,
                            OriginalViewNumber = 2,
                            Timestamp = 1,
                            InvocationScript = new[] { (byte)'D' }
                        }
                    }
                },
                PreparationHash = new UInt256(Crypto.Default.Hash256(new[] { (byte)'a' })),
                PreparationMessages = new Dictionary<int, RecoveryMessage.PreparationPayloadCompact>()
                {
                    {
                        0,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 0,
                            InvocationScript = new[] { (byte)'t', (byte)'e' }
                        }
                    },
                    {
                        3,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 3,
                            InvocationScript = new[] { (byte)'1', (byte)'2' }
                        }
                    },
                    {
                        6,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 6,
                            InvocationScript = new[] { (byte)'3', (byte)'!' }
                        }
                    }
                },
                CommitMessages = new Dictionary<int, RecoveryMessage.CommitPayloadCompact>()
            };

            // msg.TransactionHashes = null;
            // msg.Nonce = 0;
            // msg.NextConsensus = null;
            // msg.MinerTransaction = (MinerTransaction) null;
            msg.PrepareRequestMessage.Should().Be(null);

            var copiedMsg = TestUtils.CopyMsgBySerialization(msg, new RecoveryMessage()); ;

            copiedMsg.ChangeViewMessages.Should().BeEquivalentTo(msg.ChangeViewMessages);
            copiedMsg.PreparationHash.Should().Be(msg.PreparationHash);
            copiedMsg.PreparationMessages.Should().BeEquivalentTo(msg.PreparationMessages);
            copiedMsg.CommitMessages.Count.Should().Be(0);
        }

        [TestMethod]
        public void TestSerializeAndDeserializeRecoveryMessageWithChangeViewsAndPrepareRequest()
        {
            Transaction[] txs = new Transaction[5];
            for (int i = 0; i < txs.Length; i++)
                txs[i] = TestUtils.CreateRandomHashTransaction();
            var msg = new RecoveryMessage
            {
                ChangeViewMessages = new Dictionary<int, RecoveryMessage.ChangeViewPayloadCompact>()
                {
                    {
                        0,
                        new RecoveryMessage.ChangeViewPayloadCompact
                        {
                            ValidatorIndex = 0,
                            OriginalViewNumber = 9,
                            Timestamp = 6,
                            InvocationScript = new[] { (byte)'A' }
                        }
                    },
                    {
                        1,
                        new RecoveryMessage.ChangeViewPayloadCompact
                        {
                            ValidatorIndex = 1,
                            OriginalViewNumber = 7,
                            Timestamp = 5,
                            InvocationScript = new[] { (byte)'B' }
                        }
                    },
                    {
                        3,
                        new RecoveryMessage.ChangeViewPayloadCompact
                        {
                            ValidatorIndex = 3,
                            OriginalViewNumber = 5,
                            Timestamp = 3,
                            InvocationScript = new[] { (byte)'C' }
                        }
                    },
                    {
                        6,
                        new RecoveryMessage.ChangeViewPayloadCompact
                        {
                            ValidatorIndex = 6,
                            OriginalViewNumber = 2,
                            Timestamp = 1,
                            InvocationScript = new[] { (byte)'D' }
                        }
                    }
                },
                PrepareRequestMessage = new PrepareRequest
                {
                    TransactionHashes = txs.Select(p => p.Hash).ToArray()
                },
                PreparationHash = new UInt256(Crypto.Default.Hash256(new[] { (byte)'a' })),
                PreparationMessages = new Dictionary<int, RecoveryMessage.PreparationPayloadCompact>()
                {
                    {
                        0,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 0,
                            InvocationScript = new[] { (byte)'t', (byte)'e' }
                        }
                    },
                    {
                        1,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 1,
                            InvocationScript = new[] { (byte)'s', (byte)'t' }
                        }
                    },
                    {
                        3,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 3,
                            InvocationScript = new[] { (byte)'1', (byte)'2' }
                        }
                    }
                },
                CommitMessages = new Dictionary<int, RecoveryMessage.CommitPayloadCompact>()
            };

            var copiedMsg = TestUtils.CopyMsgBySerialization(msg, new RecoveryMessage()); ;

            copiedMsg.ChangeViewMessages.Should().BeEquivalentTo(msg.ChangeViewMessages);
            copiedMsg.PrepareRequestMessage.Should().BeEquivalentTo(msg.PrepareRequestMessage);
            copiedMsg.PreparationHash.Should().Be(null);
            copiedMsg.PreparationMessages.Should().BeEquivalentTo(msg.PreparationMessages);
            copiedMsg.CommitMessages.Count.Should().Be(0);
        }

        [TestMethod]
        public void TestSerializeAndDeserializeRecoveryMessageWithoutChangeViewsWithoutCommits()
        {
            Transaction[] txs = new Transaction[5];
            for (int i = 0; i < txs.Length; i++)
                txs[i] = TestUtils.CreateRandomHashTransaction();
            var msg = new RecoveryMessage
            {
                ChangeViewMessages = new Dictionary<int, RecoveryMessage.ChangeViewPayloadCompact>(),
                PrepareRequestMessage = new PrepareRequest
                {
                    TransactionHashes = txs.Select(p => p.Hash).ToArray()
                },
                PreparationMessages = new Dictionary<int, RecoveryMessage.PreparationPayloadCompact>()
                {
                    {
                        0,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 0,
                            InvocationScript = new[] { (byte)'t', (byte)'e' }
                        }
                    },
                    {
                        1,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 1,
                            InvocationScript = new[] { (byte)'s', (byte)'t' }
                        }
                    },
                    {
                        3,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 3,
                            InvocationScript = new[] { (byte)'1', (byte)'2' }
                        }
                    },
                    {
                        6,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 6,
                            InvocationScript = new[] { (byte)'3', (byte)'!' }
                        }
                    }
                },
                CommitMessages = new Dictionary<int, RecoveryMessage.CommitPayloadCompact>()
            };

            var copiedMsg = TestUtils.CopyMsgBySerialization(msg, new RecoveryMessage()); ;

            copiedMsg.ChangeViewMessages.Count.Should().Be(0);
            copiedMsg.PrepareRequestMessage.Should().BeEquivalentTo(msg.PrepareRequestMessage);
            copiedMsg.PreparationHash.Should().Be(null);
            copiedMsg.PreparationMessages.Should().BeEquivalentTo(msg.PreparationMessages);
            copiedMsg.CommitMessages.Count.Should().Be(0);
        }

        [TestMethod]
        public void TestSerializeAndDeserializeRecoveryMessageWithoutChangeViewsWithCommits()
        {
            Transaction[] txs = new Transaction[5];
            for (int i = 0; i < txs.Length; i++)
                txs[i] = TestUtils.CreateRandomHashTransaction();
            var msg = new RecoveryMessage
            {
                ChangeViewMessages = new Dictionary<int, RecoveryMessage.ChangeViewPayloadCompact>(),
                PrepareRequestMessage = new PrepareRequest
                {
                    TransactionHashes = txs.Select(p => p.Hash).ToArray()
                },
                PreparationMessages = new Dictionary<int, RecoveryMessage.PreparationPayloadCompact>()
                {
                    {
                        0,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 0,
                            InvocationScript = new[] { (byte)'t', (byte)'e' }
                        }
                    },
                    {
                        1,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 1,
                            InvocationScript = new[] { (byte)'s', (byte)'t' }
                        }
                    },
                    {
                        3,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 3,
                            InvocationScript = new[] { (byte)'1', (byte)'2' }
                        }
                    },
                    {
                        6,
                        new RecoveryMessage.PreparationPayloadCompact
                        {
                            ValidatorIndex = 6,
                            InvocationScript = new[] { (byte)'3', (byte)'!' }
                        }
                    }
                },
                CommitMessages = new Dictionary<int, RecoveryMessage.CommitPayloadCompact>
                {
                    {
                        1,
                        new RecoveryMessage.CommitPayloadCompact
                        {
                            ValidatorIndex = 1,
                            Signature = new byte[64] { (byte)'1', (byte)'2', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                            InvocationScript = new[] { (byte)'1', (byte)'2' }
                        }
                    },
                    {
                        6,
                        new RecoveryMessage.CommitPayloadCompact
                        {
                            ValidatorIndex = 6,
                            Signature = new byte[64] { (byte)'3', (byte)'D', (byte)'R', (byte)'I', (byte)'N', (byte)'K', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                            InvocationScript = new[] { (byte)'6', (byte)'7' }
                        }
                    }
                }
            };

            var copiedMsg = TestUtils.CopyMsgBySerialization(msg, new RecoveryMessage()); ;

            copiedMsg.ChangeViewMessages.Count.Should().Be(0);
            copiedMsg.PrepareRequestMessage.Should().BeEquivalentTo(msg.PrepareRequestMessage);
            copiedMsg.PreparationHash.Should().Be(null);
            copiedMsg.PreparationMessages.Should().BeEquivalentTo(msg.PreparationMessages);
            copiedMsg.CommitMessages.Should().BeEquivalentTo(msg.CommitMessages);
        }

        private static ConsensusPayload MakeSignedPayload(ConsensusContext context, ConsensusMessage message, ushort validatorIndex, byte[] witnessInvocationScript)
        {
            return new ConsensusPayload
            {
                Version = context.Block.Version,
                PrevHash = context.Block.PrevHash,
                BlockIndex = context.Block.Index,
                ValidatorIndex = validatorIndex,
                ConsensusMessage = message,
                Witness = new Witness
                {
                    InvocationScript = witnessInvocationScript,
                    VerificationScript = Contract.CreateSignatureRedeemScript(context.Validators[validatorIndex])
                }
            };
        }
    }
}
