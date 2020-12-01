using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO;
using Neo.SmartContract;
using Neo.VM;
using System.Numerics;

namespace Neo.UnitTests.SmartContract
{
    [TestClass]
    public class UT_Helper
    {
        [TestMethod]
        public void TestGetContractHash()
        {
            var nef = new NefFile()
            {
                Compiler = "test",
                Version = new System.Version().ToString(),
                Script = new byte[] { 1, 2, 3 }
            };
            nef.CheckSum = NefFile.ComputeChecksum(nef);

            Assert.AreEqual("0xb4b7417195feca1cdb5a99504ab641d8c220ae99", Neo.SmartContract.Helper.GetContractHash(UInt160.Zero, nef.Script).ToString());
            Assert.AreEqual("0xe56e4ee87f89a70e9138432c387ad49f2ee5b55f", Neo.SmartContract.Helper.GetContractHash(UInt160.Parse("0xa400ff00ff00ff00ff00ff00ff00ff00ff00ff01"), nef.Script).ToString());
        }

        [TestMethod]
        public void TestIsMultiSigContract()
        {
            var case1 = new byte[]
            {
                0, 2, 12, 33, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221,
                221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 221, 12, 33, 255, 255, 255, 255,
                255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
                255, 255, 255, 255, 255, 255, 255, 255, 0,
            };
            Assert.IsFalse(case1.IsMultiSigContract());

            var case2 = new byte[]
            {
                18, 12, 33, 2, 111, 240, 59, 148, 146, 65, 206, 29, 173, 212, 53, 25, 230, 150, 14, 10, 133, 180, 26,
                105, 160, 92, 50, 129, 3, 170, 43, 206, 21, 148, 202, 22, 12, 33, 2, 111, 240, 59, 148, 146, 65, 206,
                29, 173, 212, 53, 25, 230, 150, 14, 10, 133, 180, 26, 105, 160, 92, 50, 129, 3, 170, 43, 206, 21, 148,
                202, 22, 18
            };
            Assert.IsFalse(case2.IsMultiSigContract());
        }

        [TestMethod]
        public void TestIsSolidTransfer()
        {
            // PUSHX

            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitPush(12);
                sb.EmitAppCall(UInt160.Zero, "balanceOf", UInt160.Parse("01ff00ff00ff00ff00ff00ff00ff00ff00ff00a4"));
                sb.Emit(OpCode.EQUAL);
                sb.Emit(OpCode.ASSERT);

                Assert.IsTrue(sb.ToArray().IsSolidTransfer());
            }

            // PUSHM1

            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitPush(-1);
                sb.EmitAppCall(UInt160.Zero, "balanceOf", UInt160.Parse("01ff00ff00ff00ff00ff00ff00ff00ff00ff00a4"));
                sb.Emit(OpCode.EQUAL);
                sb.Emit(OpCode.ASSERT);

                Assert.IsTrue(sb.ToArray().IsSolidTransfer());
            }

            // PUSHINT8

            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.Emit(OpCode.PUSHINT8, new byte[] { (byte)200 });
                sb.EmitAppCall(UInt160.Zero, "balanceOf", UInt160.Parse("01ff00ff00ff00ff00ff00ff00ff00ff00ff00a4"));
                sb.Emit(OpCode.EQUAL);
                sb.Emit(OpCode.ASSERT);

                Assert.IsTrue(sb.ToArray().IsSolidTransfer());
            }

            // PUSHINT16

            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitPush(-255);
                sb.EmitAppCall(UInt160.Zero, "balanceOf", UInt160.Parse("01ff00ff00ff00ff00ff00ff00ff00ff00ff00a4"));
                sb.Emit(OpCode.EQUAL);
                sb.Emit(OpCode.ASSERT);

                Assert.IsTrue(sb.ToArray().IsSolidTransfer());
            }

            // PUSHINT32

            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitPush(int.MaxValue);
                sb.EmitAppCall(UInt160.Zero, "balanceOf", UInt160.Parse("01ff00ff00ff00ff00ff00ff00ff00ff00ff00a4"));
                sb.Emit(OpCode.EQUAL);
                sb.Emit(OpCode.ASSERT);

                Assert.IsTrue(sb.ToArray().IsSolidTransfer());
            }

            // PUSHINT64

            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitPush(long.MaxValue);
                sb.EmitAppCall(UInt160.Zero, "balanceOf", UInt160.Parse("01ff00ff00ff00ff00ff00ff00ff00ff00ff00a4"));
                sb.Emit(OpCode.EQUAL);
                sb.Emit(OpCode.ASSERT);

                Assert.IsTrue(sb.ToArray().IsSolidTransfer());
            }

            // PUSHINT128

            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitPush(new BigInteger(long.MaxValue) * new BigInteger(long.MaxValue));
                sb.EmitAppCall(UInt160.Zero, "balanceOf", UInt160.Parse("01ff00ff00ff00ff00ff00ff00ff00ff00ff00a4"));
                sb.Emit(OpCode.EQUAL);
                sb.Emit(OpCode.ASSERT);

                Assert.IsTrue(sb.ToArray().IsSolidTransfer());
            }

            // PUSHINT256

            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitPush(new BigInteger(long.MaxValue) * new BigInteger(long.MaxValue) * new BigInteger(long.MaxValue) * new BigInteger(long.MaxValue));
                sb.EmitAppCall(UInt160.Zero, "balanceOf", UInt160.Parse("01ff00ff00ff00ff00ff00ff00ff00ff00ff00a4"));
                sb.Emit(OpCode.EQUAL);
                sb.Emit(OpCode.ASSERT);

                Assert.IsTrue(sb.ToArray().IsSolidTransfer());
            }
        }
    }
}
