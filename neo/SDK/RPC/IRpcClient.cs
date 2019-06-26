﻿using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Network.RPC;
using Neo.SDK.RPC.Model;
using System.Collections.Generic;

namespace Neo.SDK.RPC
{

    /// <summary>
    /// Wrappar of NEO RPC APIs
    /// </summary>
    public interface IRpcClient
    {
        /// <summary>
        /// Returns the hash of the tallest block in the main chain.
        /// </summary>
        string GetBestBlockHash();

        /// <summary>
        /// Returns the hash of the tallest block in the main chain.
        /// The serialized information of the block is returned, represented by a hexadecimal string.
        /// </summary>
        string GetBlockHex(string hashOrIndex);

        /// <summary>
        /// Returns the hash of the tallest block in the main chain.
        /// </summary>
        SDK_Block GetBlock(string hashOrIndex);

        /// <summary>
        /// Gets the number of blocks in the main chain.
        /// </summary>
        int GetBlockCount();

        /// <summary>
        /// Returns the hash value of the corresponding block, based on the specified index.
        /// </summary>
        string GetBlockHash(int index);

        /// <summary>
        /// Returns the corresponding block header information according to the specified script hash.
        /// </summary>
        string GetBlockHeaderHex(string hashOrIndex);

        /// <summary>
        /// Returns the corresponding block header information according to the specified script hash.
        /// </summary>
        SDK_BlockHeader GetBlockHeader(string hashOrIndex);

        /// <summary>
        /// Returns the system fees of the block, based on the specified index.
        /// </summary>
        string GetBlockSysFee(int height);

        /// <summary>
        /// Gets the current number of connections for the node.
        /// </summary>
        int GetConnectionCount();

        /// <summary>
        /// Queries contract information, according to the contract script hash.
        /// </summary>
        ContractState GetContractState(string hash);

        /// <summary>
        /// Gets the list of nodes that the node is currently connected/disconnected from.
        /// </summary>
        SDK_GetPeersResult GetPeers();

        /// <summary>
        /// Obtains the list of unconfirmed transactions in memory.
        /// </summary>
        string[] GetRawMempool();

        /// <summary>
        /// Obtains the list of unconfirmed transactions in memory.
        /// shouldGetUnverified = true
        /// </summary>
        SDK_RawMemPool GetRawMempoolBoth();

        /// <summary>
        /// Returns the corresponding transaction information, based on the specified hash value.
        /// </summary>
        string GetRawTransactionHex(string txid);

        /// <summary>
        /// Returns the corresponding transaction information, based on the specified hash value.
        /// verbose = true
        /// </summary>
        Transaction GetRawTransaction(string txid);

        /// <summary>
        /// Returns the stored value, according to the contract script hash and the stored key.
        /// </summary>
        string GetStorage(string script_hash, string key);

        /// <summary>
        /// Returns the block index in which the transaction is found.
        /// </summary>
        uint GetTransactionHeight(string txid);

        /// <summary>
        /// Returns the current NEO consensus nodes information and voting status.
        /// </summary>
        SDK_Validator[] GetValidators();

        /// <summary>
        /// Returns the version information about the queried node.
        /// </summary>
        SDK_Version GetVersion();

        /// <summary>
        /// Returns the result after calling a smart contract at scripthash with the given operation and parameters.
        /// This RPC call does not affect the blockchain in any way.
        /// </summary>
        SDK_InvokeScriptResult InvokeFunction(string address, string function, SDK_StackJson[] stacks);

        /// <summary>
        /// Returns the result after passing a script through the VM.
        /// This RPC call does not affect the blockchain in any way.
        /// </summary>
        SDK_InvokeScriptResult InvokeScript(string script);

        /// <summary>
        /// Returns a list of plugins loaded by the node.
        /// </summary>
        SDK_Plugin ListPlugins();

        /// <summary>
        /// Broadcasts a transaction over the NEO network.
        /// </summary>
        bool SendRawTransaction(string rawTransaction);

        /// <summary>
        /// Broadcasts a raw block over the NEO network.
        /// </summary>
        bool SubmitBlock(string block);

        /// <summary>
        /// Verifies that the address is a correct NEO address.
        /// </summary>
        SDK_ValidateAddressResult ValidateAddress(string address);

        /// <summary>
        /// Returns the balance of all NEP-5 assets in the specified address.
        /// </summary>
        SDK_Nep5Balances GetNep5Balances(string address);

    }
}
