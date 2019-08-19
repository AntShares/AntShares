﻿using Neo.Network.P2P.Payloads;
using Neo.Network.RPC.Models;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.VM;
using Neo.Wallets;

namespace Neo.Network.RPC
{
    public class ContractClient
    {
        protected readonly RpcClient rpcClient;

        public ContractClient(RpcClient rpc)
        {
            rpcClient = rpc;
        }

        /// <summary>
        /// Generate scripts to call a specific method from a specific contract.
        /// </summary>
        public static byte[] MakeScript(UInt160 scriptHash, string operation, params object[] args)
        {
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                if (args.Length > 0)
                    sb.EmitAppCall(scriptHash, operation, args);
                else
                    sb.EmitAppCall(scriptHash, operation);
                return sb.ToArray();
            }
        }

        /// <summary>
        /// Use RPC method to test invoke operation.
        /// </summary>
        public RpcInvokeResult TestInvoke(UInt160 scriptHash, string operation, params object[] args)
        {
            byte[] script = MakeScript(scriptHash, operation, args);
            return rpcClient.InvokeScript(script);
        }

        /// <summary>
        /// Deploy Contract, return signed transaction
        /// </summary>
        public Transaction DeployContract(byte[] contractScript, bool hasStorage, bool isPayable, KeyPair key, long networkFee = 0)
        {
            ContractFeatures properties = ContractFeatures.NoProperty;
            if (hasStorage) properties |= ContractFeatures.HasStorage;
            if (isPayable) properties |= ContractFeatures.Payable;

            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitSysCall(InteropService.Neo_Contract_Create, contractScript, properties);
                script = sb.ToArray();
            }

            Transaction tx = new TransactionManager(rpcClient, Contract.CreateSignatureRedeemScript(key.PublicKey).ToScriptHash())
                .MakeTransaction(script, null, null, networkFee)
                .AddSignature(key)
                .Sign()
                .Tx;

            return tx;
        }
    }
}
