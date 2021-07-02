using Neo.IO;
using Neo.SmartContract.Manifest;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Neo.SmartContract.Native
{
    /// <summary>
    /// The base class of all native contracts.
    /// </summary>
    public abstract class NativeContract
    {
        private static readonly List<NativeContract> contractsList = new();
        private static readonly Dictionary<UInt160, NativeContract> contractsDictionary = new();
        private readonly Dictionary<int, ContractMethodMetadata> methods = new();
        private static int id_counter = 0;

        #region Named Native Contracts

        /// <summary>
        /// Gets the instance of the <see cref="Native.ContractManagement"/> class.
        /// </summary>
        public static ContractManagement ContractManagement { get; } = new();

        /// <summary>
        /// Gets the instance of the <see cref="Native.StdLib"/> class.
        /// </summary>
        public static StdLib StdLib { get; } = new();

        /// <summary>
        /// Gets the instance of the <see cref="Native.CryptoLib"/> class.
        /// </summary>
        public static CryptoLib CryptoLib { get; } = new();

        /// <summary>
        /// Gets the instance of the <see cref="LedgerContract"/> class.
        /// </summary>
        public static LedgerContract Ledger { get; } = new();

        /// <summary>
        /// Gets the instance of the <see cref="NeoToken"/> class.
        /// </summary>
        public static NeoToken NEO { get; } = new();

        /// <summary>
        /// Gets the instance of the <see cref="GasToken"/> class.
        /// </summary>
        public static GasToken GAS { get; } = new();

        /// <summary>
        /// Gets the instance of the <see cref="PolicyContract"/> class.
        /// </summary>
        public static PolicyContract Policy { get; } = new();

        /// <summary>
        /// Gets the instance of the <see cref="Native.RoleManagement"/> class.
        /// </summary>
        public static RoleManagement RoleManagement { get; } = new();

        /// <summary>
        /// Gets the instance of the <see cref="OracleContract"/> class.
        /// </summary>
        public static OracleContract Oracle { get; } = new();

        /// <summary>
        /// Gets the instance of the <see cref="NotaryContract"/> class.
        /// </summary>
        public static NotaryContract Notary { get; } = new();

        #endregion

        /// <summary>
        /// Gets all native contracts.
        /// </summary>
        public static IReadOnlyCollection<NativeContract> Contracts { get; } = contractsList;

        /// <summary>
        /// The name of the native contract.
        /// </summary>
        public string Name => GetType().Name;

        /// <summary>
        /// The nef of the native contract.
        /// </summary>
        public NefFile Nef { get; }

        /// <summary>
        /// The hash of the native contract.
        /// </summary>
        public UInt160 Hash { get; }

        /// <summary>
        /// The id of the native contract.
        /// </summary>
        public int Id { get; } = --id_counter;

        /// <summary>
        /// The manifest of the native contract.
        /// </summary>
        public ContractManifest Manifest { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeContract"/> class.
        /// </summary>
        protected NativeContract()
        {
            List<ContractMethodMetadata> descriptors = new();
            foreach (MemberInfo member in GetType().GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                ContractMethodAttribute attribute = member.GetCustomAttribute<ContractMethodAttribute>();
                if (attribute is null) continue;
                descriptors.Add(new ContractMethodMetadata(member, attribute));
            }
            descriptors = descriptors.OrderBy(p => p.Name).ThenBy(p => p.Parameters.Length).ToList();
            byte[] script;
            using (ScriptBuilder sb = new())
            {
                foreach (ContractMethodMetadata method in descriptors)
                {
                    method.Descriptor.Offset = sb.Length;
                    sb.EmitPush(0); //version
                    methods.Add(sb.Length, method);
                    sb.EmitSysCall(ApplicationEngine.System_Contract_CallNative);
                    sb.Emit(OpCode.RET);
                }
                script = sb.ToArray();
            }
            this.Nef = new NefFile
            {
                Compiler = "neo-core-v3.0",
                Tokens = Array.Empty<MethodToken>(),
                Script = script
            };
            this.Nef.CheckSum = NefFile.ComputeChecksum(Nef);
            this.Hash = Helper.GetContractHash(UInt160.Zero, 0, Name);
            this.Manifest = new ContractManifest
            {
                Name = Name,
                Groups = Array.Empty<ContractGroup>(),
                SupportedStandards = Array.Empty<string>(),
                Abi = new ContractAbi()
                {
                    Events = Array.Empty<ContractEventDescriptor>(),
                    Methods = descriptors.Select(p => p.Descriptor).ToArray()
                },
                Permissions = new[] { ContractPermission.DefaultPermission },
                Trusts = WildcardContainer<ContractPermissionDescriptor>.Create(),
                Extra = null
            };
            contractsList.Add(this);
            contractsDictionary.Add(Hash, this);
        }

        /// <summary>
        /// Checks whether the committee has witnessed the current transaction.
        /// </summary>
        /// <param name="engine">The <see cref="ApplicationEngine"/> that is executing the contract.</param>
        /// <returns><see langword="true"/> if the committee has witnessed the current transaction; otherwise, <see langword="false"/>.</returns>
        protected static bool CheckCommittee(ApplicationEngine engine)
        {
            UInt160 committeeMultiSigAddr = NEO.GetCommitteeAddress(engine.Snapshot);
            return engine.CheckWitnessInternal(committeeMultiSigAddr);
        }

        private protected KeyBuilder CreateStorageKey(byte prefix)
        {
            return new KeyBuilder(Id, prefix);
        }

        /// <summary>
        /// Gets the native contract with the specified hash.
        /// </summary>
        /// <param name="hash">The hash of the native contract.</param>
        /// <returns>The native contract with the specified hash.</returns>
        public static NativeContract GetContract(UInt160 hash)
        {
            contractsDictionary.TryGetValue(hash, out var contract);
            return contract;
        }

        internal async void Invoke(ApplicationEngine engine, byte version)
        {
            try
            {
                if (version != 0)
                    throw new InvalidOperationException($"The native contract of version {version} is not active.");
                ExecutionContext context = engine.CurrentContext;
                ContractMethodMetadata method = methods[context.InstructionPointer];
                ExecutionContextState state = context.GetState<ExecutionContextState>();
                if (!state.CallFlags.HasFlag(method.RequiredCallFlags))
                    throw new InvalidOperationException($"Cannot call this method with the flag {state.CallFlags}.");
                engine.AddGas(method.CpuFee * Policy.GetExecFeeFactor(engine.Snapshot) + method.StorageFee * Policy.GetStoragePrice(engine.Snapshot));
                List<object> parameters = new();
                if (method.NeedApplicationEngine) parameters.Add(engine);
                if (method.NeedSnapshot) parameters.Add(engine.Snapshot);
                for (int i = 0; i < method.Parameters.Length; i++)
                    parameters.Add(engine.Convert(context.EvaluationStack.Pop(), method.Parameters[i]));
                object returnValue = method.Handler.Invoke(this, parameters.ToArray());
                if (returnValue is ContractTask task)
                {
                    await task;
                    returnValue = task.GetResult();
                }
                if (method.Handler.ReturnType != typeof(void) && method.Handler.ReturnType != typeof(ContractTask))
                {
                    context.EvaluationStack.Push(engine.Convert(returnValue));
                }
            }
            catch (Exception ex)
            {
                engine.Throw(ex);
            }
        }

        /// <summary>
        /// Determine whether the specified contract is a native contract.
        /// </summary>
        /// <param name="hash">The hash of the contract.</param>
        /// <returns><see langword="true"/> if the contract is native; otherwise, <see langword="false"/>.</returns>
        public static bool IsNative(UInt160 hash)
        {
            return contractsDictionary.ContainsKey(hash);
        }

        internal virtual ContractTask Initialize(ApplicationEngine engine)
        {
            return ContractTask.CompletedTask;
        }

        internal virtual ContractTask OnPersist(ApplicationEngine engine)
        {
            return ContractTask.CompletedTask;
        }

        internal virtual ContractTask PostPersist(ApplicationEngine engine)
        {
            return ContractTask.CompletedTask;
        }
    }
}
