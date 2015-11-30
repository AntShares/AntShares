﻿using AntShares.Core;
using AntShares.Core.Scripts;
using AntShares.Cryptography;
using AntShares.Cryptography.ECC;
using AntShares.IO;
using AntShares.Network;
using AntShares.Wallets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Transaction = AntShares.Core.Transaction;

namespace AntShares.UI
{
    internal partial class DeveloperToolsForm : Form
    {
        private static readonly int[] magic = { 38, 38, 40, 40, 37, 39, 37, 39, 65, 66, 65, 66 };
        private List<int> chars = new List<int>();

        public DeveloperToolsForm()
        {
            InitializeComponent();
            tabControl1.TabPages.Remove(tabPage100);
        }

        private void DeveloperToolsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (!tabControl1.TabPages.Contains(tabPage100))
            {
                chars.Add(e.KeyValue);
                if (chars.Count >= magic.Length && chars.Skip(chars.Count - magic.Length).SequenceEqual(magic))
                {
                    tabControl1.TabPages.Add(tabPage100);
                    tabControl1.SelectedTab = tabPage100;
                    this.KeyDown -= DeveloperToolsForm_KeyDown;
                }
            }
        }

        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            button6.Enabled = numericUpDown2.Value > 0;
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            button5.Enabled = listBox1.SelectedIndices.Count > 0;
        }

        private void textBox5_TextChanged(object sender, EventArgs e)
        {
            button4.Enabled = textBox5.TextLength > 0;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            listBox1.Items.Add(textBox5.Text);
            textBox5.Clear();
            numericUpDown2.Maximum = listBox1.Items.Count;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            listBox1.Items.RemoveAt(listBox1.SelectedIndex);
            numericUpDown2.Maximum = listBox1.Items.Count;
        }

        private void button6_Click(object sender, EventArgs e)
        {
            byte[] redeemScript = Contract.CreateMultiSigRedeemScript((int)numericUpDown2.Value, listBox1.Items.OfType<string>().Select(p => ECPoint.DecodePoint(p.HexToBytes(), ECCurve.Secp256r1)).ToArray());
            textBox6.Text = Wallet.ToAddress(redeemScript.ToScriptHash()).ToString();
            textBox7.Text = redeemScript.ToHexString();
        }

        private void textBox8_TextChanged(object sender, EventArgs e)
        {
            button7.Enabled = textBox8.TextLength > 0;
            button8.Enabled = textBox8.TextLength > 0;
        }

        private void button7_Click(object sender, EventArgs e)
        {
            SignatureContext context = SignatureContext.Parse(textBox8.Text);
            context.Signable.Scripts = context.GetScripts();
            InformationBox.Show(context.Signable.ToArray().ToHexString());
        }

        private async void button8_Click(object sender, EventArgs e)
        {
            SignatureContext context = SignatureContext.Parse(textBox8.Text);
            context.Signable.Scripts = context.GetScripts();
            await Program.LocalNode.RelayAsync((Inventory)context.Signable);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            RegisterTransaction antshare = new RegisterTransaction
            {
                AssetType = AssetType.AntShare,
                Name = "[{'lang':'zh-CN','name':'小蚁股'},{'lang':'en','name':'AntShare'}]",
                Amount = Fixed8.FromDecimal(numericUpDown1.Value),
                Issuer = Wallet.ToScriptHash(textBox1.Text),
                Admin = Wallet.ToScriptHash(textBox2.Text),
                Inputs = new TransactionInput[0],
                Outputs = new TransactionOutput[0]
            };
            SignatureContext context = new SignatureContext(antshare);
            InformationBox.Show(context.ToString(), "小蚁股签名上下文");
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Block block = new Block
            {
                PrevBlock = UInt256.Zero,
                Timestamp = DateTime.Now.ToTimestamp(),
                Height = 0,
                Nonce = 2083236893, //向比特币致敬
                NextMiner = Blockchain.GetMinerAddress(Blockchain.StandbyMiners),
                Transactions = new Transaction[]
                {
                    new GenerationTransaction
                    {
                        Nonce = 0,
                        Inputs = new TransactionInput[0],
                        Outputs = new TransactionOutput[0],
                        Scripts = new Script[0]
                    },
                    textBox3.Text.HexToBytes().AsSerializable<RegisterTransaction>()
                }
            };
            block.RebuildMerkleRoot();
            SignatureContext context = new SignatureContext(block.Header);
            InformationBox.Show(context.ToString(), "创世区块头签名上下文");
        }

        private void button3_Click(object sender, EventArgs e)
        {
            Block block = textBox4.Text.HexToBytes().AsSerializable<Block>();
            block.Transactions = new Transaction[]
            {
                new GenerationTransaction
                {
                    Nonce = 0,
                    Inputs = new TransactionInput[0],
                    Outputs = new TransactionOutput[0],
                    Scripts = new Script[0]
                },
                textBox3.Text.HexToBytes().AsSerializable<RegisterTransaction>()
            };
            Debug.Assert(MerkleTree.ComputeRoot(block.Transactions.Select(p => p.Hash).ToArray()) == block.MerkleRoot);
            InformationBox.Show(block.ToArray().ToHexString(), "创世区块");
        }
    }
}
