﻿using Neo.VM;
using Neo.VM.Types;
using System.Numerics;
using Neo.Ledger;

namespace Neo.SmartContract.Native.Tokens
{
    public class Nep5AccountState
    {
        public BigInteger Balance;

        public readonly byte[] balanceSuffix = new byte[1]{ 0x01 };

        public StorageKey CreateAccountBalanceKey(byte Prefix_Account, UInt160 account)
        {
            return NativeContract.CreateStorageKey(Prefix_Account, account, balanceSuffix);
        }

        public Nep5AccountState()
        {
        }

        public Nep5AccountState(byte[] data)
        {
            FromByteArray(data);
        }

        public void FromByteArray(byte[] data)
        {
            FromStruct((Struct)data.DeserializeStackItem(16));
        }

        protected virtual void FromStruct(Struct @struct)
        {
            Balance = @struct[0].GetBigInteger();
        }

        public byte[] ToByteArray()
        {
            return ToStruct().Serialize();
        }

        protected virtual Struct ToStruct()
        {
            return new Struct(new StackItem[] { Balance });
        }
    }
}
