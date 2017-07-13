﻿using Neo.Core;
using Neo.Cryptography.ECC;
using Neo.VM;
using Neo.Wallets;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;

namespace Neo.UnitTests
{
    [TestClass]
    public class UT_Block
    {
        Block uut;

        [TestInitialize]
        public void TestSetup()
        {
            uut = new Block();
        }

        [TestMethod]
        public void Transactions_Get()
        {
            uut.Transactions.Should().BeNull();
        }

        [TestMethod]
        public void Transactions_Set()
        {
            Transaction[] val = new Transaction[10];
            uut.Transactions = val;
            uut.Transactions.Length.Should().Be(10);
        }

        private void setupBlockWithValues(Block block, out UInt256 val256, out UInt256 merkRootVal, out UInt160 val160, out uint timestampVal, out uint indexVal, out ulong consensusDataVal, out Witness scriptVal, out Transaction[] transactionsVal, bool populateTransactions)
        {
            val256 = UInt256.Zero;
            block.PrevHash = val256;
            merkRootVal = new UInt256(new byte[] { 214, 87, 42, 69, 155, 149, 217, 19, 107, 122, 113, 60, 84, 133, 202, 112, 159, 158, 250, 79, 8, 241, 194, 93, 215, 146, 103, 45, 43, 215, 91, 251 });
            block.MerkleRoot = merkRootVal;
            timestampVal = new DateTime(1968, 06, 01).ToTimestamp();
            block.Timestamp = timestampVal;
            indexVal = 0;
            block.Index = indexVal;
            consensusDataVal = 30;
            block.ConsensusData = consensusDataVal;
            val160 = UInt160.Zero;
            block.NextConsensus = val160;
            scriptVal = new Witness
            {
                InvocationScript = new byte[0],
                VerificationScript = new[] { (byte)OpCode.PUSHT }
            };
            block.Script = scriptVal;
            if (populateTransactions)
            {
                transactionsVal = new Transaction[1] {
                    getMinerTransaction()
                };
            }
            else
            {
                transactionsVal = new Transaction[0];
            }
            block.Transactions = transactionsVal;
        }

        [TestMethod]
        public void Header_Get()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.Header.Should().NotBeNull();
            uut.Header.PrevHash.Should().Be(val256);
            uut.Header.MerkleRoot.Should().Be(merkRootVal);
            uut.Header.Timestamp.Should().Be(timestampVal);
            uut.Header.Index.Should().Be(indexVal);
            uut.Header.ConsensusData.Should().Be(consensusDataVal);
            uut.Header.Script.Should().Be(scriptVal);
        }

        [TestMethod]
        public void Size_Get()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            // blockbase 4 + 32 + 32 + 4 + 4 + 8 + 20 + 1 + 3
            // block 1
            uut.Size.Should().Be(109);
        }

        private MinerTransaction getMinerTransaction()
        {
            return new MinerTransaction
            {
                Nonce = 2083236893,
                Attributes = new TransactionAttribute[0],
                Inputs = new CoinReference[0],
                Outputs = new TransactionOutput[0],
                Scripts = new Witness[0]
            };
        }

        private ClaimTransaction getClaimTransaction()
        {
            return new ClaimTransaction
            {
                Claims = new CoinReference[0]
            };
        }

        private readonly ECPoint[] StandbyValidators = new ECPoint[] { ECPoint.DecodePoint("03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c".HexToBytes(), ECCurve.Secp256r1) };
        private IssueTransaction getIssueTransaction(bool inputVal, decimal outputVal, UInt256 assetId)
        {
            setupTestBlockchain(assetId);

            CoinReference[] inputsVal;
            if (inputVal)
            {
                inputsVal = new[]
                {
                    new CoinReference
                    {
                        PrevHash = UInt256.Zero,
                        PrevIndex = 0
                    }
                };
            }
            else
            {
                inputsVal = new CoinReference[0];
            }


            return new IssueTransaction
            {
                Attributes = new TransactionAttribute[0],
                Inputs = inputsVal,
                Outputs = new[]
                {
                    new TransactionOutput
                    {
                        AssetId = assetId,
                        Value = Fixed8.FromDecimal(outputVal),
                        ScriptHash = Contract.CreateMultiSigRedeemScript(1, new ECPoint[] { ECPoint.DecodePoint("03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c".HexToBytes(), ECCurve.Secp256r1)  }).ToScriptHash()
                    }
                },
                Scripts = new[]
                {
                    new Witness
                    {
                        InvocationScript = new byte[0],
                        VerificationScript = new[] { (byte)OpCode.PUSHT }
                    }
                }
            };
        }

        private void setupTestBlockchain(UInt256 assetId)
        {
            Blockchain testBlockchain = new TestBlockchain(assetId);
            Blockchain.RegisterBlockchain(testBlockchain);
        }

        [TestMethod]
        public void Size_Get_1_Transaction()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.Transactions = new Transaction[1] {
                getMinerTransaction()
            };

            // blockbase 4 + 32 + 32 + 4 + 4 + 8 + 20 + 1 + 3
            // block 11
            uut.Size.Should().Be(119);
        }

        [TestMethod]
        public void Size_Get_3_Transaction()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.Transactions = new Transaction[3] {
                getMinerTransaction(),
                getMinerTransaction(),
                getMinerTransaction()
            };

            // blockbase 4 + 32 + 32 + 4 + 4 + 8 + 20 + 1 + 3
            // block 31
            uut.Size.Should().Be(139);
        }

        [TestMethod]
        public void CalculateNetFee_EmptyTransactions()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            Block.CalculateNetFee(uut.Transactions).Should().Be(Fixed8.Zero);
        }

        [TestMethod]
        public void CalculateNetFee_Ignores_MinerTransactions()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.Transactions = new Transaction[1] {
                getMinerTransaction()
            };

            Block.CalculateNetFee(uut.Transactions).Should().Be(Fixed8.Zero);
        }

        [TestMethod]
        public void CalculateNetFee_Ignores_ClaimTransactions()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.Transactions = new Transaction[1] {
                getClaimTransaction()
            };

            Block.CalculateNetFee(uut.Transactions).Should().Be(Fixed8.Zero);
        }


        [TestMethod]
        public void CalculateNetFee_Out()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.Transactions = new Transaction[1] {
                getIssueTransaction(false, 100, Blockchain.SystemCoin.Hash)
            };

            Block.CalculateNetFee(uut.Transactions).Should().Be(Fixed8.FromDecimal(-100));
        }

        [TestMethod]
        public void CalculateNetFee_In()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.Transactions = new Transaction[1] {
                getIssueTransaction(true, 0, Blockchain.SystemCoin.Hash)
            };

            Block.CalculateNetFee(uut.Transactions).Should().Be(Fixed8.FromDecimal(50));
        }

        [TestMethod]
        public void CalculateNetFee_In_And_Out()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.Transactions = new Transaction[1] {
                getIssueTransaction(true, 100, Blockchain.SystemCoin.Hash)
            };

            Block.CalculateNetFee(uut.Transactions).Should().Be(Fixed8.FromDecimal(-50));
        }

        [TestMethod]
        public void CalculateNetFee_SystemFee()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = false;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.Transactions = new Transaction[1] {
                getIssueTransaction(true, 0, new UInt256(TestUtils.GetByteArray(32, 0x42)))
            };

            Block.CalculateNetFee(uut.Transactions).Should().Be(Fixed8.FromDecimal(-500));
        }

        [TestMethod]
        public void Serialize()
        {
            UInt256 val256;
            UInt256 merkRootVal;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = true;
            setupBlockWithValues(uut, out val256, out merkRootVal, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);
          
            byte[] data;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, true))
                {
                    uut.Serialize(writer);
                    data = stream.ToArray();
                }
            }

            byte[] requiredData = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 214, 87, 42, 69, 155, 149, 217, 19, 107, 122, 113, 60, 84, 133, 202, 112, 159, 158, 250, 79, 8, 241, 194, 93, 215, 146, 103, 45, 43, 215, 91, 251, 112, 157, 4, 253, 0, 0, 0, 0, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 81, 1, 0, 0, 29, 172, 43, 124, 0, 0, 0, 0 };

            data.Length.Should().Be(119);
            for (int i = 0; i < 119; i++)
            {
                data[i].Should().Be(requiredData[i]);
            }
        }

        [TestMethod]
        public void Deserialize()
        {
            UInt256 val256;
            UInt256 merkRoot;
            UInt160 val160;
            uint timestampVal, indexVal;
            ulong consensusDataVal;
            Witness scriptVal;
            Transaction[] transactionsVal;
            bool populateTransactions = true;
            setupBlockWithValues(new Block(), out val256, out merkRoot, out val160, out timestampVal, out indexVal, out consensusDataVal, out scriptVal, out transactionsVal, populateTransactions);

            uut.MerkleRoot = merkRoot; // need to set for deserialise to be valid

            byte[] data = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 214, 87, 42, 69, 155, 149, 217, 19, 107, 122, 113, 60, 84, 133, 202, 112, 159, 158, 250, 79, 8, 241, 194, 93, 215, 146, 103, 45, 43, 215, 91, 251, 112, 157, 4, 253, 0, 0, 0, 0, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 81, 1, 0, 0, 29, 172, 43, 124, 0, 0, 0, 0 };
            int index = 0;
            using (MemoryStream ms = new MemoryStream(data, index, data.Length - index, false))
            {
                using (BinaryReader reader = new BinaryReader(ms))
                {
                    uut.Deserialize(reader);
                }
            }

            uut.PrevHash.Should().Be(val256);
            uut.MerkleRoot.Should().Be(merkRoot);
            uut.Timestamp.Should().Be(timestampVal);
            uut.Index.Should().Be(indexVal);
            uut.ConsensusData.Should().Be(consensusDataVal);
            uut.NextConsensus.Should().Be(val160);
            uut.Script.InvocationScript.Length.Should().Be(0);
            uut.Script.Size.Should().Be(scriptVal.Size);
            uut.Script.VerificationScript[0].Should().Be(scriptVal.VerificationScript[0]);
            uut.Transactions.Length.Should().Be(1);
            uut.Transactions[0].Should().Be(transactionsVal[0]);
        }
    }
}
