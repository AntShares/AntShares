using Akka.TestKit.Xunit2;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Neo.Cryptography;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Neo.UnitTests.Ledger
{
    internal class TestIMemoryPoolTxObserverPlugin : Plugin, IMemoryPoolTxObserverPlugin
    {
        protected override void Configure() { }
        public void TransactionAdded(NeoSystem system, Transaction tx) { }
        public void TransactionsRemoved(NeoSystem system, MemoryPoolTxRemovalReason reason, IEnumerable<Transaction> transactions) { }
    }

    [TestClass]
    public class UT_MemoryPool : TestKit
    {
        private static NeoSystem testBlockchain;

        private const byte Prefix_MaxTransactionsPerBlock = 23;
        private const byte Prefix_FeePerByte = 10;
        private readonly UInt160 senderAccount = UInt160.Zero;
        private MemoryPool _unit;
        private MemoryPool _unit2;
        private TestIMemoryPoolTxObserverPlugin plugin;

        [ClassInitialize]
        public static void TestSetup(TestContext ctx)
        {
            testBlockchain = TestBlockchain.TheNeoSystem;
        }

        private static DataCache GetSnapshot()
        {
            return testBlockchain.StoreView.CreateSnapshot();
        }

        [TestInitialize]
        public void TestSetup()
        {
            // protect against external changes on TimeProvider
            TimeProvider.ResetToDefault();

            // Create a MemoryPool with capacity of 100
            _unit = new MemoryPool(new NeoSystem(ProtocolSettings.Default with { MemoryPoolMaxTransactions = 100 }));

            // Verify capacity equals the amount specified
            _unit.Capacity.Should().Be(100);

            _unit.VerifiedCount.Should().Be(0);
            _unit.UnVerifiedCount.Should().Be(0);
            _unit.Count.Should().Be(0);
            _unit2 = new MemoryPool(new NeoSystem(ProtocolSettings.Default with { MemoryPoolMaxTransactions = 0 }));
            plugin = new TestIMemoryPoolTxObserverPlugin();
        }

        [TestCleanup]
        public void CleanUp()
        {
            Plugin.TxObserverPlugins.Remove(plugin);
        }

        private static long LongRandom(long min, long max, Random rand)
        {
            // Only returns positive random long values.
            long longRand = (long)rand.NextBigInteger(63);
            return longRand % (max - min) + min;
        }

        private Transaction CreateTransactionWithFee(long fee)
        {
            Random random = new();
            var randomBytes = new byte[16];
            random.NextBytes(randomBytes);
            Mock<Transaction> mock = new();
            mock.Setup(p => p.VerifyStateDependent(It.IsAny<ProtocolSettings>(), It.IsAny<DataCache>(), It.IsAny<TransactionVerificationContext>(), null)).Returns(VerifyResult.Succeed);
            mock.Setup(p => p.VerifyStateIndependent(It.IsAny<ProtocolSettings>())).Returns(VerifyResult.Succeed);
            mock.Object.Script = randomBytes;
            mock.Object.NetworkFee = fee;
            mock.Object.Attributes = Array.Empty<TransactionAttribute>();
            mock.Object.Signers = new Signer[] { new Signer() { Account = senderAccount, Scopes = WitnessScope.None } };
            mock.Object.Witnesses = new[]
            {
                new Witness
                {
                    InvocationScript = Array.Empty<byte>(),
                    VerificationScript = Array.Empty<byte>()
                }
            };
            return mock.Object;
        }

        private Transaction CreateTransactionWithFeeAndBalanceVerify(long fee)
        {
            Random random = new();
            var randomBytes = new byte[16];
            random.NextBytes(randomBytes);
            Mock<Transaction> mock = new();
            UInt160 sender = senderAccount;
            mock.Setup(p => p.VerifyStateDependent(It.IsAny<ProtocolSettings>(), It.IsAny<DataCache>(), It.IsAny<TransactionVerificationContext>(), null)).Returns((ProtocolSettings settings, DataCache snapshot, TransactionVerificationContext context, IEnumerable<Transaction> mempool) => context.CheckTransaction(mock.Object, snapshot) ? VerifyResult.Succeed : VerifyResult.InsufficientFunds);
            mock.Setup(p => p.VerifyStateIndependent(It.IsAny<ProtocolSettings>())).Returns(VerifyResult.Succeed);
            mock.Object.Script = randomBytes;
            mock.Object.NetworkFee = fee;
            mock.Object.Attributes = Array.Empty<TransactionAttribute>();
            mock.Object.Signers = new Signer[] { new Signer() { Account = senderAccount, Scopes = WitnessScope.None } };
            mock.Object.Witnesses = new[]
            {
                new Witness
                {
                    InvocationScript = Array.Empty<byte>(),
                    VerificationScript = Array.Empty<byte>()
                }
            };
            return mock.Object;
        }

        private Transaction CreateTransaction(long fee = -1)
        {
            if (fee != -1)
                return CreateTransactionWithFee(fee);
            return CreateTransactionWithFee(LongRandom(100000, 100000000, TestUtils.TestRandom));
        }

        private void AddTransactions(int count)
        {
            var snapshot = GetSnapshot();
            for (int i = 0; i < count; i++)
            {
                var txToAdd = CreateTransaction();
                _unit.TryAdd(txToAdd, snapshot);
            }

            Console.WriteLine($"created {count} tx");
        }

        private void AddTransaction(Transaction txToAdd)
        {
            var snapshot = GetSnapshot();
            _unit.TryAdd(txToAdd, snapshot);
        }

        private void AddTransactionsWithBalanceVerify(int count, long fee, DataCache snapshot)
        {
            for (int i = 0; i < count; i++)
            {
                var txToAdd = CreateTransactionWithFeeAndBalanceVerify(fee);
                _unit.TryAdd(txToAdd, snapshot);
            }

            Console.WriteLine($"created {count} tx");
        }

        [TestMethod]
        public void CapacityTest()
        {
            // Add over the capacity items, verify that the verified count increases each time
            AddTransactions(101);

            Console.WriteLine($"VerifiedCount: {_unit.VerifiedCount} Count {_unit.SortedTxCount}");

            _unit.SortedTxCount.Should().Be(100);
            _unit.VerifiedCount.Should().Be(100);
            _unit.UnVerifiedCount.Should().Be(0);
            _unit.Count.Should().Be(100);
        }

        [TestMethod]
        public void BlockPersistMovesTxToUnverifiedAndReverification()
        {
            AddTransactions(70);

            _unit.SortedTxCount.Should().Be(70);

            var block = new Block
            {
                Header = new Header(),
                Transactions = _unit.GetSortedVerifiedTransactions().Take(10)
                    .Concat(_unit.GetSortedVerifiedTransactions().Take(5)).ToArray()
            };
            _unit.UpdatePoolForBlockPersisted(block, GetSnapshot());
            _unit.InvalidateVerifiedTransactions();
            _unit.SortedTxCount.Should().Be(0);
            _unit.UnverifiedSortedTxCount.Should().Be(60);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, GetSnapshot());
            _unit.SortedTxCount.Should().Be(10);
            _unit.UnverifiedSortedTxCount.Should().Be(50);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, GetSnapshot());
            _unit.SortedTxCount.Should().Be(20);
            _unit.UnverifiedSortedTxCount.Should().Be(40);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, GetSnapshot());
            _unit.SortedTxCount.Should().Be(30);
            _unit.UnverifiedSortedTxCount.Should().Be(30);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, GetSnapshot());
            _unit.SortedTxCount.Should().Be(40);
            _unit.UnverifiedSortedTxCount.Should().Be(20);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, GetSnapshot());
            _unit.SortedTxCount.Should().Be(50);
            _unit.UnverifiedSortedTxCount.Should().Be(10);

            _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(10, GetSnapshot());
            _unit.SortedTxCount.Should().Be(60);
            _unit.UnverifiedSortedTxCount.Should().Be(0);
        }

        [TestMethod]
        public async Task BlockPersistAndReverificationWillAbandonTxAsBalanceTransfered()
        {
            var snapshot = GetSnapshot();
            BigInteger balance = NativeContract.GAS.BalanceOf(snapshot, senderAccount);
            ApplicationEngine engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, settings: TestBlockchain.TheNeoSystem.Settings, gas: long.MaxValue);
            await NativeContract.GAS.Burn(engine, UInt160.Zero, balance);
            _ = NativeContract.GAS.Mint(engine, UInt160.Zero, 70, true);

            long txFee = 1;
            AddTransactionsWithBalanceVerify(70, txFee, snapshot);

            _unit.SortedTxCount.Should().Be(70);

            var block = new Block
            {
                Header = new Header(),
                Transactions = _unit.GetSortedVerifiedTransactions().Take(10).ToArray()
            };

            // Simulate the transfer process in tx by burning the balance
            UInt160 sender = block.Transactions[0].Sender;

            ApplicationEngine applicationEngine = ApplicationEngine.Create(TriggerType.All, block, snapshot, block, settings: TestBlockchain.TheNeoSystem.Settings, gas: (long)balance);
            await NativeContract.GAS.Burn(applicationEngine, sender, NativeContract.GAS.BalanceOf(snapshot, sender));
            _ = NativeContract.GAS.Mint(applicationEngine, sender, txFee * 30, true); // Set the balance to meet 30 txs only

            // Persist block and reverify all the txs in mempool, but half of the txs will be discarded
            _unit.UpdatePoolForBlockPersisted(block, snapshot);
            _unit.SortedTxCount.Should().Be(30);
            _unit.UnverifiedSortedTxCount.Should().Be(0);

            // Revert the balance
            await NativeContract.GAS.Burn(applicationEngine, sender, txFee * 30);
            _ = NativeContract.GAS.Mint(applicationEngine, sender, balance, true);
        }

        private static void VerifyTransactionsSortedDescending(IEnumerable<Transaction> transactions)
        {
            Transaction lastTransaction = null;
            foreach (var tx in transactions)
            {
                if (lastTransaction != null)
                {
                    if (lastTransaction.FeePerByte == tx.FeePerByte)
                    {
                        if (lastTransaction.NetworkFee == tx.NetworkFee)
                            lastTransaction.Hash.Should().BeLessThan(tx.Hash);
                        else
                            lastTransaction.NetworkFee.Should().BeGreaterThan(tx.NetworkFee);
                    }
                    else
                    {
                        lastTransaction.FeePerByte.Should().BeGreaterThan(tx.FeePerByte);
                    }
                }
                lastTransaction = tx;
            }
        }

        [TestMethod]
        public void VerifySortOrderAndThatHighetFeeTransactionsAreReverifiedFirst()
        {
            AddTransactions(100);

            var sortedVerifiedTxs = _unit.GetSortedVerifiedTransactions().ToList();
            // verify all 100 transactions are returned in sorted order
            sortedVerifiedTxs.Count.Should().Be(100);
            VerifyTransactionsSortedDescending(sortedVerifiedTxs);

            // move all to unverified
            var block = new Block
            {
                Header = new Header(),
                Transactions = Array.Empty<Transaction>()
            };
            _unit.UpdatePoolForBlockPersisted(block, GetSnapshot());
            _unit.InvalidateVerifiedTransactions();
            _unit.SortedTxCount.Should().Be(0);
            _unit.UnverifiedSortedTxCount.Should().Be(100);

            // We can verify the order they are re-verified by reverifying 2 at a time
            while (_unit.UnVerifiedCount > 0)
            {
                _unit.GetVerifiedAndUnverifiedTransactions(out var sortedVerifiedTransactions, out var sortedUnverifiedTransactions);
                sortedVerifiedTransactions.Count().Should().Be(0);
                var sortedUnverifiedArray = sortedUnverifiedTransactions.ToArray();
                VerifyTransactionsSortedDescending(sortedUnverifiedArray);
                var maxTransaction = sortedUnverifiedArray.First();
                var minTransaction = sortedUnverifiedArray.Last();

                // reverify 1 high priority and 1 low priority transaction
                _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(1, GetSnapshot());
                var verifiedTxs = _unit.GetSortedVerifiedTransactions().ToArray();
                verifiedTxs.Length.Should().Be(1);
                verifiedTxs[0].Should().BeEquivalentTo(maxTransaction);
                var blockWith2Tx = new Block
                {
                    Header = new Header(),
                    Transactions = new[] { maxTransaction, minTransaction }
                };
                // verify and remove the 2 transactions from the verified pool
                _unit.UpdatePoolForBlockPersisted(blockWith2Tx, GetSnapshot());
                _unit.InvalidateVerifiedTransactions();
                _unit.SortedTxCount.Should().Be(0);
            }
            _unit.UnverifiedSortedTxCount.Should().Be(0);
        }

        void VerifyCapacityThresholdForAttemptingToAddATransaction()
        {
            var sortedVerified = _unit.GetSortedVerifiedTransactions().ToArray();

            var txBarelyWontFit = CreateTransactionWithFee(sortedVerified.Last().NetworkFee - 1);
            _unit.CanTransactionFitInPool(txBarelyWontFit).Should().Be(false);
            var txBarelyFits = CreateTransactionWithFee(sortedVerified.Last().NetworkFee + 1);
            _unit.CanTransactionFitInPool(txBarelyFits).Should().Be(true);
        }

        [TestMethod]
        public void VerifyCanTransactionFitInPoolWorksAsIntended()
        {
            AddTransactions(100);
            VerifyCapacityThresholdForAttemptingToAddATransaction();
            AddTransactions(50);
            VerifyCapacityThresholdForAttemptingToAddATransaction();
            AddTransactions(50);
            VerifyCapacityThresholdForAttemptingToAddATransaction();
        }

        [TestMethod]
        public void CapacityTestWithUnverifiedHighProirtyTransactions()
        {
            // Verify that unverified high priority transactions will not be pushed out of the queue by incoming
            // low priority transactions

            // Fill pool with high priority transactions
            AddTransactions(99);

            // move all to unverified
            var block = new Block
            {
                Header = new Header(),
                Transactions = Array.Empty<Transaction>()
            };
            _unit.UpdatePoolForBlockPersisted(block, GetSnapshot());

            _unit.CanTransactionFitInPool(CreateTransaction()).Should().Be(true);
            AddTransactions(1);
            _unit.CanTransactionFitInPool(CreateTransactionWithFee(0)).Should().Be(false);
        }

        [TestMethod]
        public void TestInvalidateAll()
        {
            AddTransactions(30);

            _unit.UnverifiedSortedTxCount.Should().Be(0);
            _unit.SortedTxCount.Should().Be(30);
            _unit.InvalidateAllTransactions();
            _unit.UnverifiedSortedTxCount.Should().Be(30);
            _unit.SortedTxCount.Should().Be(0);
        }

        [TestMethod]
        public void TestContainsKey()
        {
            var snapshot = GetSnapshot();
            AddTransactions(10);

            var txToAdd = CreateTransaction();
            _unit.TryAdd(txToAdd, snapshot);
            _unit.ContainsKey(txToAdd.Hash).Should().BeTrue();
            _unit.InvalidateVerifiedTransactions();
            _unit.ContainsKey(txToAdd.Hash).Should().BeTrue();
        }

        [TestMethod]
        public void TestGetEnumerator()
        {
            AddTransactions(10);
            _unit.InvalidateVerifiedTransactions();
            IEnumerator<Transaction> enumerator = _unit.GetEnumerator();
            foreach (Transaction tx in _unit)
            {
                enumerator.MoveNext();
                enumerator.Current.Should().BeSameAs(tx);
            }
        }

        [TestMethod]
        public void TestIEnumerableGetEnumerator()
        {
            AddTransactions(10);
            _unit.InvalidateVerifiedTransactions();
            IEnumerable enumerable = _unit;
            var enumerator = enumerable.GetEnumerator();
            foreach (Transaction tx in _unit)
            {
                enumerator.MoveNext();
                enumerator.Current.Should().BeSameAs(tx);
            }
        }

        [TestMethod]
        public void TestGetVerifiedTransactions()
        {
            var snapshot = GetSnapshot();
            var tx1 = CreateTransaction();
            var tx2 = CreateTransaction();
            _unit.TryAdd(tx1, snapshot);
            _unit.InvalidateVerifiedTransactions();
            _unit.TryAdd(tx2, snapshot);
            IEnumerable<Transaction> enumerable = _unit.GetVerifiedTransactions();
            enumerable.Count().Should().Be(1);
            var enumerator = enumerable.GetEnumerator();
            enumerator.MoveNext();
            enumerator.Current.Should().BeSameAs(tx2);
        }

        [TestMethod]
        public void TestReVerifyTopUnverifiedTransactionsIfNeeded()
        {
            _unit = new MemoryPool(new NeoSystem(ProtocolSettings.Default with { MemoryPoolMaxTransactions = 600 }));

            AddTransaction(CreateTransaction(100000001));
            AddTransaction(CreateTransaction(100000001));
            AddTransaction(CreateTransaction(100000001));
            AddTransaction(CreateTransaction(1));
            _unit.VerifiedCount.Should().Be(4);
            _unit.UnVerifiedCount.Should().Be(0);

            _unit.InvalidateVerifiedTransactions();
            _unit.VerifiedCount.Should().Be(0);
            _unit.UnVerifiedCount.Should().Be(4);

            AddTransactions(511); // Max per block currently is 512
            _unit.VerifiedCount.Should().Be(511);
            _unit.UnVerifiedCount.Should().Be(4);

            var result = _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(1, GetSnapshot());
            result.Should().BeTrue();
            _unit.VerifiedCount.Should().Be(512);
            _unit.UnVerifiedCount.Should().Be(3);

            result = _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(2, GetSnapshot());
            result.Should().BeTrue();
            _unit.VerifiedCount.Should().Be(514);
            _unit.UnVerifiedCount.Should().Be(1);

            result = _unit.ReVerifyTopUnverifiedTransactionsIfNeeded(3, GetSnapshot());
            result.Should().BeFalse();
            _unit.VerifiedCount.Should().Be(515);
            _unit.UnVerifiedCount.Should().Be(0);
        }

        [TestMethod]
        public void TestTryAdd()
        {
            var snapshot = GetSnapshot();
            var tx1 = CreateTransaction();
            _unit.TryAdd(tx1, snapshot).Should().Be(VerifyResult.Succeed);
            _unit.TryAdd(tx1, snapshot).Should().NotBe(VerifyResult.Succeed);
            _unit2.TryAdd(tx1, snapshot).Should().NotBe(VerifyResult.Succeed);
        }

        [TestMethod]
        public void TestTryGetValue()
        {
            var snapshot = GetSnapshot();
            var tx1 = CreateTransaction();
            _unit.TryAdd(tx1, snapshot);
            _unit.TryGetValue(tx1.Hash, out Transaction tx).Should().BeTrue();
            tx.Should().BeEquivalentTo(tx1);

            _unit.InvalidateVerifiedTransactions();
            _unit.TryGetValue(tx1.Hash, out tx).Should().BeTrue();
            tx.Should().BeEquivalentTo(tx1);

            var tx2 = CreateTransaction();
            _unit.TryGetValue(tx2.Hash, out _).Should().BeFalse();
        }

        [TestMethod]
        public void TestUpdatePoolForBlockPersisted()
        {
            var snapshot = GetSnapshot();
            byte[] transactionsPerBlock = { 0x18, 0x00, 0x00, 0x00 }; // 24
            byte[] feePerByte = { 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00 }; // 1048576
            StorageItem item1 = new()
            {
                Value = transactionsPerBlock
            };
            StorageItem item2 = new()
            {
                Value = feePerByte
            };
            var key1 = CreateStorageKey(Prefix_MaxTransactionsPerBlock);
            var key2 = CreateStorageKey(Prefix_FeePerByte);
            key1.Id = NativeContract.Policy.Id;
            key2.Id = NativeContract.Policy.Id;
            snapshot.Add(key1, item1);
            snapshot.Add(key2, item2);

            var tx1 = CreateTransaction();
            var tx2 = CreateTransaction();
            Transaction[] transactions = { tx1, tx2 };
            _unit.TryAdd(tx1, snapshot);

            var block = new Block
            {
                Header = new Header(),
                Transactions = transactions
            };

            _unit.UnVerifiedCount.Should().Be(0);
            _unit.VerifiedCount.Should().Be(1);

            _unit.UpdatePoolForBlockPersisted(block, snapshot);

            _unit.UnVerifiedCount.Should().Be(0);
            _unit.VerifiedCount.Should().Be(0);
        }

        public static StorageKey CreateStorageKey(byte prefix, byte[] key = null)
        {
            StorageKey storageKey = new()
            {
                Id = 0,
                Key = new byte[sizeof(byte) + (key?.Length ?? 0)]
            };
            storageKey.Key[0] = prefix;
            if (key != null)
                Buffer.BlockCopy(key, 0, storageKey.Key, 1, key.Length);
            return storageKey;
        }
    }
}
