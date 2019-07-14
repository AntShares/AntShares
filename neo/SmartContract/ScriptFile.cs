﻿using Neo.IO;
using System;
using System.IO;

namespace Neo.SmartContract
{
    public class ScriptFile : ISerializable
    {
        public enum ScriptMagic : int
        {
            /// <summary>
            /// NEO Executable Format 3
            /// </summary>
            NEF3 = 0x4E454633
        }

        /// <summary>
        /// Magic
        /// </summary>
        public ScriptMagic Magic { get; set; }

        /// <summary>
        /// Compiler
        /// </summary>
        public string Compiler { get; set; }

        /// <summary>
        /// Version
        /// </summary>
        public Version Version { get; set; }

        /// <summary>
        /// Script
        /// </summary>
        public byte[] Script { get; set; }

        /// <summary>
        /// Script Hash
        /// </summary>
        public UInt160 ScriptHash { get; set; }

        public int Size =>
            sizeof(ScriptMagic) +       // Engine
            Compiler.GetVarSize() +     // Compiler
            (sizeof(int) * 4) +         // Version
            Script.GetVarSize() +       // Script
            ScriptHash.Size;            // ScriptHash (CRC)

        /// <summary>
        /// Read Script Header from a binary
        /// </summary>
        /// <param name="script">Script</param>
        /// <returns>Return script header</returns>
        public static ScriptFile FromByteArray(byte[] script)
        {
            using (var stream = new MemoryStream(script))
            using (var reader = new BinaryReader(stream))
            {
                return reader.ReadSerializable<ScriptFile>();
            }
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((int)Magic);
            writer.WriteFixedString(Compiler, 64);

            // Version
            writer.Write(Version.Major);
            writer.Write(Version.Minor);
            writer.Write(Version.Build);
            writer.Write(Version.Revision);

            writer.WriteVarBytes(Script);
            writer.Write(ScriptHash);
        }

        public void Deserialize(BinaryReader reader)
        {
            Magic = (ScriptMagic)reader.ReadInt32();

            if (Magic != ScriptMagic.NEF3)
            {
                throw new FormatException("Wrong magic");
            }

            Compiler = reader.ReadFixedString(64);
            Version = new Version(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            Script = reader.ReadVarBytes(1024 * 1024);
            ScriptHash = reader.ReadSerializable<UInt160>();

            if (Script.ToScriptHash() != ScriptHash)
            {
                throw new FormatException("CRC verification fail");
            }
        }
    }
}