using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Network.P2P.Payloads;
using Neo.Plugins;
using System;

namespace Neo.UnitTests.Plugins
{
    [TestClass]
    public class UT_Plugin
    {
        private static readonly object locker = new();

        private class DummyP2PPlugin : IP2PPlugin
        {
            public void OnVerifiedInventory(IInventory inventory) { }
        }
        private class DummyPersistencePlugin : IPersistencePlugin { }

        [TestMethod]
        public void TestIP2PPlugin()
        {
            var pp = new DummyP2PPlugin() as IP2PPlugin;
            Assert.IsTrue(pp.OnP2PMessage(null, null));
        }

        [TestMethod]
        public void TestIPersistencePlugin()
        {
            var pp = new DummyPersistencePlugin() as IPersistencePlugin;

            Assert.IsFalse(pp.ShouldThrowExceptionFromCommit(null));

            // With empty default implementation

            pp.OnCommit(null, null, null);
            pp.OnPersist(null, null, null, null);
        }

        [TestMethod]
        public void TestGetConfigFile()
        {
            // var pp = new TestLogPlugin();
            // var file = pp.ConfigFile;
            // file.EndsWith("config.json").Should().BeTrue();
        }

        [TestMethod]
        public void TestGetName()
        {
            var pp = new TestLogPlugin();
            pp.Name.Should().Be("TestLogPlugin");
        }

        [TestMethod]
        public void TestGetVersion()
        {
            var pp = new TestLogPlugin();
            Action action = () => pp.Version.ToString();
            action.Should().NotThrow();
        }

        [TestMethod]
        [Ignore]
        public void TestLog()
        {
            var lp = new TestLogPlugin();
            lp.LogMessage("Hello");
            lp.Output.Should().Be("Plugin:TestLogPlugin_Info_Hello");
        }

        [TestMethod]
        [Ignore]
        public void TestSendMessage()
        {
            lock (locker)
            {
                // Plugin.Plugins.Clear();
                // Plugin.SendMessage("hey1").Should().BeFalse();

                // var lp = new TestLogPlugin();
                // Plugin.SendMessage("hey2").Should().BeTrue();
            }
        }

        [TestMethod]
        [Ignore]
        public void TestGetConfiguration()
        {
            // var pp = new TestLogPlugin();
            // pp.TestGetConfiguration().Key.Should().Be("PluginConfiguration");
        }
    }
}
