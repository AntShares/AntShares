using Neo.Cryptography;
using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets.NEP6;
using Org.BouncyCastle.Crypto.Generators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using static Neo.SmartContract.Helper;
using static Neo.Wallets.Helper;
using ECPoint = Neo.Cryptography.ECC.ECPoint;

namespace Neo.Wallets
{
    /// <summary>
    /// The base class of wallets.
    /// </summary>
    public abstract class Wallet
    {
        /// <summary>
        /// The <see cref="Neo.ProtocolSettings"/> to be used by the wallet.
        /// </summary>
        public ProtocolSettings ProtocolSettings { get; }

        /// <summary>
        /// The name of the wallet.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// The path of the wallet.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// The version of the wallet.
        /// </summary>
        public abstract Version Version { get; }

        /// <summary>
        /// Changes the password of the wallet.
        /// </summary>
        /// <param name="oldPassword">The old password of the wallet.</param>
        /// <param name="newPassword">The new password to be used.</param>
        /// <returns><see langword="true"/> if the password is changed successfully; otherwise, <see langword="false"/>.</returns>
        public abstract bool ChangePassword(string oldPassword, string newPassword);

        /// <summary>
        /// Determines whether the specified account is included in the wallet.
        /// </summary>
        /// <param name="scriptHash">The hash of the account.</param>
        /// <returns><see langword="true"/> if the account is included in the wallet; otherwise, <see langword="false"/>.</returns>
        public abstract bool Contains(UInt160 scriptHash);

        /// <summary>
        /// Creates a standard account with the specified private key.
        /// </summary>
        /// <param name="privateKey">The private key of the account.</param>
        /// <returns>The created account.</returns>
        public abstract WalletAccount CreateAccount(byte[] privateKey);

        /// <summary>
        /// Creates a contract account for the wallet.
        /// </summary>
        /// <param name="contract">The contract of the account.</param>
        /// <param name="key">The private key of the account.</param>
        /// <returns>The created account.</returns>
        public abstract WalletAccount CreateAccount(Contract contract, KeyPair key = null);

        /// <summary>
        /// Creates a watch-only account for the wallet.
        /// </summary>
        /// <param name="scriptHash">The hash of the account.</param>
        /// <returns>The created account.</returns>
        public abstract WalletAccount CreateAccount(UInt160 scriptHash);

        /// <summary>
        /// Deletes an account from the wallet.
        /// </summary>
        /// <param name="scriptHash">The hash of the account.</param>
        /// <returns><see langword="true"/> if the account is removed; otherwise, <see langword="false"/>.</returns>
        public abstract bool DeleteAccount(UInt160 scriptHash);

        /// <summary>
        /// Gets the account with the specified hash.
        /// </summary>
        /// <param name="scriptHash">The hash of the account.</param>
        /// <returns>The account with the specified hash.</returns>
        public abstract WalletAccount GetAccount(UInt160 scriptHash);

        /// <summary>
        /// Gets all the accounts from the wallet.
        /// </summary>
        /// <returns>All accounts in the wallet.</returns>
        public abstract IEnumerable<WalletAccount> GetAccounts();

        /// <summary>
        /// Initializes a new instance of the <see cref="Wallet"/> class.
        /// </summary>
        /// <param name="path">The path of the wallet file.</param>
        /// <param name="settings">The <see cref="Neo.ProtocolSettings"/> to be used by the wallet.</param>
        protected Wallet(string path, ProtocolSettings settings)
        {
            this.ProtocolSettings = settings;
            this.Path = path;
        }

        /// <summary>
        /// Creates a standard account for the wallet.
        /// </summary>
        /// <returns>The created account.</returns>
        public WalletAccount CreateAccount()
        {
            byte[] privateKey = new byte[32];
        generate:
            try
            {
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(privateKey);
                }
                return CreateAccount(privateKey);
            }
            catch (ArgumentException)
            {
                goto generate;
            }
            finally
            {
                Array.Clear(privateKey, 0, privateKey.Length);
            }
        }

        /// <summary>
        /// Creates a contract account for the wallet.
        /// </summary>
        /// <param name="contract">The contract of the account.</param>
        /// <param name="privateKey">The private key of the account.</param>
        /// <returns>The created account.</returns>
        public WalletAccount CreateAccount(Contract contract, byte[] privateKey)
        {
            if (privateKey == null) return CreateAccount(contract);
            return CreateAccount(contract, new KeyPair(privateKey));
        }

        private static List<(UInt160 Account, BigInteger Value)> FindPayingAccounts(List<(UInt160 Account, BigInteger Value)> orderedAccounts, BigInteger amount)
        {
            var result = new List<(UInt160 Account, BigInteger Value)>();
            BigInteger sum_balance = orderedAccounts.Select(p => p.Value).Sum();
            if (sum_balance == amount)
            {
                result.AddRange(orderedAccounts);
                orderedAccounts.Clear();
            }
            else
            {
                for (int i = 0; i < orderedAccounts.Count; i++)
                {
                    if (orderedAccounts[i].Value < amount)
                        continue;
                    if (orderedAccounts[i].Value == amount)
                    {
                        result.Add(orderedAccounts[i]);
                        orderedAccounts.RemoveAt(i);
                    }
                    else
                    {
                        result.Add((orderedAccounts[i].Account, amount));
                        orderedAccounts[i] = (orderedAccounts[i].Account, orderedAccounts[i].Value - amount);
                    }
                    break;
                }
                if (result.Count == 0)
                {
                    int i = orderedAccounts.Count - 1;
                    while (orderedAccounts[i].Value <= amount)
                    {
                        result.Add(orderedAccounts[i]);
                        amount -= orderedAccounts[i].Value;
                        orderedAccounts.RemoveAt(i);
                        i--;
                    }
                    for (i = 0; i < orderedAccounts.Count; i++)
                    {
                        if (orderedAccounts[i].Value < amount)
                            continue;
                        if (orderedAccounts[i].Value == amount)
                        {
                            result.Add(orderedAccounts[i]);
                            orderedAccounts.RemoveAt(i);
                        }
                        else
                        {
                            result.Add((orderedAccounts[i].Account, amount));
                            orderedAccounts[i] = (orderedAccounts[i].Account, orderedAccounts[i].Value - amount);
                        }
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Gets the account with the specified public key.
        /// </summary>
        /// <param name="pubkey">The public key of the account.</param>
        /// <returns>The account with the specified public key.</returns>
        public WalletAccount GetAccount(ECPoint pubkey)
        {
            return GetAccount(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash());
        }

        /// <summary>
        /// Gets the available balance for the specified asset in the wallet.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="asset_id">The id of the asset.</param>
        /// <returns>The available balance for the specified asset.</returns>
        public BigDecimal GetAvailable(DataCache snapshot, UInt160 asset_id)
        {
            UInt160[] accounts = GetAccounts().Where(p => !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
            return GetBalance(snapshot, asset_id, accounts);
        }

        /// <summary>
        /// Gets the balance for the specified asset in the wallet.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="asset_id">The id of the asset.</param>
        /// <param name="accounts">The accounts to be counted.</param>
        /// <returns>The balance for the specified asset.</returns>
        public BigDecimal GetBalance(DataCache snapshot, UInt160 asset_id, params UInt160[] accounts)
        {
            byte[] script;
            using (ScriptBuilder sb = new())
            {
                sb.EmitPush(0);
                foreach (UInt160 account in accounts)
                {
                    sb.EmitDynamicCall(asset_id, "balanceOf", CallFlags.ReadOnly, account);
                    sb.Emit(OpCode.ADD);
                }
                sb.EmitDynamicCall(asset_id, "decimals", CallFlags.ReadOnly);
                script = sb.ToArray();
            }
            using ApplicationEngine engine = ApplicationEngine.Run(script, snapshot, settings: ProtocolSettings, gas: 0_60000000L * accounts.Length);
            if (engine.State == VMState.FAULT)
                return new BigDecimal(BigInteger.Zero, 0);
            byte decimals = (byte)engine.ResultStack.Pop().GetInteger();
            BigInteger amount = engine.ResultStack.Pop().GetInteger();
            return new BigDecimal(amount, decimals);
        }

        private static byte[] Decrypt(byte[] data, byte[] key)
        {
            using Aes aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data, 0, data.Length);
        }

        /// <summary>
        /// Decodes a private key from the specified NEP-2 string.
        /// </summary>
        /// <param name="nep2">The NEP-2 string to be decoded.</param>
        /// <param name="passphrase">The passphrase of the private key.</param>
        /// <param name="version">The address version of NEO system.</param>
        /// <param name="N">The N field of the <see cref="ScryptParameters"/> to be used.</param>
        /// <param name="r">The R field of the <see cref="ScryptParameters"/> to be used.</param>
        /// <param name="p">The P field of the <see cref="ScryptParameters"/> to be used.</param>
        /// <returns>The decoded private key.</returns>
        public static byte[] GetPrivateKeyFromNEP2(string nep2, string passphrase, byte version, int N = 16384, int r = 8, int p = 8)
        {
            if (nep2 == null) throw new ArgumentNullException(nameof(nep2));
            if (passphrase == null) throw new ArgumentNullException(nameof(passphrase));
            byte[] data = nep2.Base58CheckDecode();
            if (data.Length != 39 || data[0] != 0x01 || data[1] != 0x42 || data[2] != 0xe0)
                throw new FormatException();
            byte[] addresshash = new byte[4];
            Buffer.BlockCopy(data, 3, addresshash, 0, 4);
            byte[] datapassphrase = Encoding.UTF8.GetBytes(passphrase);
            byte[] derivedkey = SCrypt.Generate(datapassphrase, addresshash, N, r, p, 64);
            Array.Clear(datapassphrase, 0, datapassphrase.Length);
            byte[] derivedhalf1 = derivedkey[..32];
            byte[] derivedhalf2 = derivedkey[32..];
            Array.Clear(derivedkey, 0, derivedkey.Length);
            byte[] encryptedkey = new byte[32];
            Buffer.BlockCopy(data, 7, encryptedkey, 0, 32);
            Array.Clear(data, 0, data.Length);
            byte[] prikey = XOR(Decrypt(encryptedkey, derivedhalf2), derivedhalf1);
            Array.Clear(derivedhalf1, 0, derivedhalf1.Length);
            Array.Clear(derivedhalf2, 0, derivedhalf2.Length);
            ECPoint pubkey = Cryptography.ECC.ECCurve.Secp256r1.G * prikey;
            UInt160 script_hash = Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();
            string address = script_hash.ToAddress(version);
            if (!Encoding.ASCII.GetBytes(address).Sha256().Sha256().AsSpan(0, 4).SequenceEqual(addresshash))
                throw new FormatException();
            return prikey;
        }

        /// <summary>
        /// Decodes a private key from the specified WIF string.
        /// </summary>
        /// <param name="wif">The WIF string to be decoded.</param>
        /// <returns>The decoded private key.</returns>
        public static byte[] GetPrivateKeyFromWIF(string wif)
        {
            if (wif is null) throw new ArgumentNullException(nameof(wif));
            byte[] data = wif.Base58CheckDecode();
            if (data.Length != 34 || data[0] != 0x80 || data[33] != 0x01)
                throw new FormatException();
            byte[] privateKey = new byte[32];
            Buffer.BlockCopy(data, 1, privateKey, 0, privateKey.Length);
            Array.Clear(data, 0, data.Length);
            return privateKey;
        }

        private static Signer[] GetSigners(UInt160 sender, Signer[] cosigners)
        {
            for (int i = 0; i < cosigners.Length; i++)
            {
                if (cosigners[i].Account.Equals(sender))
                {
                    if (i == 0) return cosigners;
                    List<Signer> list = new(cosigners);
                    list.RemoveAt(i);
                    list.Insert(0, cosigners[i]);
                    return list.ToArray();
                }
            }
            return cosigners.Prepend(new Signer
            {
                Account = sender,
                Scopes = WitnessScope.None
            }).ToArray();
        }

        /// <summary>
        /// Imports an account from a <see cref="X509Certificate2"/>.
        /// </summary>
        /// <param name="cert">The <see cref="X509Certificate2"/> to import.</param>
        /// <returns>The imported account.</returns>
        public virtual WalletAccount Import(X509Certificate2 cert)
        {
            byte[] privateKey;
            using (ECDsa ecdsa = cert.GetECDsaPrivateKey())
            {
                privateKey = ecdsa.ExportParameters(true).D;
            }
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        /// <summary>
        /// Imports an account from the specified WIF string.
        /// </summary>
        /// <param name="wif">The WIF string to import.</param>
        /// <returns>The imported account.</returns>
        public virtual WalletAccount Import(string wif)
        {
            byte[] privateKey = GetPrivateKeyFromWIF(wif);
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        /// <summary>
        /// Imports an account from the specified NEP-2 string.
        /// </summary>
        /// <param name="nep2">The NEP-2 string to import.</param>
        /// <param name="passphrase">The passphrase of the private key.</param>
        /// <param name="N">The N field of the <see cref="ScryptParameters"/> to be used.</param>
        /// <param name="r">The R field of the <see cref="ScryptParameters"/> to be used.</param>
        /// <param name="p">The P field of the <see cref="ScryptParameters"/> to be used.</param>
        /// <returns>The imported account.</returns>
        public virtual WalletAccount Import(string nep2, string passphrase, int N = 16384, int r = 8, int p = 8)
        {
            byte[] privateKey = GetPrivateKeyFromNEP2(nep2, passphrase, ProtocolSettings.AddressVersion, N, r, p);
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        /// <summary>
        /// Makes a transaction to transfer assets.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="outputs">The array of <see cref="TransferOutput"/> that contain the asset, amount, and targets of the transfer.</param>
        /// <param name="from">The account to transfer from.</param>
        /// <param name="cosigners">The cosigners to be added to the transction.</param>
        /// <returns>The created transction.</returns>
        public Transaction MakeTransaction(DataCache snapshot, TransferOutput[] outputs, UInt160 from = null, Signer[] cosigners = null)
        {
            UInt160[] accounts;
            if (from is null)
            {
                accounts = GetAccounts().Where(p => !p.Lock && !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
            }
            else
            {
                accounts = new[] { from };
            }
            Dictionary<UInt160, Signer> cosignerList = cosigners?.ToDictionary(p => p.Account) ?? new Dictionary<UInt160, Signer>();
            byte[] script;
            List<(UInt160 Account, BigInteger Value)> balances_gas = null;
            using (ScriptBuilder sb = new())
            {
                foreach (var (assetId, group, sum) in outputs.GroupBy(p => p.AssetId, (k, g) => (k, g, g.Select(p => p.Value.Value).Sum())))
                {
                    var balances = new List<(UInt160 Account, BigInteger Value)>();
                    foreach (UInt160 account in accounts)
                    {
                        using ScriptBuilder sb2 = new();
                        sb2.EmitDynamicCall(assetId, "balanceOf", CallFlags.ReadOnly, account);
                        using ApplicationEngine engine = ApplicationEngine.Run(sb2.ToArray(), snapshot, settings: ProtocolSettings);
                        if (engine.State != VMState.HALT)
                            throw new InvalidOperationException($"Execution for {assetId}.balanceOf('{account}' fault");
                        BigInteger value = engine.ResultStack.Pop().GetInteger();
                        if (value.Sign > 0) balances.Add((account, value));
                    }
                    BigInteger sum_balance = balances.Select(p => p.Value).Sum();
                    if (sum_balance < sum)
                        throw new InvalidOperationException($"It does not have enough balance, expected: {sum} found: {sum_balance}");
                    foreach (TransferOutput output in group)
                    {
                        balances = balances.OrderBy(p => p.Value).ToList();
                        var balances_used = FindPayingAccounts(balances, output.Value.Value);
                        foreach (var (account, value) in balances_used)
                        {
                            if (cosignerList.TryGetValue(account, out Signer signer))
                            {
                                if (signer.Scopes != WitnessScope.Global)
                                    signer.Scopes |= WitnessScope.CalledByEntry;
                            }
                            else
                            {
                                cosignerList.Add(account, new Signer
                                {
                                    Account = account,
                                    Scopes = WitnessScope.CalledByEntry
                                });
                            }
                            sb.EmitDynamicCall(output.AssetId, "transfer", account, output.ScriptHash, value, output.Data);
                            sb.Emit(OpCode.ASSERT);
                        }
                    }
                    if (assetId.Equals(NativeContract.GAS.Hash))
                        balances_gas = balances;
                }
                script = sb.ToArray();
            }
            if (balances_gas is null)
                balances_gas = accounts.Select(p => (Account: p, Value: NativeContract.GAS.BalanceOf(snapshot, p))).Where(p => p.Value.Sign > 0).ToList();

            return MakeTransaction(snapshot, script, cosignerList.Values.ToArray(), Array.Empty<TransactionAttribute>(), balances_gas);
        }

        /// <summary>
        /// Makes a transaction to run a smart contract.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="script">The script to be loaded in the transaction.</param>
        /// <param name="sender">The sender of the transaction.</param>
        /// <param name="cosigners">The cosigners to be added to the transction.</param>
        /// <param name="attributes">The attributes to be added to the transction.</param>
        /// <param name="maxGas">The maximum gas that can be spent to execute the script.</param>
        /// <returns>The created transction.</returns>
        public Transaction MakeTransaction(DataCache snapshot, byte[] script, UInt160 sender = null, Signer[] cosigners = null, TransactionAttribute[] attributes = null, long maxGas = ApplicationEngine.TestModeGas)
        {
            UInt160[] accounts;
            if (sender is null)
            {
                accounts = GetAccounts().Where(p => !p.Lock && !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
            }
            else
            {
                accounts = new[] { sender };
            }
            var balances_gas = accounts.Select(p => (Account: p, Value: NativeContract.GAS.BalanceOf(snapshot, p))).Where(p => p.Value.Sign > 0).ToList();
            return MakeTransaction(snapshot, script, cosigners ?? Array.Empty<Signer>(), attributes ?? Array.Empty<TransactionAttribute>(), balances_gas, maxGas);
        }

        private Transaction MakeTransaction(DataCache snapshot, byte[] script, Signer[] cosigners, TransactionAttribute[] attributes, List<(UInt160 Account, BigInteger Value)> balances_gas, long maxGas = ApplicationEngine.TestModeGas)
        {
            Random rand = new();
            foreach (var (account, value) in balances_gas)
            {
                Transaction tx = new()
                {
                    Version = 0,
                    Nonce = (uint)rand.Next(),
                    Script = script,
                    ValidUntilBlock = NativeContract.Ledger.CurrentIndex(snapshot) + ProtocolSettings.MaxValidUntilBlockIncrement,
                    Signers = GetSigners(account, cosigners),
                    Attributes = attributes,
                };

                // will try to execute 'transfer' script to check if it works 
                using (ApplicationEngine engine = ApplicationEngine.Run(script, snapshot.CreateSnapshot(), tx, settings: ProtocolSettings, gas: maxGas))
                {
                    if (engine.State == VMState.FAULT)
                    {
                        throw new InvalidOperationException($"Failed execution for '{Convert.ToBase64String(script)}'", engine.FaultException);
                    }
                    tx.SystemFee = engine.GasConsumed;
                }

                tx.NetworkFee = CalculateNetworkFee(snapshot, tx);
                if (value >= tx.SystemFee + tx.NetworkFee) return tx;
            }
            throw new InvalidOperationException("Insufficient GAS");
        }

        /// <summary>
        /// Calculates the network fee for the specified transaction.
        /// </summary>
        /// <param name="snapshot">The snapshot used to read data.</param>
        /// <param name="tx">The transaction to calculate.</param>
        /// <returns>The network fee of the transaction.</returns>
        public long CalculateNetworkFee(DataCache snapshot, Transaction tx)
        {
            UInt160[] hashes = tx.GetScriptHashesForVerifying(snapshot);

            // base size for transaction: includes const_header + signers + attributes + script + hashes
            int size = Transaction.HeaderSize + tx.Signers.GetVarSize() + tx.Attributes.GetVarSize() + tx.Script.GetVarSize() + IO.Helper.GetVarSize(hashes.Length);
            uint exec_fee_factor = NativeContract.Policy.GetExecFeeFactor(snapshot);
            long networkFee = 0;
            int index = -1;
            foreach (UInt160 hash in hashes)
            {
                index++;
                byte[] witness_script = GetAccount(hash)?.Contract?.Script;
                byte[] invocationScript = null;

                if (tx.Witnesses != null)
                {
                    if (witness_script is null)
                    {
                        // Try to find the script in the witnesses
                        Witness witness = tx.Witnesses[index];
                        witness_script = witness?.VerificationScript;

                        if (witness_script is null || witness_script.Length == 0)
                        {
                            // Then it's a contract-based witness, so try to get the corresponding invocation script for it
                            invocationScript = witness?.InvocationScript;
                        }
                    }
                }

                if (witness_script is null || witness_script.Length == 0)
                {
                    var contract = NativeContract.ContractManagement.GetContract(snapshot, hash);
                    if (contract is null)
                        throw new ArgumentException($"The smart contract or address {hash} is not found");
                    var md = contract.Manifest.Abi.GetMethod("verify", -1);
                    if (md is null)
                        throw new ArgumentException($"The smart contract {contract.Hash} haven't got verify method");
                    if (md.ReturnType != ContractParameterType.Boolean)
                        throw new ArgumentException("The verify method doesn't return boolean value.");
                    if (md.Parameters.Length > 0 && invocationScript is null)
                        throw new ArgumentException("The verify method needs invocation script.");

                    // Empty verification and non-empty invocation scripts
                    var invSize = invocationScript != null ? invocationScript.GetVarSize() : Array.Empty<byte>().GetVarSize();
                    size += Array.Empty<byte>().GetVarSize() + invSize;

                    // Check verify cost
                    using ApplicationEngine engine = ApplicationEngine.Create(TriggerType.Verification, tx, snapshot.CreateSnapshot(), settings: ProtocolSettings);
                    engine.LoadContract(contract, md, CallFlags.ReadOnly);
                    if (invocationScript != null) engine.LoadScript(invocationScript, configureState: p => p.CallFlags = CallFlags.None);
                    if (engine.Execute() == VMState.FAULT) throw new ArgumentException($"Smart contract {contract.Hash} verification fault.");
                    if (!engine.ResultStack.Pop().GetBoolean()) throw new ArgumentException($"Smart contract {contract.Hash} returns false.");

                    networkFee += engine.GasConsumed;
                }
                else if (witness_script.IsSignatureContract())
                {
                    size += 67 + witness_script.GetVarSize();
                    networkFee += exec_fee_factor * SignatureContractCost();
                }
                else if (witness_script.IsMultiSigContract(out int m, out int n))
                {
                    int size_inv = 66 * m;
                    size += IO.Helper.GetVarSize(size_inv) + size_inv + witness_script.GetVarSize();
                    networkFee += exec_fee_factor * MultiSignatureContractCost(m, n);
                }
                else
                {
                    //We can support more contract types in the future.
                }
            }
            networkFee += size * NativeContract.Policy.GetFeePerByte(snapshot);
            return networkFee;
        }

        /// <summary>
        /// Signs the <see cref="IVerifiable"/> in the specified <see cref="ContractParametersContext"/> with the wallet.
        /// </summary>
        /// <param name="context">The <see cref="ContractParametersContext"/> to be used.</param>
        /// <returns><see langword="true"/> if the signature is successfully added to the context; otherwise, <see langword="false"/>.</returns>
        public bool Sign(ContractParametersContext context)
        {
            if (context.Network != ProtocolSettings.Network) return false;
            bool fSuccess = false;
            foreach (UInt160 scriptHash in context.ScriptHashes)
            {
                WalletAccount account = GetAccount(scriptHash);

                if (account != null)
                {
                    // Try to sign self-contained multiSig

                    Contract multiSigContract = account.Contract;

                    if (multiSigContract != null &&
                        multiSigContract.Script.IsMultiSigContract(out int m, out ECPoint[] points))
                    {
                        foreach (var point in points)
                        {
                            account = GetAccount(point);
                            if (account?.HasKey != true) continue;
                            KeyPair key = account.GetKey();
                            byte[] signature = context.Verifiable.Sign(key, context.Network);
                            fSuccess |= context.AddSignature(multiSigContract, key.PublicKey, signature);
                            if (fSuccess) m--;
                            if (context.Completed || m <= 0) break;
                        }
                        continue;
                    }
                    else if (account.HasKey)
                    {
                        // Try to sign with regular accounts
                        KeyPair key = account.GetKey();
                        byte[] signature = context.Verifiable.Sign(key, context.Network);
                        fSuccess |= context.AddSignature(account.Contract, key.PublicKey, signature);
                        continue;
                    }
                }

                // Try Smart contract verification

                var contract = NativeContract.ContractManagement.GetContract(context.Snapshot, scriptHash);

                if (contract != null)
                {
                    var deployed = new DeployedContract(contract);

                    // Only works with verify without parameters

                    if (deployed.ParameterList.Length == 0)
                    {
                        fSuccess |= context.Add(deployed);
                    }
                }
            }

            return fSuccess;
        }

        /// <summary>
        /// Checks that the specified password is correct for the wallet.
        /// </summary>
        /// <param name="password">The password to be checked.</param>
        /// <returns><see langword="true"/> if the password is correct; otherwise, <see langword="false"/>.</returns>
        public abstract bool VerifyPassword(string password);
    }
}
