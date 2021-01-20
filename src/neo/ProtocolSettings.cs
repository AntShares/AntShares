using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Neo
{
    public class ProtocolSettings
    {
        public uint Magic { get; }
        public byte AddressVersion { get; }
        public string[] StandbyCommittee { get; }
        public int CommitteeMembersCount { get; }
        public int ValidatorsCount { get; }
        public string[] SeedList { get; }
        public uint MillisecondsPerBlock { get; }
        public int MemoryPoolMaxTransactions { get; }
        public uint MaxTraceableBlocks { get; }
        public IReadOnlyDictionary<string, uint> NativeActivations { get; }
        public IReadOnlyList<uint> EmergencyMode { get; }

        static ProtocolSettings _default;

        static bool UpdateDefault(IConfiguration configuration)
        {
            var settings = new ProtocolSettings(configuration.GetSection("ProtocolConfiguration"));
            return null == Interlocked.CompareExchange(ref _default, settings, null);
        }

        public static bool Initialize(IConfiguration configuration)
        {
            return UpdateDefault(configuration);
        }

        public static ProtocolSettings Default
        {
            get
            {
                if (_default == null)
                {
                    var configuration = Utility.LoadConfig("protocol");
                    UpdateDefault(configuration);
                }

                return _default;
            }
        }

        private ProtocolSettings(IConfigurationSection section)
        {
            this.Magic = section.GetValue("Magic", 0x4F454Eu);
            this.AddressVersion = section.GetValue("AddressVersion", (byte)0x35);
            IConfigurationSection section_sc = section.GetSection("StandbyCommittee");
            if (section_sc.Exists())
                this.StandbyCommittee = section_sc.GetChildren().Select(p => p.Get<string>()).ToArray();
            else
                this.StandbyCommittee = new[]
                {
                    //Validators
                    "03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c",
                    "02df48f60e8f3e01c48ff40b9b7f1310d7a8b2a193188befe1c2e3df740e895093",
                    "03b8d9d5771d8f513aa0869b9cc8d50986403b78c6da36890638c3d46a5adce04a",
                    "02ca0e27697b9c248f6f16e085fd0061e26f44da85b58ee835c110caa5ec3ba554",
                    "024c7b7fb6c310fccf1ba33b082519d82964ea93868d676662d4a59ad548df0e7d",
                    "02aaec38470f6aad0042c6e877cfd8087d2676b0f516fddd362801b9bd3936399e",
                    "02486fd15702c4490a26703112a5cc1d0923fd697a33406bd5a1c00e0013b09a70",

                    //Other Members
                    "023a36c72844610b4d34d1968662424011bf783ca9d984efa19a20babf5582f3fe",
                    "03708b860c1de5d87f5b151a12c2a99feebd2e8b315ee8e7cf8aa19692a9e18379",
                    "03c6aa6e12638b36e88adc1ccdceac4db9929575c3e03576c617c49cce7114a050",
                    "03204223f8c86b8cd5c89ef12e4f0dbb314172e9241e30c9ef2293790793537cf0",
                    "02a62c915cf19c7f19a50ec217e79fac2439bbaad658493de0c7d8ffa92ab0aa62",
                    "03409f31f0d66bdc2f70a9730b66fe186658f84a8018204db01c106edc36553cd0",
                    "0288342b141c30dc8ffcde0204929bb46aed5756b41ef4a56778d15ada8f0c6654",
                    "020f2887f41474cfeb11fd262e982051c1541418137c02a0f4961af911045de639",
                    "0222038884bbd1d8ff109ed3bdef3542e768eef76c1247aea8bc8171f532928c30",
                    "03d281b42002647f0113f36c7b8efb30db66078dfaaa9ab3ff76d043a98d512fde",
                    "02504acbc1f4b3bdad1d86d6e1a08603771db135a73e61c9d565ae06a1938cd2ad",
                    "0226933336f1b75baa42d42b71d9091508b638046d19abd67f4e119bf64a7cfb4d",
                    "03cdcea66032b82f5c30450e381e5295cae85c5e6943af716cc6b646352a6067dc",
                    "02cd5a5547119e24feaa7c2a0f37b8c9366216bab7054de0065c9be42084003c8a"
                };
            this.CommitteeMembersCount = StandbyCommittee.Length;
            this.ValidatorsCount = section.GetValue("ValidatorsCount", (byte)7);
            IConfigurationSection section_sl = section.GetSection("SeedList");
            if (section_sl.Exists())
                this.SeedList = section_sl.GetChildren().Select(p => p.Get<string>()).ToArray();
            else
                this.SeedList = new[]
                {
                    "seed1.neo.org:10333",
                    "seed2.neo.org:10333",
                    "seed3.neo.org:10333",
                    "seed4.neo.org:10333",
                    "seed5.neo.org:10333"
                };
            this.MillisecondsPerBlock = section.GetValue("MillisecondsPerBlock", 15000u);
            this.MemoryPoolMaxTransactions = Math.Max(1, section.GetValue("MemoryPoolMaxTransactions", 50_000));
            this.MaxTraceableBlocks = section.GetValue("MaxTraceableBlocks", 2_102_400u);// 365 days
            IConfigurationSection section_na = section.GetSection("NativeActivations");
            if (section_na.Exists())
                this.NativeActivations = section_na.GetChildren().ToDictionary((a) => a.Key, b => uint.Parse(b.Value));
            else
                this.NativeActivations = new Dictionary<string, uint>();

            IConfigurationSection section_em = section.GetSection("EmergencyMode");
            if (section_em.Exists())
                this.EmergencyMode = section_em.GetChildren().Select(a => uint.Parse(a.Value)).ToList().AsReadOnly();
            else
                this.EmergencyMode = new List<uint>().AsReadOnly();
        }
    }
}
