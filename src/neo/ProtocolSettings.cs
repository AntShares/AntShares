using Microsoft.Extensions.Configuration;
using Neo.Cryptography.ECC;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract.Native;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo
{
    /// <summary>
    /// Represents the protocol settings of the NEO system.
    /// </summary>
    public record ProtocolSettings
    {
        /// <summary>
        /// The magic number of the NEO network.
        /// </summary>
        public uint Network { get; init; }

        /// <summary>
        /// The address version of the NEO system.
        /// </summary>
        public byte AddressVersion { get; init; }

        /// <summary>
        /// The public keys of the standby committee members.
        /// </summary>
        public IReadOnlyList<ECPoint> StandbyCommittee { get; init; }

        /// <summary>
        /// The number of members of the committee in NEO system.
        /// </summary>
        public int CommitteeMembersCount => StandbyCommittee.Count;

        /// <summary>
        /// The number of the validators in NEO system.
        /// </summary>
        public int ValidatorsCount { get; init; }

        /// <summary>
        /// The default seed nodes list.
        /// </summary>
        public string[] SeedList { get; init; }

        /// <summary>
        /// Indicates the time in milliseconds between two blocks.
        /// </summary>
        public uint MillisecondsPerBlock { get; init; }

        /// <summary>
        /// Indicates the time between two blocks.
        /// </summary>
        public TimeSpan TimePerBlock => TimeSpan.FromMilliseconds(MillisecondsPerBlock);

        /// <summary>
        /// The maximum increment of the <see cref="Transaction.ValidUntilBlock"/> field.
        /// </summary>
        public uint MaxValidUntilBlockIncrement => 86400000 / MillisecondsPerBlock;

        /// <summary>
        /// Indicates the maximum number of transactions that can be contained in a block.
        /// </summary>
        public uint MaxTransactionsPerBlock { get; init; }

        /// <summary>
        /// Indicates the maximum number of transactions that can be contained in the memory pool.
        /// </summary>
        public int MemoryPoolMaxTransactions { get; init; }

        /// <summary>
        /// Indicates the maximum number of blocks that can be traced in the smart contract.
        /// </summary>
        public uint MaxTraceableBlocks { get; init; }

        /// <summary>
        /// Contains the update history of all native contracts.
        /// </summary>
        public IReadOnlyDictionary<string, uint[]> NativeUpdateHistory { get; init; }

        private IReadOnlyList<ECPoint> _standbyValidators;
        /// <summary>
        /// The public keys of the standby validators.
        /// </summary>
        public IReadOnlyList<ECPoint> StandbyValidators => _standbyValidators ??= StandbyCommittee.Take(ValidatorsCount).ToArray();

        /// <summary>
        /// The default protocol settings for NEO MainNet.
        /// </summary>
        public static ProtocolSettings Default { get; } = new ProtocolSettings
        {
            Network = 0x4F454Eu,
            AddressVersion = 0x35,
            StandbyCommittee = new[]
            {
                //Validators
                ECPoint.Parse("03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c", ECCurve.Secp256r1),
                ECPoint.Parse("02df48f60e8f3e01c48ff40b9b7f1310d7a8b2a193188befe1c2e3df740e895093", ECCurve.Secp256r1),
                ECPoint.Parse("03b8d9d5771d8f513aa0869b9cc8d50986403b78c6da36890638c3d46a5adce04a", ECCurve.Secp256r1),
                ECPoint.Parse("02ca0e27697b9c248f6f16e085fd0061e26f44da85b58ee835c110caa5ec3ba554", ECCurve.Secp256r1),
                ECPoint.Parse("024c7b7fb6c310fccf1ba33b082519d82964ea93868d676662d4a59ad548df0e7d", ECCurve.Secp256r1),
                ECPoint.Parse("02aaec38470f6aad0042c6e877cfd8087d2676b0f516fddd362801b9bd3936399e", ECCurve.Secp256r1),
                ECPoint.Parse("02486fd15702c4490a26703112a5cc1d0923fd697a33406bd5a1c00e0013b09a70", ECCurve.Secp256r1),
                //Other Members
                ECPoint.Parse("023a36c72844610b4d34d1968662424011bf783ca9d984efa19a20babf5582f3fe", ECCurve.Secp256r1),
                ECPoint.Parse("03708b860c1de5d87f5b151a12c2a99feebd2e8b315ee8e7cf8aa19692a9e18379", ECCurve.Secp256r1),
                ECPoint.Parse("03c6aa6e12638b36e88adc1ccdceac4db9929575c3e03576c617c49cce7114a050", ECCurve.Secp256r1),
                ECPoint.Parse("03204223f8c86b8cd5c89ef12e4f0dbb314172e9241e30c9ef2293790793537cf0", ECCurve.Secp256r1),
                ECPoint.Parse("02a62c915cf19c7f19a50ec217e79fac2439bbaad658493de0c7d8ffa92ab0aa62", ECCurve.Secp256r1),
                ECPoint.Parse("03409f31f0d66bdc2f70a9730b66fe186658f84a8018204db01c106edc36553cd0", ECCurve.Secp256r1),
                ECPoint.Parse("0288342b141c30dc8ffcde0204929bb46aed5756b41ef4a56778d15ada8f0c6654", ECCurve.Secp256r1),
                ECPoint.Parse("020f2887f41474cfeb11fd262e982051c1541418137c02a0f4961af911045de639", ECCurve.Secp256r1),
                ECPoint.Parse("0222038884bbd1d8ff109ed3bdef3542e768eef76c1247aea8bc8171f532928c30", ECCurve.Secp256r1),
                ECPoint.Parse("03d281b42002647f0113f36c7b8efb30db66078dfaaa9ab3ff76d043a98d512fde", ECCurve.Secp256r1),
                ECPoint.Parse("02504acbc1f4b3bdad1d86d6e1a08603771db135a73e61c9d565ae06a1938cd2ad", ECCurve.Secp256r1),
                ECPoint.Parse("0226933336f1b75baa42d42b71d9091508b638046d19abd67f4e119bf64a7cfb4d", ECCurve.Secp256r1),
                ECPoint.Parse("03cdcea66032b82f5c30450e381e5295cae85c5e6943af716cc6b646352a6067dc", ECCurve.Secp256r1),
                ECPoint.Parse("02cd5a5547119e24feaa7c2a0f37b8c9366216bab7054de0065c9be42084003c8a", ECCurve.Secp256r1)
            },
            ValidatorsCount = 7,
            SeedList = new[]
            {
                "seed1.neo.org:10333",
                "seed2.neo.org:10333",
                "seed3.neo.org:10333",
                "seed4.neo.org:10333",
                "seed5.neo.org:10333"
            },
            MillisecondsPerBlock = 15000,
            MaxTransactionsPerBlock = 512,
            MemoryPoolMaxTransactions = 50_000,
            MaxTraceableBlocks = 2_102_400,
            NativeUpdateHistory = new Dictionary<string, uint[]>
            {
                [nameof(ContractManagement)] = new[] { 0u },
                [nameof(StdLib)] = new[] { 0u },
                [nameof(CryptoLib)] = new[] { 0u },
                [nameof(LedgerContract)] = new[] { 0u },
                [nameof(NeoToken)] = new[] { 0u },
                [nameof(GasToken)] = new[] { 0u },
                [nameof(PolicyContract)] = new[] { 0u },
                [nameof(RoleManagement)] = new[] { 0u },
                [nameof(OracleContract)] = new[] { 0u },
                [nameof(NotaryContract)] = new[] { 0u }
            }
        };

        /// <summary>
        /// Loads the <see cref="ProtocolSettings"/> at the specified path.
        /// </summary>
        /// <param name="path">The path of the settings file.</param>
        /// <param name="optional">Indicates whether the file is optional.</param>
        /// <returns>The loaded <see cref="ProtocolSettings"/>.</returns>
        public static ProtocolSettings Load(string path, bool optional = true)
        {
            IConfigurationRoot config = new ConfigurationBuilder().AddJsonFile(path, optional).Build();
            IConfigurationSection section = config.GetSection("ProtocolConfiguration");
            return Load(section);
        }

        /// <summary>
        /// Loads the <see cref="ProtocolSettings"/> with the specified <see cref="IConfigurationSection"/>.
        /// </summary>
        /// <param name="section">The <see cref="IConfigurationSection"/> to be loaded.</param>
        /// <returns>The loaded <see cref="ProtocolSettings"/>.</returns>
        public static ProtocolSettings Load(IConfigurationSection section)
        {
            return new ProtocolSettings
            {
                Network = section.GetValue("Network", Default.Network),
                AddressVersion = section.GetValue("AddressVersion", Default.AddressVersion),
                StandbyCommittee = section.GetSection("StandbyCommittee").Exists()
                    ? section.GetSection("StandbyCommittee").GetChildren().Select(p => ECPoint.Parse(p.Get<string>(), ECCurve.Secp256r1)).ToArray()
                    : Default.StandbyCommittee,
                ValidatorsCount = section.GetValue("ValidatorsCount", Default.ValidatorsCount),
                SeedList = section.GetSection("SeedList").Exists()
                    ? section.GetSection("SeedList").GetChildren().Select(p => p.Get<string>()).ToArray()
                    : Default.SeedList,
                MillisecondsPerBlock = section.GetValue("MillisecondsPerBlock", Default.MillisecondsPerBlock),
                MaxTransactionsPerBlock = section.GetValue("MaxTransactionsPerBlock", Default.MaxTransactionsPerBlock),
                MemoryPoolMaxTransactions = section.GetValue("MemoryPoolMaxTransactions", Default.MemoryPoolMaxTransactions),
                MaxTraceableBlocks = section.GetValue("MaxTraceableBlocks", Default.MaxTraceableBlocks),
                NativeUpdateHistory = section.GetSection("NativeUpdateHistory").Exists()
                    ? section.GetSection("NativeUpdateHistory").GetChildren().ToDictionary(p => p.Key, p => p.GetChildren().Select(q => uint.Parse(q.Value)).ToArray())
                    : Default.NativeUpdateHistory
            };
        }
    }
}
