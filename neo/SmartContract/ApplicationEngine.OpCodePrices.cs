﻿using Neo.VM;
using System.Collections.Generic;

namespace Neo.SmartContract
{
    partial class ApplicationEngine
    {
        public static readonly IReadOnlyDictionary<OpCode, long> OpCodePrices = new Dictionary<OpCode, long>
        {
            [OpCode.PUSH0] = 30,
            [OpCode.PUSHBYTES1] = 120,
            [OpCode.PUSHBYTES2] = 120,
            [OpCode.PUSHBYTES3] = 120,
            [OpCode.PUSHBYTES4] = 120,
            [OpCode.PUSHBYTES5] = 120,
            [OpCode.PUSHBYTES6] = 120,
            [OpCode.PUSHBYTES7] = 120,
            [OpCode.PUSHBYTES8] = 120,
            [OpCode.PUSHBYTES9] = 120,
            [OpCode.PUSHBYTES10] = 120,
            [OpCode.PUSHBYTES11] = 120,
            [OpCode.PUSHBYTES12] = 120,
            [OpCode.PUSHBYTES13] = 120,
            [OpCode.PUSHBYTES14] = 120,
            [OpCode.PUSHBYTES15] = 120,
            [OpCode.PUSHBYTES16] = 120,
            [OpCode.PUSHBYTES17] = 120,
            [OpCode.PUSHBYTES18] = 120,
            [OpCode.PUSHBYTES19] = 120,
            [OpCode.PUSHBYTES20] = 120,
            [OpCode.PUSHBYTES21] = 120,
            [OpCode.PUSHBYTES22] = 120,
            [OpCode.PUSHBYTES23] = 120,
            [OpCode.PUSHBYTES24] = 120,
            [OpCode.PUSHBYTES25] = 120,
            [OpCode.PUSHBYTES26] = 120,
            [OpCode.PUSHBYTES27] = 120,
            [OpCode.PUSHBYTES28] = 120,
            [OpCode.PUSHBYTES29] = 120,
            [OpCode.PUSHBYTES30] = 120,
            [OpCode.PUSHBYTES31] = 120,
            [OpCode.PUSHBYTES32] = 120,
            [OpCode.PUSHBYTES33] = 120,
            [OpCode.PUSHBYTES34] = 120,
            [OpCode.PUSHBYTES35] = 120,
            [OpCode.PUSHBYTES36] = 120,
            [OpCode.PUSHBYTES37] = 120,
            [OpCode.PUSHBYTES38] = 120,
            [OpCode.PUSHBYTES39] = 120,
            [OpCode.PUSHBYTES40] = 120,
            [OpCode.PUSHBYTES41] = 120,
            [OpCode.PUSHBYTES42] = 120,
            [OpCode.PUSHBYTES43] = 120,
            [OpCode.PUSHBYTES44] = 120,
            [OpCode.PUSHBYTES45] = 120,
            [OpCode.PUSHBYTES46] = 120,
            [OpCode.PUSHBYTES47] = 120,
            [OpCode.PUSHBYTES48] = 120,
            [OpCode.PUSHBYTES49] = 120,
            [OpCode.PUSHBYTES50] = 120,
            [OpCode.PUSHBYTES51] = 120,
            [OpCode.PUSHBYTES52] = 120,
            [OpCode.PUSHBYTES53] = 120,
            [OpCode.PUSHBYTES54] = 120,
            [OpCode.PUSHBYTES55] = 120,
            [OpCode.PUSHBYTES56] = 120,
            [OpCode.PUSHBYTES57] = 120,
            [OpCode.PUSHBYTES58] = 120,
            [OpCode.PUSHBYTES59] = 120,
            [OpCode.PUSHBYTES60] = 120,
            [OpCode.PUSHBYTES61] = 120,
            [OpCode.PUSHBYTES62] = 120,
            [OpCode.PUSHBYTES63] = 120,
            [OpCode.PUSHBYTES64] = 120,
            [OpCode.PUSHBYTES65] = 120,
            [OpCode.PUSHBYTES66] = 120,
            [OpCode.PUSHBYTES67] = 120,
            [OpCode.PUSHBYTES68] = 120,
            [OpCode.PUSHBYTES69] = 120,
            [OpCode.PUSHBYTES70] = 120,
            [OpCode.PUSHBYTES71] = 120,
            [OpCode.PUSHBYTES72] = 120,
            [OpCode.PUSHBYTES73] = 120,
            [OpCode.PUSHBYTES74] = 120,
            [OpCode.PUSHBYTES75] = 120,
            [OpCode.PUSHDATA1] = 180,
            [OpCode.PUSHDATA2] = 13000,
            [OpCode.PUSHDATA4] = 110000,
            [OpCode.PUSHM1] = 30,
            [OpCode.PUSH1] = 30,
            [OpCode.PUSH2] = 30,
            [OpCode.PUSH3] = 30,
            [OpCode.PUSH4] = 30,
            [OpCode.PUSH5] = 30,
            [OpCode.PUSH6] = 30,
            [OpCode.PUSH7] = 30,
            [OpCode.PUSH8] = 30,
            [OpCode.PUSH9] = 30,
            [OpCode.PUSH10] = 30,
            [OpCode.PUSH11] = 30,
            [OpCode.PUSH12] = 30,
            [OpCode.PUSH13] = 30,
            [OpCode.PUSH14] = 30,
            [OpCode.PUSH15] = 30,
            [OpCode.PUSH16] = 30,
            [OpCode.NOP] = 30,
            [OpCode.JMP] = 70,
            [OpCode.JMPIF] = 70,
            [OpCode.JMPIFNOT] = 70,
            [OpCode.CALL] = 22000,
            [OpCode.RET] = 40,
            [OpCode.SYSCALL] = 0,
            [OpCode.DUPFROMALTSTACKBOTTOM] = 60,
            [OpCode.DUPFROMALTSTACK] = 60,
            [OpCode.TOALTSTACK] = 60,
            [OpCode.FROMALTSTACK] = 60,
            [OpCode.XDROP] = 400,
            [OpCode.XSWAP] = 60,
            [OpCode.XTUCK] = 400,
            [OpCode.DEPTH] = 60,
            [OpCode.DROP] = 60,
            [OpCode.DUP] = 60,
            [OpCode.NIP] = 60,
            [OpCode.OVER] = 60,
            [OpCode.PICK] = 60,
            [OpCode.ROLL] = 400,
            [OpCode.ROT] = 60,
            [OpCode.SWAP] = 60,
            [OpCode.TUCK] = 60,
            [OpCode.CAT] = 80000,
            [OpCode.SUBSTR] = 80000,
            [OpCode.LEFT] = 80000,
            [OpCode.RIGHT] = 80000,
            [OpCode.SIZE] = 60,
            [OpCode.INVERT] = 100,
            [OpCode.AND] = 200,
            [OpCode.OR] = 200,
            [OpCode.XOR] = 200,
            [OpCode.EQUAL] = 200,
            [OpCode.INC] = 100,
            [OpCode.DEC] = 100,
            [OpCode.SIGN] = 100,
            [OpCode.NEGATE] = 100,
            [OpCode.ABS] = 100,
            [OpCode.NOT] = 100,
            [OpCode.NZ] = 100,
            [OpCode.ADD] = 200,
            [OpCode.SUB] = 200,
            [OpCode.MUL] = 300,
            [OpCode.DIV] = 300,
            [OpCode.MOD] = 300,
            [OpCode.SHL] = 300,
            [OpCode.SHR] = 300,
            [OpCode.BOOLAND] = 200,
            [OpCode.BOOLOR] = 200,
            [OpCode.NUMEQUAL] = 200,
            [OpCode.NUMNOTEQUAL] = 200,
            [OpCode.LT] = 200,
            [OpCode.GT] = 200,
            [OpCode.LTE] = 200,
            [OpCode.GTE] = 200,
            [OpCode.MIN] = 200,
            [OpCode.MAX] = 200,
            [OpCode.WITHIN] = 200,
            [OpCode.ARRAYSIZE] = 150,
            [OpCode.PACK] = 7000,
            [OpCode.UNPACK] = 7000,
            [OpCode.PICKITEM] = 270000,
            [OpCode.SETITEM] = 270000,
            [OpCode.NEWARRAY] = 15000,
            [OpCode.NEWSTRUCT] = 15000,
            [OpCode.NEWMAP] = 200,
            [OpCode.APPEND] = 15000,
            [OpCode.REVERSE] = 500,
            [OpCode.REMOVE] = 500,
            [OpCode.HASKEY] = 270000,
            [OpCode.KEYS] = 500,
            [OpCode.VALUES] = 7000,
            [OpCode.THROW] = 30,
            [OpCode.THROWIFNOT] = 30
        };
    }
}
