﻿using AntShares.Core;
using AntShares.Cryptography.ECC;
using AntShares.Wallets;
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AntShares.UI
{
    public partial class AssetRegisterDialog : Form
    {
        public AssetRegisterDialog()
        {
            InitializeComponent();
        }

        public RegisterTransaction GetTransaction()
        {
            return Program.CurrentWallet.MakeTransaction(new RegisterTransaction
            {
                AssetType = (AssetType)comboBox1.SelectedItem,
                Name = (AssetType)comboBox1.SelectedItem == AssetType.Share ? string.Empty : $"[{{\"lang\":\"{CultureInfo.CurrentCulture.Name}\",\"name\":\"{textBox1.Text}\"}}]",
                Amount = checkBox1.Checked ? Fixed8.Parse(textBox2.Text) : -Fixed8.Satoshi,
                Precision = (AssetType)comboBox1.SelectedItem == AssetType.Share ? (byte)0 : (byte)8,
                Issuer = (ECPoint)comboBox2.SelectedItem,
                Admin = Wallet.ToScriptHash(comboBox3.Text),
                Outputs = new TransactionOutput[0]
            }, Fixed8.Zero);
        }

        private void AssetRegisterDialog_Load(object sender, EventArgs e)
        {
            comboBox1.Items.AddRange(new object[] { AssetType.Share, AssetType.Token });
            comboBox2.Items.AddRange(Program.CurrentWallet.GetContracts().Where(p => p.IsStandard).Select(p => Program.CurrentWallet.GetAccount(p.PublicKeyHash)).ToArray());
            comboBox3.Items.AddRange(Program.CurrentWallet.GetContracts().Select(p => p.Address).ToArray());
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            textBox1.Enabled = (AssetType)comboBox1.SelectedItem != AssetType.Share;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            textBox2.Enabled = checkBox1.Checked;
        }
    }
}
