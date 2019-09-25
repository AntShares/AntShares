using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Neo.IO.Json;
using Neo.Network.P2P.Payloads;
using Neo.Network.RPC;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets;
using System.Numerics;

namespace Neo.UnitTests.Network.RPC
{
    [TestClass]
    public class UT_NeoAPI
    {
        Mock<RpcClient> rpcClientMock;
        KeyPair keyPair1;
        string address1;
        UInt160 sender;
        NeoAPI neoAPI;

        [TestInitialize]
        public void TestSetup()
        {
            keyPair1 = new KeyPair(Wallet.GetPrivateKeyFromWIF("KyXwTh1hB76RRMquSvnxZrJzQx7h9nQP2PCRL38v6VDb5ip3nf1p"));
            address1 = Neo.Wallets.Helper.ToAddress(keyPair1.ToScriptHash());
            sender = Contract.CreateSignatureRedeemScript(keyPair1.PublicKey).ToScriptHash();
            rpcClientMock = UT_TransactionManager.MockRpcClient(sender, new byte[0]);
            neoAPI = new NeoAPI(rpcClientMock.Object);
        }

        [TestMethod]
        public void TestGetUnclaimedGas()
        {
            byte[] testScript = NativeContract.NEO.Hash.MakeScript("unclaimedGas", keyPair1.ToScriptHash(), 99);
            UT_TransactionManager.MockInvokeScript(rpcClientMock, testScript, new ContractParameter { Type = ContractParameterType.Integer, Value = new BigInteger(1_10000000) });

            var balance = neoAPI.GetUnclaimedGas(address1);
            Assert.AreEqual(1.1m, balance);
        }

        [TestMethod]
        public void TestGetNeoBalance()
        {
            byte[] testScript = NativeContract.NEO.Hash.MakeScript("balanceOf", keyPair1.ToScriptHash());
            UT_TransactionManager.MockInvokeScript(rpcClientMock, testScript, new ContractParameter { Type = ContractParameterType.Integer, Value = new BigInteger(1_00000000) });

            var balance = neoAPI.GetNeoBalance(address1);
            Assert.AreEqual(1_00000000u, balance);
        }

        [TestMethod]
        public void TestGetGasBalance()
        {
            byte[] testScript = NativeContract.GAS.Hash.MakeScript("balanceOf", keyPair1.ToScriptHash());
            UT_TransactionManager.MockInvokeScript(rpcClientMock, testScript, new ContractParameter { Type = ContractParameterType.Integer, Value = new BigInteger(1_10000000) });

            var balance = neoAPI.GetGasBalance(address1);
            Assert.AreEqual(1.1m, balance);
        }

        [TestMethod]
        public void TestGetTokenBalance()
        {
            byte[] testScript = UInt160.Zero.MakeScript("balanceOf", keyPair1.ToScriptHash());
            UT_TransactionManager.MockInvokeScript(rpcClientMock, testScript, new ContractParameter { Type = ContractParameterType.Integer, Value = new BigInteger(1_10000000) });

            var balance = neoAPI.GetTokenBalance(UInt160.Zero.ToString(), address1);
            Assert.AreEqual(1_10000000, balance);
        }

        [TestMethod]
        public void TestClaimGas()
        {
            byte[] balanceScript = NativeContract.NEO.Hash.MakeScript("balanceOf", keyPair1.ToScriptHash());
            UT_TransactionManager.MockInvokeScript(rpcClientMock, balanceScript, new ContractParameter { Type = ContractParameterType.Integer, Value = new BigInteger(1_00000000) });

            byte[] testScript = NativeContract.NEO.Hash.MakeScript("transfer", keyPair1.ToScriptHash(), keyPair1.ToScriptHash(), new BigInteger(1_00000000));
            UT_TransactionManager.MockInvokeScript(rpcClientMock, testScript, new ContractParameter { Type = ContractParameterType.Integer, Value = new BigInteger(1_10000000) });

            rpcClientMock.Setup(p => p.RpcSend("sendrawtransaction", It.IsAny<JObject>())).Returns(true);

            var tranaction = neoAPI.ClaimGas(keyPair1.Export());
            Assert.AreEqual(testScript.ToHexString(), tranaction.Script.ToHexString());
        }

        [TestMethod]
        public void TestTransfer()
        {
            byte[] decimalsScript = NativeContract.GAS.Hash.MakeScript("decimals");
            UT_TransactionManager.MockInvokeScript(rpcClientMock, decimalsScript, new ContractParameter { Type = ContractParameterType.Integer, Value = new BigInteger(8) });

            byte[] testScript = NativeContract.GAS.Hash.MakeScript("transfer", keyPair1.ToScriptHash(), UInt160.Zero, NativeContract.GAS.Factor * 100);
            UT_TransactionManager.MockInvokeScript(rpcClientMock, testScript, new ContractParameter { Type = ContractParameterType.Integer, Value = new BigInteger(1_10000000) });

            rpcClientMock.Setup(p => p.RpcSend("sendrawtransaction", It.IsAny<JObject>())).Returns(true);

            var tranaction = neoAPI.Transfer(NativeContract.GAS.Hash.ToString(), keyPair1.Export(), UInt160.Zero.ToAddress(), 100, 1.1m);
            Assert.AreEqual(testScript.ToHexString(), tranaction.Script.ToHexString());
        }

        [TestMethod]
        public void TestWaitTransaction()
        {
            Transaction transaction = TestUtils.GetTransaction();
            rpcClientMock.Setup(p => p.RpcSend("gettransactionheight", It.Is<JObject>(j => j.AsString() == transaction.Hash.ToString()))).Returns(1000);

            var height = neoAPI.WaitTransaction(transaction).Result;
            Assert.AreEqual(1000u, height);
        }

        //[TestMethod]
        //public void IntegratinTestTransfer()
        //{
        //    RpcClient client = new RpcClient("http://127.0.0.1:20332");
        //    NeoAPI neoAPI = new NeoAPI(client);

        //    KeyPair key1 = "L1rFMTamZj85ENnqNLwmhXKAprHuqr1MxMHmCWCGiXGsAdQ2dnhb".ToKeyPair();
        //    KeyPair key2 = "L3TbPZ3Gtqh3TTk2CWn44m9iiuUhBGZWoDJQuvVw5Zbx5NAjPbdb".ToKeyPair();
        //    string address1 = Neo.Wallets.Helper.ToAddress(key1.ToScriptHash());
        //    string address2 = Neo.Wallets.Helper.ToAddress(key2.ToScriptHash());

        //    Console.WriteLine($"Before Transfer {address1}(NEO):{neoAPI.GetNeoBalance(address1)} (GAS):{neoAPI.GetGasBalance(address1)}");

        //    var trans = neoAPI.Transfer(NativeContract.NEO.Hash.ToString(), "L1rFMTamZj85ENnqNLwmhXKAprHuqr1MxMHmCWCGiXGsAdQ2dnhb", address2, 1);
        //    uint height = neoAPI.WaitTransaction(trans).Result;

        //    Console.WriteLine($"Transaction on block height {height}, {address1}(NEO):{neoAPI.GetNeoBalance(address1)} (GAS):{neoAPI.GetGasBalance(address1)}");
        //}

        //[TestMethod]
        //public void IntegratinTestClaimGas()
        //{
        //    RpcClient client = new RpcClient("http://127.0.0.1:20332");
        //    NeoAPI neoAPI = new NeoAPI(client);

        //    KeyPair key1 = "L1rFMTamZj85ENnqNLwmhXKAprHuqr1MxMHmCWCGiXGsAdQ2dnhb".ToKeyPair();
        //    string address1 = Neo.Wallets.Helper.ToAddress(key1.ToScriptHash());

        //    Console.WriteLine($"GAS Balance:{neoAPI.GetGasBalance(address1)}, GAS Claimable: {neoAPI.GetUnclaimedGas(address1)}");

        //    var trans = neoAPI.ClaimGas(key1.Export());
        //    uint height = neoAPI.WaitTransaction(trans).Result;

        //    Console.WriteLine($"Transaction on block height {height}, GAS Balance:{neoAPI.GetGasBalance(address1)}, GAS Claimable: {neoAPI.GetUnclaimedGas(address1)}");
        //}
    }
}
