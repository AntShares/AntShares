﻿using AntShares.Core;
using AntShares.Implementations.Blockchains.LevelDB;
using AntShares.Implementations.Wallets.EntityFramework;
using AntShares.Network;
using AntShares.Properties;
using AntShares.UI;
using System;
using System.Windows.Forms;

namespace AntShares
{
    internal static class Program
    {
        public static readonly LocalNode LocalNode;
        public static UserWallet CurrentWallet;

        static Program()
        {
            Blockchain.RegisterBlockchain(new LevelDBBlockchain(Settings.Default.DataDirectoryPath));
            LocalNode = new LocalNode(Settings.Default.NodePort);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            LocalNode.Dispose();
            Blockchain.Default.Dispose();
        }
    }
}
