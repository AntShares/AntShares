﻿using AntShares.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AntShares.Core
{
    /// <summary>
    /// 委托交易
	/// 交易规则：
    /// 1. 单个交易中，所有订单的代理人必须是同一人；
    /// 2. 单个交易中，所有订单的交易商品必须完全相同，交易货币也必须完全相同；
    /// 3. 交易商品不能和交易货币相同；
    /// 4. 买盘和卖盘两者都至少需要包含一笔订单；
    /// 5. 交易中不能包含完全未成交的订单，且至多只能包含一笔部分成交的订单；
    /// 6. 如果存在部分成交的订单，则该订单的价格必须是最差的，即：对于买单，它的价格是最低价格；对于卖单，它的价格是最高价格；
    /// 7. 对于买单，需以不高于委托方所指定的价格成交；
    /// 8. 对于卖单，需以不低于委托方所指定的价格成交；
    /// 9. 交易数量精确到10^-4，交易价格精确到10^-4；
    /// </summary>
    public class AgencyTransaction : Transaction
    {
        /// <summary>
        /// 资产编号
        /// </summary>
        public UInt256 AssetId;
        /// <summary>
        /// 货币编号
        /// </summary>
        public UInt256 ValueAssetId;
        /// <summary>
        /// 代理人的合约散列
        /// </summary>
        public UInt160 Agent;
        /// <summary>
        /// 订单列表
        /// </summary>
        public Order[] Orders;
        /// <summary>
        /// 部分成交的订单
        /// </summary>
        public SplitOrder SplitOrder;

        public override int Size => base.Size + AssetId.Size + ValueAssetId.Size + Agent.Size + Orders.Length.GetVarSize() + Orders.Sum(p => p.SizeInTransaction) + SplitOrder.Size;

        public AgencyTransaction()
            : base(TransactionType.AgencyTransaction)
        {
        }
        
        /// <summary>
        /// 反序列化交易中的额外数据
        /// </summary>
        /// <param name="reader">数据来源</param>
        protected override void DeserializeExclusiveData(BinaryReader reader)
        {
            this.AssetId = reader.ReadSerializable<UInt256>();
            this.ValueAssetId = reader.ReadSerializable<UInt256>();
            if (AssetId == ValueAssetId) throw new FormatException();
            this.Agent = reader.ReadSerializable<UInt160>();
            this.Orders = new Order[reader.ReadVarInt(0x10000000)];
            for (int i = 0; i < Orders.Length; i++)
            {
                Orders[i] = new Order();
                Orders[i].DeserializeInTransaction(reader, this);
            }
            if (reader.ReadVarInt(1) == 0)
            {
                this.SplitOrder = null;
            }
            else
            {
                this.SplitOrder = reader.ReadSerializable<SplitOrder>();
            }
        }
       
        /// <summary>
        /// 获取交易中所有的输入
        /// </summary>
        /// <returns>返回交易中所有的输入以及订单<paramref name="Orders"/>中的所有输入</returns>
        public override IEnumerable<TransactionInput> GetAllInputs()
        {
            return Orders.SelectMany(p => p.Inputs).Concat(base.GetAllInputs());
        }

        /// <summary>
        /// 获得需要校验的脚本Hash
        /// </summary>
        /// <returns>返回需要校验的脚本Hash</returns>
        public override UInt160[] GetScriptHashesForVerifying()
        {
            HashSet<UInt160> hashes = new HashSet<UInt160>();
            foreach (var group in Inputs.GroupBy(p => p.PrevHash))
            {
                Transaction tx = Blockchain.Default.GetTransaction(group.Key);
                if (tx == null) throw new InvalidOperationException();
                AgencyTransaction tx_agency = tx as AgencyTransaction;
                if (tx_agency?.SplitOrder == null || tx_agency.AssetId != AssetId || tx_agency.ValueAssetId != ValueAssetId || tx_agency.Agent != Agent)
                {
                    hashes.UnionWith(group.Select(p => tx.Outputs[p.PrevIndex].ScriptHash));
                }
                else
                {
                    hashes.UnionWith(group.Select(p => tx.Outputs[p.PrevIndex].ScriptHash).Where(p => p != tx_agency.SplitOrder.Client));
                }
            }
            hashes.Add(Agent);
            return hashes.OrderBy(p => p).ToArray();
        }

        /// <summary>
        /// 序列化交易中的额外数据
        /// </summary>
        /// <param name="writer">存放序列化后的结果</param>
        protected override void SerializeExclusiveData(BinaryWriter writer)
        {
            writer.Write(AssetId);
            writer.Write(ValueAssetId);
            writer.Write(Agent);
            writer.WriteVarInt(Orders.Length);
            for (int i = 0; i < Orders.Length; i++)
            {
                Orders[i].SerializeInTransaction(writer);
            }
            if (SplitOrder == null)
            {
                writer.WriteVarInt(0);
            }
            else
            {
                writer.WriteVarInt(1);
                writer.Write(SplitOrder);
            }
        }

        //TODO: 此处需要较多的测试来证明它的正确性
        //因为委托交易的验证算法有点太复杂了，
        //考虑未来是否可以优化这个算法
        /// <summary>
        /// 验证交易
        /// </summary>
        /// <returns>返回验证的结果</returns>
        public override bool Verify()
        {
            if (!base.Verify()) return false;
            foreach (Order order in Orders)
                if (!order.VerifySignature())
                    return false;
            RegisterTransaction asset_value = Blockchain.Default.GetTransaction(ValueAssetId) as RegisterTransaction;
            if (asset_value?.AssetType != AssetType.Currency)
                return false;
            List<Order> orders = new List<Order>(Orders);
            foreach (var group in Inputs.GroupBy(p => p.PrevHash))
            {
                Transaction tx = Blockchain.Default.GetTransaction(group.Key);
                if (tx == null) return false;
                AgencyTransaction tx_agency = tx as AgencyTransaction;
                if (tx_agency?.SplitOrder == null || tx_agency.AssetId != AssetId || tx_agency.ValueAssetId != ValueAssetId || tx_agency.Agent != Agent)
                    continue;
                var outputs = group.Select(p => new
                {
                    Input = p,
                    Output = tx_agency.Outputs[p.PrevIndex]
                }).Where(p => p.Output.ScriptHash == tx_agency.SplitOrder.Client).ToDictionary(p => p.Input, p => p.Output);
                if (outputs.Count == 0) continue;
                if (outputs.Count != tx_agency.Outputs.Count(p => p.ScriptHash == tx_agency.SplitOrder.Client))
                    return false;
                orders.Add(new Order
                {
                    AssetId = this.AssetId,
                    ValueAssetId = this.ValueAssetId,
                    Agent = this.Agent,
                    Amount = tx_agency.SplitOrder.Amount,
                    Price = tx_agency.SplitOrder.Price,
                    Client = tx_agency.SplitOrder.Client,
                    Inputs = outputs.Keys.ToArray()
                });
            }
            if (orders.Count < 2) return false;
            if (orders.Count(p => p.Amount > Fixed8.Zero) == 0 || orders.Count(p => p.Amount < Fixed8.Zero) == 0)
                return false;
            Fixed8 amount_unmatched = orders.Sum(p => p.Amount);
            if (amount_unmatched == Fixed8.Zero)
            {
                if (SplitOrder != null) return false;
            }
            else
            {
                if (SplitOrder?.Amount != amount_unmatched) return false;
            }
            foreach (Order order in orders)
            {
                TransactionOutput[] inputs = order.Inputs.Select(p => References[p]).ToArray();
                if (order.Amount > Fixed8.Zero)
                {
                    if (inputs.Any(p => p.AssetId != order.ValueAssetId)) return false;
                    if (inputs.Sum(p => p.Value) < order.Amount * order.Price) return false;
                }
                else
                {
                    if (inputs.Any(p => p.AssetId != order.AssetId)) return false;
                    if (inputs.Sum(p => p.Value) < order.Amount) return false;
                }
            }
            if (SplitOrder != null)
            {
                Fixed8 price_worst = amount_unmatched > Fixed8.Zero ? orders.Min(p => p.Price) : orders.Max(p => p.Price);
                if (SplitOrder.Price != price_worst) return false;
                Order[] orders_worst = orders.Where(p => p.Price == price_worst && p.Client == SplitOrder.Client).ToArray();
                if (orders_worst.Length == 0) return false;
                Fixed8 amount_worst = orders_worst.Sum(p => p.Amount);
                if (amount_worst.Abs() < amount_unmatched.Abs()) return false;
                Order order_combine = new Order
                {
                    AssetId = this.AssetId,
                    ValueAssetId = this.ValueAssetId,
                    Agent = this.Agent,
                    Amount = amount_worst - amount_unmatched,
                    Price = price_worst,
                    Client = SplitOrder.Client,
                    Inputs = orders_worst.SelectMany(p => p.Inputs).ToArray()
                };
                foreach (Order order_worst in orders_worst)
                {
                    orders.Remove(order_worst);
                }
                orders.Add(order_combine);
            }
            foreach (var group in orders.GroupBy(p => p.Client))
            {
                TransactionOutput[] inputs = group.SelectMany(p => p.Inputs).Select(p => References[p]).ToArray();
                TransactionOutput[] outputs = Outputs.Where(p => p.ScriptHash == group.Key).ToArray();
                Fixed8 money_spent = inputs.Where(p => p.AssetId == ValueAssetId).Sum(p => p.Value) - outputs.Where(p => p.AssetId == ValueAssetId).Sum(p => p.Value);
                Fixed8 amount_changed = outputs.Where(p => p.AssetId == AssetId).Sum(p => p.Value) - inputs.Where(p => p.AssetId == AssetId).Sum(p => p.Value);
                if (amount_changed != group.Sum(p => p.Amount)) return false;
                if (money_spent > group.Sum(p => p.Amount * p.Price)) return false;
            }
            return true;
        }
    }
}
