﻿using AntShares.Cryptography;
using AntShares.Cryptography.ECC;
using AntShares.IO;
using AntShares.VM;
using AntShares.Wallets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AntShares.Core
{
    /// <summary>
    /// 实现区块链功能的基类
    /// </summary>
    public abstract class Blockchain : IDisposable, IScriptTable
    {
        /// <summary>
        /// 当区块被写入到硬盘后触发
        /// </summary>
        public static event EventHandler<Block> PersistCompleted;

        /// <summary>
        /// 产生每个区块的时间间隔，已秒为单位
        /// </summary>
        public const uint SecondsPerBlock = 15;
        /// <summary>
        /// 小蚁币产量递减的时间间隔，以区块数量为单位
        /// </summary>
        public const uint DecrementInterval = 2000000;
        /// <summary>
        /// 每个区块产生的小蚁币的数量
        /// </summary>
        public static readonly uint[] MintingAmount = { 8, 7, 6, 5, 4, 3, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        /// <summary>
        /// 产生每个区块的时间间隔
        /// </summary>
        public static readonly TimeSpan TimePerBlock = TimeSpan.FromSeconds(SecondsPerBlock);
        /// <summary>
        /// 后备记账人列表
        /// </summary>
        public static readonly ECPoint[] StandbyMiners = Settings.Default.StandbyMiners.OfType<string>().Select(p => ECPoint.DecodePoint(p.HexToBytes(), ECCurve.Secp256r1)).ToArray();

        /// <summary>
        /// 小蚁股
        /// </summary>
        public static readonly RegisterTransaction AntShare = new RegisterTransaction
        {
            AssetType = AssetType.AntShare,
            Name = "[{\"lang\":\"zh-CN\",\"name\":\"小蚁股\"},{\"lang\":\"en\",\"name\":\"AntShare\"}]",
            Amount = Fixed8.FromDecimal(100000000),
            Precision = 0,
            Owner = ECCurve.Secp256r1.Infinity,
            Admin = (new[] { (byte)OpCode.PUSHT }).ToScriptHash(),
            Attributes = new TransactionAttribute[0],
            Inputs = new CoinReference[0],
            Outputs = new TransactionOutput[0],
            Scripts = new Witness[0]
        };

        /// <summary>
        /// 小蚁币
        /// </summary>
        public static readonly RegisterTransaction AntCoin = new RegisterTransaction
        {
            AssetType = AssetType.AntCoin,
            Name = "[{\"lang\":\"zh-CN\",\"name\":\"小蚁币\"},{\"lang\":\"en\",\"name\":\"AntCoin\"}]",
            Amount = Fixed8.FromDecimal(MintingAmount.Sum(p => p * DecrementInterval)),
            Precision = 8,
            Owner = ECCurve.Secp256r1.Infinity,
            Admin = (new[] { (byte)OpCode.PUSHF }).ToScriptHash(),
            Attributes = new TransactionAttribute[0],
            Inputs = new CoinReference[0],
            Outputs = new TransactionOutput[0],
            Scripts = new Witness[0]
        };

        /// <summary>
        /// 创世区块
        /// </summary>
        public static readonly Block GenesisBlock = new Block
        {
            PrevBlock = UInt256.Zero,
            Timestamp = (new DateTime(2016, 7, 15, 15, 8, 21, DateTimeKind.Utc)).ToTimestamp(),
            Height = 0,
            ConsensusData = 2083236893, //向比特币致敬
            NextMiner = GetMinerAddress(StandbyMiners),
            Script = new Witness
            {
                StackScript = new byte[0],
                RedeemScript = new[] { (byte)OpCode.PUSHT }
            },
            Transactions = new Transaction[]
            {
                new MinerTransaction
                {
                    Nonce = 2083236893,
                    Attributes = new TransactionAttribute[0],
                    Inputs = new CoinReference[0],
                    Outputs = new TransactionOutput[0],
                    Scripts = new Witness[0]
                },
                AntShare,
                AntCoin,
                new IssueTransaction
                {
                    Attributes = new TransactionAttribute[0],
                    Inputs = new CoinReference[0],
                    Outputs = new[]
                    {
                        new TransactionOutput
                        {
                            AssetId = AntShare.Hash,
                            Value = AntShare.Amount,
                            ScriptHash = Contract.CreateMultiSigRedeemScript(StandbyMiners.Length / 2 + 1, StandbyMiners).ToScriptHash()
                        }
                    },
                    Scripts = new[]
                    {
                        new Witness
                        {
                            StackScript = new byte[0],
                            RedeemScript = new[] { (byte)OpCode.PUSHT }
                        }
                    }
                }
            }
        };

        /// <summary>
        /// 区块链所提供的功能
        /// </summary>
        public abstract BlockchainAbility Ability { get; }
        /// <summary>
        /// 当前最新区块散列值
        /// </summary>
        public abstract UInt256 CurrentBlockHash { get; }
        /// <summary>
        /// 当前最新区块头的散列值
        /// </summary>
        public virtual UInt256 CurrentHeaderHash => CurrentBlockHash;
        /// <summary>
        /// 默认的区块链实例
        /// </summary>
        public static Blockchain Default { get; private set; } = null;
        /// <summary>
        /// 区块头高度
        /// </summary>
        public virtual uint HeaderHeight => Height;
        /// <summary>
        /// 区块高度
        /// </summary>
        public abstract uint Height { get; }
        /// <summary>
        /// 表示当前的区块链实现是否为只读的
        /// </summary>
        public abstract bool IsReadOnly { get; }

        static Blockchain()
        {
            GenesisBlock.RebuildMerkleRoot();
        }

        /// <summary>
        /// 将指定的区块添加到区块链中
        /// </summary>
        /// <param name="block">要添加的区块</param>
        /// <returns>返回是否添加成功</returns>
        public abstract bool AddBlock(Block block);

        /// <summary>
        /// 将指定的区块头添加到区块头链中
        /// </summary>
        /// <param name="headers">要添加的区块头列表</param>
        protected internal abstract void AddHeaders(IEnumerable<Header> headers);

        /// <summary>
        /// 判断区块链中是否包含指定的区块
        /// </summary>
        /// <param name="hash">区块编号</param>
        /// <returns>如果包含指定区块则返回true</returns>
        public virtual bool ContainsBlock(UInt256 hash)
        {
            return hash == GenesisBlock.Hash;
        }

        /// <summary>
        /// 判断区块链中是否包含指定的交易
        /// </summary>
        /// <param name="hash">交易编号</param>
        /// <returns>如果包含指定交易则返回true</returns>
        public virtual bool ContainsTransaction(UInt256 hash)
        {
            return GenesisBlock.Transactions.Any(p => p.Hash == hash);
        }

        public bool ContainsUnspent(CoinReference input)
        {
            return ContainsUnspent(input.PrevHash, input.PrevIndex);
        }

        public abstract bool ContainsUnspent(UInt256 hash, ushort index);

        public abstract void Dispose();

        public abstract AccountState GetAccountState(UInt160 script_hash);

        public abstract AssetState GetAssetState(UInt256 asset_id);

        /// <summary>
        /// 根据指定的高度，返回对应的区块信息
        /// </summary>
        /// <param name="height">区块高度</param>
        /// <returns>返回对应的区块信息</returns>
        public Block GetBlock(uint height)
        {
            return GetBlock(GetBlockHash(height));
        }

        /// <summary>
        /// 根据指定的散列值，返回对应的区块信息
        /// </summary>
        /// <param name="hash">散列值</param>
        /// <returns>返回对应的区块信息</returns>
        public virtual Block GetBlock(UInt256 hash)
        {
            if (hash == GenesisBlock.Hash)
                return GenesisBlock;
            return null;
        }

        /// <summary>
        /// 根据指定的高度，返回对应区块的散列值
        /// </summary>
        /// <param name="height">区块高度</param>
        /// <returns>返回对应区块的散列值</returns>
        public virtual UInt256 GetBlockHash(uint height)
        {
            if (height == 0) return GenesisBlock.Hash;
            return null;
        }

        public virtual ContractState GetContract(UInt160 hash)
        {
            return null;
        }

        public IEnumerable<ValidatorState> GetEnrollments()
        {
            return GetEnrollments(Enumerable.Empty<Transaction>());
        }

        public abstract IEnumerable<ValidatorState> GetEnrollments(IEnumerable<Transaction> others);

        /// <summary>
        /// 根据指定的高度，返回对应的区块头信息
        /// </summary>
        /// <param name="height">区块高度</param>
        /// <returns>返回对应的区块头信息</returns>
        public virtual Header GetHeader(uint height)
        {
            return GetHeader(GetBlockHash(height));
        }

        /// <summary>
        /// 根据指定的散列值，返回对应的区块头信息
        /// </summary>
        /// <param name="hash">散列值</param>
        /// <returns>返回对应的区块头信息</returns>
        public virtual Header GetHeader(UInt256 hash)
        {
            return GetBlock(hash)?.Header;
        }

        /// <summary>
        /// 获取记账人的合约地址
        /// </summary>
        /// <param name="miners">记账人的公钥列表</param>
        /// <returns>返回记账人的合约地址</returns>
        public static UInt160 GetMinerAddress(ECPoint[] miners)
        {
            return Contract.CreateMultiSigRedeemScript(miners.Length - (miners.Length - 1) / 3, miners).ToScriptHash();
        }

        private List<ECPoint> _miners = new List<ECPoint>();
        /// <summary>
        /// 获取下一个区块的记账人列表
        /// </summary>
        /// <returns>返回一组公钥，表示下一个区块的记账人列表</returns>
        public ECPoint[] GetMiners()
        {
            lock (_miners)
            {
                if (_miners.Count == 0)
                {
                    _miners.AddRange(GetMiners(Enumerable.Empty<Transaction>()));
                }
                return _miners.ToArray();
            }
        }

        public virtual IEnumerable<ECPoint> GetMiners(IEnumerable<Transaction> others)
        {
            if (!Ability.HasFlag(BlockchainAbility.TransactionIndexes) || !Ability.HasFlag(BlockchainAbility.UnspentIndexes))
                throw new NotSupportedException();
            //TODO: 此处排序可能将耗费大量内存，考虑是否采用其它机制
            VoteState[] votes = GetVotes(others).OrderBy(p => p.PublicKeys.Length).ToArray();
            int miner_count = (int)votes.WeightedFilter(0.25, 0.75, p => p.Count.GetData(), (p, w) => new
            {
                MinerCount = p.PublicKeys.Length,
                Weight = w
            }).WeightedAverage(p => p.MinerCount, p => p.Weight);
            miner_count = Math.Max(miner_count, StandbyMiners.Length);
            Dictionary<ECPoint, Fixed8> miners = GetEnrollments(others).ToDictionary(p => p.PublicKey, p => Fixed8.Zero);
            foreach (var vote in votes)
            {
                foreach (ECPoint pubkey in vote.PublicKeys.Take(miner_count))
                {
                    if (miners.ContainsKey(pubkey))
                        miners[pubkey] += vote.Count;
                }
            }
            return miners.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Select(p => p.Key).Concat(StandbyMiners).Take(miner_count);
        }

        /// <summary>
        /// 根据指定的散列值，返回下一个区块的信息
        /// </summary>
        /// <param name="hash">散列值</param>
        /// <returns>返回下一个区块的信息>
        public abstract Block GetNextBlock(UInt256 hash);

        /// <summary>
        /// 根据指定的散列值，返回下一个区块的散列值
        /// </summary>
        /// <param name="hash">散列值</param>
        /// <returns>返回下一个区块的散列值</returns>
        public abstract UInt256 GetNextBlockHash(UInt256 hash);

        byte[] IScriptTable.GetScript(byte[] script_hash)
        {
            return GetContract(new UInt160(script_hash)).Script;
        }

        /// <summary>
        /// 根据指定的区块高度，返回对应区块及之前所有区块中包含的系统费用的总量
        /// </summary>
        /// <param name="height">区块高度</param>
        /// <returns>返回对应的系统费用的总量</returns>
        public virtual long GetSysFeeAmount(uint height)
        {
            return GetSysFeeAmount(GetBlockHash(height));
        }

        /// <summary>
        /// 根据指定的区块散列值，返回对应区块及之前所有区块中包含的系统费用的总量
        /// </summary>
        /// <param name="hash">散列值</param>
        /// <returns>返回系统费用的总量</returns>
        public abstract long GetSysFeeAmount(UInt256 hash);

        /// <summary>
        /// 根据指定的散列值，返回对应的交易信息
        /// </summary>
        /// <param name="hash">散列值</param>
        /// <returns>返回对应的交易信息</returns>
        public Transaction GetTransaction(UInt256 hash)
        {
            int height;
            return GetTransaction(hash, out height);
        }

        /// <summary>
        /// 根据指定的散列值，返回对应的交易信息与该交易所在区块的高度
        /// </summary>
        /// <param name="hash">交易散列值</param>
        /// <param name="height">返回该交易所在区块的高度</param>
        /// <returns>返回对应的交易信息</returns>
        public virtual Transaction GetTransaction(UInt256 hash, out int height)
        {
            Transaction tx = GenesisBlock.Transactions.FirstOrDefault(p => p.Hash == hash);
            if (tx != null)
            {
                height = 0;
                return tx;
            }
            height = -1;
            return null;
        }

        public abstract Dictionary<ushort, SpentCoin> GetUnclaimed(UInt256 hash);

        /// <summary>
        /// 根据指定的散列值和索引，获取对应的未花费的资产
        /// </summary>
        /// <param name="hash">交易散列值</param>
        /// <param name="index">输出的索引</param>
        /// <returns>返回一个交易输出，表示一个未花费的资产</returns>
        public abstract TransactionOutput GetUnspent(UInt256 hash, ushort index);

        /// <summary>
        /// 获取选票信息
        /// </summary>
        /// <returns>返回一个选票列表，包含当前区块链中所有有效的选票</returns>
        public IEnumerable<VoteState> GetVotes()
        {
            return GetVotes(Enumerable.Empty<Transaction>());
        }

        public abstract IEnumerable<VoteState> GetVotes(IEnumerable<Transaction> others);

        /// <summary>
        /// 判断交易是否双花
        /// </summary>
        /// <param name="tx">交易</param>
        /// <returns>返回交易是否双花</returns>
        public abstract bool IsDoubleSpend(Transaction tx);

        /// <summary>
        /// 当区块被写入到硬盘后调用
        /// </summary>
        /// <param name="block">区块</param>
        protected void OnPersistCompleted(Block block)
        {
            lock (_miners)
            {
                _miners.Clear();
            }
            if (PersistCompleted != null) PersistCompleted(this, block);
        }

        /// <summary>
        /// 注册默认的区块链实例
        /// </summary>
        /// <param name="blockchain">区块链实例</param>
        /// <returns>返回注册后的区块链实例</returns>
        public static Blockchain RegisterBlockchain(Blockchain blockchain)
        {
            if (blockchain == null) throw new ArgumentNullException();
            if (Default != null) Default.Dispose();
            Default = blockchain;
            return blockchain;
        }
    }
}
