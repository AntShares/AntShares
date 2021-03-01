using Neo.Cryptography;
using Neo.IO;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Org.BouncyCastle.Crypto.Generators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using static Neo.Wallets.Helper;
using ECPoint = Neo.Cryptography.ECC.ECPoint;

namespace Neo.Wallets
{
    public abstract class Wallet
    {
        public ProtocolSettings ProtocolSettings { get; }
        public abstract string Name { get; }
        public string Path { get; }
        public abstract Version Version { get; }

        public abstract bool ChangePassword(string oldPassword, string newPassword);
        public abstract bool Contains(UInt160 scriptHash);
        public abstract WalletAccount CreateAccount(byte[] privateKey);
        public abstract WalletAccount CreateAccount(Contract contract, KeyPair key = null);
        public abstract WalletAccount CreateAccount(UInt160 scriptHash);
        public abstract bool DeleteAccount(UInt160 scriptHash);
        public abstract WalletAccount GetAccount(UInt160 scriptHash);
        public abstract IEnumerable<WalletAccount> GetAccounts();

        protected Wallet(string path, ProtocolSettings settings)
        {
            this.ProtocolSettings = settings;
            this.Path = path;
        }

        public WalletAccount CreateAccount()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public WalletAccount CreateAccount(Contract contract, byte[] privateKey)
        {
            if (privateKey == null) return CreateAccount(contract);
            return CreateAccount(contract, new KeyPair(privateKey));
        }

        private List<(UInt160 Account, BigInteger Value)> FindPayingAccounts(List<(UInt160 Account, BigInteger Value)> orderedAccounts, BigInteger amount)
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

        public WalletAccount GetAccount(ECPoint pubkey)
        {
            return GetAccount(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash());
        }

        public BigDecimal GetAvailable(DataCache snapshot, UInt160 asset_id)
        {
            UInt160[] accounts = GetAccounts().Where(p => !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
            return GetBalance(snapshot, asset_id, accounts);
        }

        public BigDecimal GetBalance(DataCache snapshot, UInt160 asset_id, params UInt160[] accounts)
        {
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
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
            using ApplicationEngine engine = ApplicationEngine.Run(script, snapshot, settings: ProtocolSettings, gas: 20000000L * accounts.Length);
            if (engine.State == VMState.FAULT)
                return new BigDecimal(BigInteger.Zero, 0);
            byte decimals = (byte)engine.ResultStack.Pop().GetInteger();
            BigInteger amount = engine.ResultStack.Pop().GetInteger();
            return new BigDecimal(amount, decimals);
        }

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
            byte[] prikey = XOR(encryptedkey.AES256Decrypt(derivedhalf2), derivedhalf1);
            Array.Clear(derivedhalf1, 0, derivedhalf1.Length);
            Array.Clear(derivedhalf2, 0, derivedhalf2.Length);
            ECPoint pubkey = Cryptography.ECC.ECCurve.Secp256r1.G * prikey;
            UInt160 script_hash = Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();
            string address = script_hash.ToAddress(version);
            if (!Encoding.ASCII.GetBytes(address).Sha256().Sha256().AsSpan(0, 4).SequenceEqual(addresshash))
                throw new FormatException();
            return prikey;
        }

        public static byte[] GetPrivateKeyFromWIF(string wif)
        {
            if (wif == null) throw new ArgumentNullException();
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
                    List<Signer> list = new List<Signer>(cosigners);
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

        public virtual WalletAccount Import(string wif)
        {
            byte[] privateKey = GetPrivateKeyFromWIF(wif);
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public virtual WalletAccount Import(string nep2, string passphrase, int N = 16384, int r = 8, int p = 8)
        {
            byte[] privateKey = GetPrivateKeyFromNEP2(nep2, passphrase, ProtocolSettings.AddressVersion, N, r, p);
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

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
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                foreach (var (assetId, group, sum) in outputs.GroupBy(p => p.AssetId, (k, g) => (k, g, g.Select(p => p.Value.Value).Sum())))
                {
                    var balances = new List<(UInt160 Account, BigInteger Value)>();
                    foreach (UInt160 account in accounts)
                        using (ScriptBuilder sb2 = new ScriptBuilder())
                        {
                            sb2.EmitDynamicCall(assetId, "balanceOf", CallFlags.ReadOnly, account);
                            using (ApplicationEngine engine = ApplicationEngine.Run(sb2.ToArray(), snapshot, settings: ProtocolSettings))
                            {
                                if (engine.State != VMState.HALT)
                                    throw new InvalidOperationException($"Execution for {assetId}.balanceOf('{account}' fault");
                                BigInteger value = engine.ResultStack.Pop().GetInteger();
                                if (value.Sign > 0) balances.Add((account, value));
                            }
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
            Random rand = new Random();
            foreach (var (account, value) in balances_gas)
            {
                Transaction tx = new Transaction
                {
                    Version = 0,
                    Nonce = (uint)rand.Next(),
                    Script = script,
                    ValidUntilBlock = NativeContract.Ledger.CurrentIndex(snapshot) + Transaction.MaxValidUntilBlockIncrement,
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

                tx.NetworkFee = CalculateNetworkFee(snapshot, tx, ProtocolSettings, (a) => GetAccount(a)?.Contract?.Script);
                if (value >= tx.SystemFee + tx.NetworkFee) return tx;
            }
            throw new InvalidOperationException("Insufficient GAS");
        }

        public bool Sign(ContractParametersContext context)
        {
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
                            byte[] signature = context.Verifiable.Sign(key, ProtocolSettings.Magic);
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
                        byte[] signature = context.Verifiable.Sign(key, ProtocolSettings.Magic);
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

        public abstract bool VerifyPassword(string password);
    }
}
