﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Neo.Ledger;
using FluentAssertions;
using Neo.Cryptography.ECC;
using Neo.IO.Wrappers;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;

namespace Neo.UnitTests
{
    [TestClass]
    public class UT_MemoryPool
    {
        private static NeoSystem TheNeoSystem;

        private readonly Random _random = new Random(1337); // use fixed seed for guaranteed determinism

        private MemoryPool _unit;

        [TestInitialize]
        public void TestSetup()
        {
            if (TheNeoSystem == null)
            {
                var mockSnapshot = new Mock<Snapshot>();
                mockSnapshot.SetupGet(p => p.Blocks).Returns(new TestDataCache<UInt256, BlockState>());
                mockSnapshot.SetupGet(p => p.Transactions).Returns(new TestDataCache<UInt256, TransactionState>());
                mockSnapshot.SetupGet(p => p.Accounts).Returns(new TestDataCache<UInt160, AccountState>());
                mockSnapshot.SetupGet(p => p.UnspentCoins).Returns(new TestDataCache<UInt256, UnspentCoinState>());
                mockSnapshot.SetupGet(p => p.SpentCoins).Returns(new TestDataCache<UInt256, SpentCoinState>());
                mockSnapshot.SetupGet(p => p.Validators).Returns(new TestDataCache<ECPoint, ValidatorState>());
                mockSnapshot.SetupGet(p => p.Assets).Returns(new TestDataCache<UInt256, AssetState>());
                mockSnapshot.SetupGet(p => p.Contracts).Returns(new TestDataCache<UInt160, ContractState>());
                mockSnapshot.SetupGet(p => p.Storages).Returns(new TestDataCache<StorageKey, StorageItem>());
                mockSnapshot.SetupGet(p => p.HeaderHashList)
                    .Returns(new TestDataCache<UInt32Wrapper, HeaderHashList>());
                mockSnapshot.SetupGet(p => p.ValidatorsCount).Returns(new TestMetaDataCache<ValidatorsCountState>());
                mockSnapshot.SetupGet(p => p.BlockHashIndex).Returns(new TestMetaDataCache<HashIndexState>());
                mockSnapshot.SetupGet(p => p.HeaderHashIndex).Returns(new TestMetaDataCache<HashIndexState>());

                var mockStore = new Mock<Store>();

                var defaultTx = CreateRandomHashInvocationMockTransaction().Object;
                defaultTx.Outputs = new TransactionOutput[1];
                defaultTx.Outputs[0] = new TransactionOutput
                {
                    AssetId = Blockchain.UtilityToken.Hash,
                    Value = new Fixed8(1000000),
                    ScriptHash = UInt160.Zero // doesn't matter for our purposes.
                };

                mockStore.Setup(p => p.GetBlocks()).Returns(new TestDataCache<UInt256, BlockState>());
                mockStore.Setup(p => p.GetTransactions()).Returns(new TestDataCache<UInt256, TransactionState>(
                    new TransactionState
                    {
                        BlockIndex = 1,
                        Transaction = defaultTx
                    }));

                mockStore.Setup(p => p.GetAccounts()).Returns(new TestDataCache<UInt160, AccountState>());
                mockStore.Setup(p => p.GetUnspentCoins()).Returns(new TestDataCache<UInt256, UnspentCoinState>());
                mockStore.Setup(p => p.GetSpentCoins()).Returns(new TestDataCache<UInt256, SpentCoinState>());
                mockStore.Setup(p => p.GetValidators()).Returns(new TestDataCache<ECPoint, ValidatorState>());
                mockStore.Setup(p => p.GetAssets()).Returns(new TestDataCache<UInt256, AssetState>());
                mockStore.Setup(p => p.GetContracts()).Returns(new TestDataCache<UInt160, ContractState>());
                mockStore.Setup(p => p.GetStorages()).Returns(new TestDataCache<StorageKey, StorageItem>());
                mockStore.Setup(p => p.GetHeaderHashList()).Returns(new TestDataCache<UInt32Wrapper, HeaderHashList>());
                mockStore.Setup(p => p.GetValidatorsCount()).Returns(new TestMetaDataCache<ValidatorsCountState>());
                mockStore.Setup(p => p.GetBlockHashIndex()).Returns(new TestMetaDataCache<HashIndexState>());
                mockStore.Setup(p => p.GetHeaderHashIndex()).Returns(new TestMetaDataCache<HashIndexState>());
                mockStore.Setup(p => p.GetSnapshot()).Returns(mockSnapshot.Object);

                Console.WriteLine("initialize NeoSystem");
                TheNeoSystem = new NeoSystem(mockStore.Object); // new Mock<NeoSystem>(mockStore.Object);
            }

            // Create a MemoryPool with capacity of 100
            _unit = new MemoryPool(TheNeoSystem, 100);

            // Verify capacity equals the amount specified
            _unit.Capacity.ShouldBeEquivalentTo(100);

            _unit.VerifiedCount.ShouldBeEquivalentTo(0);
            _unit.UnVerifiedCount.ShouldBeEquivalentTo(0);
            _unit.Count.ShouldBeEquivalentTo(0);
        }

        private Mock<InvocationTransaction> CreateRandomHashInvocationMockTransaction()
        {
            var mockTx = new Mock<InvocationTransaction>();
            mockTx.CallBase = true;
            mockTx.Setup(p => p.Verify(It.IsAny<Snapshot>(), It.IsAny<IEnumerable<Transaction>>())).Returns(true);
            var tx = mockTx.Object;
            var randomBytes = new byte[16];
            _random.NextBytes(randomBytes);
            tx.Script = randomBytes;
            tx.Attributes = new TransactionAttribute[0];
            tx.Inputs = new CoinReference[0];
            tx.Outputs = new TransactionOutput[0];
            tx.Witnesses = new Witness[0];

            return mockTx;
        }

        private Transaction CreateMockHighPriorityTransaction()
        {
            var mockTx = CreateRandomHashInvocationMockTransaction();
            long randomFeeLong = _random.Next(200000) + 100000; // (0.001 + [0,0.002]) GAS (enough to be a high priority TX)
            mockTx.SetupGet(p => p.NetworkFee).Returns(new Fixed8(randomFeeLong));
            var tx = mockTx.Object;
            tx.Inputs = new CoinReference[1];
            // Any input will trigger reading the transaction output and get our mocked transaction output.
            tx.Inputs[0] = new CoinReference
            {
                PrevHash = UInt256.Zero,
                PrevIndex = 0
            };
            return tx;
        }


        private Transaction CreateMockLowPriorityTransaction()
        {
            var mockTx = CreateRandomHashInvocationMockTransaction();
            long randomFeeLong = _random.Next(100000); // a fee lower than 0.001 GAS (not enough to be a high priority TX)
            mockTx.SetupGet(p => p.NetworkFee).Returns(new Fixed8(randomFeeLong));            
            return mockTx.Object;
        }

        private  void AddTransactions(int count, bool isHighPriority=false)
        {
            for (int i = 0; i < count; i++)
            {
                var txToAdd = isHighPriority ? CreateMockHighPriorityTransaction(): CreateMockLowPriorityTransaction();
                Console.WriteLine($"created tx: {txToAdd.Hash}");
                _unit.TryAdd(txToAdd.Hash, txToAdd);
            }
        }

        private void AddLowPriorityTransactions(int count) => AddTransactions(count);
        public void AddHighPriorityTransactions(int count) => AddTransactions(count, true);

        [TestMethod]
        public void LowPriorityCapacityTest()
        {
            // Add over the capacity items, verify that the verified count increases each time
            AddLowPriorityTransactions(50);
            _unit.VerifiedCount.ShouldBeEquivalentTo(50);
            AddLowPriorityTransactions(51);
            Console.WriteLine($"VerifiedCount: {_unit.VerifiedCount}  LowPrioCount {_unit.SortedLowPrioTxCount}  HighPrioCount {_unit.SortedHighPrioTxCount}");
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(100);
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(0);

            _unit.VerifiedCount.ShouldBeEquivalentTo(100);
            _unit.UnVerifiedCount.ShouldBeEquivalentTo(0);
            _unit.Count.ShouldBeEquivalentTo(100);
        }

        [TestMethod]
        public void HighPriorityCapacityTest()
        {
            // Add over the capacity items, verify that the verified count increases each time
            AddHighPriorityTransactions(101);

            Console.WriteLine($"VerifiedCount: {_unit.VerifiedCount}  LowPrioCount {_unit.SortedLowPrioTxCount}  HighPrioCount {_unit.SortedHighPrioTxCount}");
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(0);
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(100);

            _unit.VerifiedCount.ShouldBeEquivalentTo(100);
            _unit.UnVerifiedCount.ShouldBeEquivalentTo(0);
            _unit.Count.ShouldBeEquivalentTo(100);
        }

        [TestMethod]
        public void HighPriorityPushesOutLowPriority()
        {
            // Add over the capacity items, verify that the verified count increases each time
            AddLowPriorityTransactions(70);
            AddHighPriorityTransactions(40);

            Console.WriteLine($"VerifiedCount: {_unit.VerifiedCount}  LowPrioCount {_unit.SortedLowPrioTxCount}  HighPrioCount {_unit.SortedHighPrioTxCount}");
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(60);
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(40);
            _unit.Count.ShouldBeEquivalentTo(100);
        }

        [TestMethod]
        public void LowPriorityDoesNotPushOutHighPrority()
        {
            AddHighPriorityTransactions(70);
            AddLowPriorityTransactions(40);

            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(30);
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(70);
            _unit.Count.ShouldBeEquivalentTo(100);
        }

        [TestMethod]
        public void BlockPersistMovesTxToUnverifiedAndReverification()
        {
            AddHighPriorityTransactions(70);
            AddLowPriorityTransactions(30);

            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(70);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(30);

            var block = new Block
            {
                Transactions = _unit.GetSortedVerifiedTransactions().Take(10)
                    .Concat(_unit.GetSortedVerifiedTransactions().Where(x => x.IsLowPriority).Take(5)).ToArray()
            };
            _unit.UpdatePoolForBlockPersisted(block, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(0);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(0);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(60);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(25);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(9);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(1);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(51);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(24);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(18);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(2);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(42);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(23);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(27);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(3);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(33);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(22);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(36);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(4);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(24);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(21);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(45);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(5);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(15);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(20);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(54);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(6);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(6);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(19);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(60);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(10);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(0);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(15);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(60);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(20);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(0);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(5);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, Blockchain.Singleton.GetSnapshot());
            _unit.SortedHighPrioTxCount.ShouldBeEquivalentTo(60);
            _unit.SortedLowPrioTxCount.ShouldBeEquivalentTo(25);
            _unit.UnverifiedSortedHighPrioTxCount.ShouldBeEquivalentTo(0);
            _unit.UnverifiedSortedLowPrioTxCount.ShouldBeEquivalentTo(0);
        }

        [TestMethod]
        public void CapacityTestWithUnverifiedHighProirtyTransactions()
        {
            // Verify that unverified high priority transactions will not be pushed out of the queue by incoming
            // low priority transactions

            // Fill pool with high priority transactions
            AddHighPriorityTransactions(99);

            // move all to unverified
            var block = new Block { Transactions = new Transaction[0] };
            _unit.UpdatePoolForBlockPersisted(block, Blockchain.Singleton.GetSnapshot());

            _unit.CanTransactionFitInPool(CreateMockLowPriorityTransaction()).ShouldBeEquivalentTo(true);
            AddHighPriorityTransactions(1);
            _unit.CanTransactionFitInPool(CreateMockLowPriorityTransaction()).ShouldBeEquivalentTo(false);
        }
    }
}
