using Neo.IO;
using Neo.IO.Json;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using static System.Threading.Interlocked;
using Array = System.Array;
using VMArray = Neo.VM.Types.Array;

namespace Neo.SmartContract
{
    public partial class ApplicationEngine : ExecutionEngine
    {
        /// <summary>
        /// This constant can be used for testing scripts.
        /// </summary>
        public const long TestModeGas = 20_00000000;

        public static event EventHandler<NotifyEventArgs> Notify;
        public static event EventHandler<LogEventArgs> Log;

        private static IApplicationEngineProvider applicationEngineProvider;
        private static Dictionary<uint, InteropDescriptor> services;
        private readonly long gas_amount;
        private List<NotifyEventArgs> notifications;
        private List<IDisposable> disposables;
        private readonly Dictionary<UInt160, int> invocationCounter = new Dictionary<UInt160, int>();
        private readonly Dictionary<ExecutionContext, ContractTaskAwaiter> contractTasks = new Dictionary<ExecutionContext, ContractTaskAwaiter>();
        private readonly uint exec_fee_factor;
        internal readonly uint StoragePrice;

        public static IReadOnlyDictionary<uint, InteropDescriptor> Services => services;
        private List<IDisposable> Disposables => disposables ??= new List<IDisposable>();
        public List<UInt160> InvokedContracts => invocationCounter.Keys.ToList();
        public TriggerType Trigger { get; }
        public IVerifiable ScriptContainer { get; }
        public DataCache Snapshot { get; }
        public Block PersistingBlock { get; }
        public ProtocolSettings ProtocolSettings { get; }
        public long GasConsumed { get; private set; } = 0;
        public long GasLeft => gas_amount - GasConsumed;
        public Exception FaultException { get; private set; }
        public UInt160 CurrentScriptHash => CurrentContext?.GetScriptHash();
        public UInt160 CallingScriptHash => CurrentContext?.GetState<ExecutionContextState>().CallingScriptHash;
        public UInt160 EntryScriptHash => EntryContext?.GetScriptHash();
        public IReadOnlyList<NotifyEventArgs> Notifications => notifications ?? (IReadOnlyList<NotifyEventArgs>)Array.Empty<NotifyEventArgs>();

        protected ApplicationEngine(TriggerType trigger, IVerifiable container, DataCache snapshot, Block persistingBlock, ProtocolSettings settings, long gas)
        {
            this.Trigger = trigger;
            this.ScriptContainer = container;
            this.Snapshot = snapshot;
            this.PersistingBlock = persistingBlock;
            this.ProtocolSettings = settings;
            this.gas_amount = gas;
            this.exec_fee_factor = snapshot is null || persistingBlock?.Index == 0 ? PolicyContract.DefaultExecFeeFactor : NativeContract.Policy.GetExecFeeFactor(Snapshot);
            this.StoragePrice = snapshot is null || persistingBlock?.Index == 0 ? PolicyContract.DefaultStoragePrice : NativeContract.Policy.GetStoragePrice(Snapshot);
        }

        protected internal void AddGas(long gas)
        {
            GasConsumed = checked(GasConsumed + gas);
            if (GasConsumed > gas_amount)
                throw new InvalidOperationException("Insufficient GAS.");
        }

        protected override void OnFault(Exception ex)
        {
            FaultException = ex;
            base.OnFault(ex);
        }

        internal void Throw(Exception ex)
        {
            OnFault(ex);
        }

        private ExecutionContext CallContractInternal(UInt160 contractHash, string method, CallFlags flags, bool hasReturnValue, StackItem[] args)
        {
            ContractState contract = NativeContract.ContractManagement.GetContract(Snapshot, contractHash);
            if (contract is null) throw new InvalidOperationException($"Called Contract Does Not Exist: {contractHash}");
            ContractMethodDescriptor md = contract.Manifest.Abi.GetMethod(method, args.Length);
            if (md is null) throw new InvalidOperationException($"Method \"{method}\" with {args.Length} parameter(s) doesn't exist in the contract {contractHash}.");
            return CallContractInternal(contract, md, flags, hasReturnValue, args);
        }

        private ExecutionContext CallContractInternal(ContractState contract, ContractMethodDescriptor method, CallFlags flags, bool hasReturnValue, IReadOnlyList<StackItem> args)
        {
            if (method.Safe)
            {
                flags &= ~(CallFlags.WriteStates | CallFlags.AllowNotify);
            }
            else
            {
                ContractState currentContract = NativeContract.ContractManagement.GetContract(Snapshot, CurrentScriptHash);
                if (currentContract?.CanCall(contract, method.Name) == false)
                    throw new InvalidOperationException($"Cannot Call Method {method} Of Contract {contract.Hash} From Contract {CurrentScriptHash}");
            }

            if (invocationCounter.TryGetValue(contract.Hash, out var counter))
            {
                invocationCounter[contract.Hash] = counter + 1;
            }
            else
            {
                invocationCounter[contract.Hash] = 1;
            }

            ExecutionContextState state = CurrentContext.GetState<ExecutionContextState>();
            UInt160 callingScriptHash = state.ScriptHash;
            CallFlags callingFlags = state.CallFlags;

            if (args.Count != method.Parameters.Length) throw new InvalidOperationException($"Method {method} Expects {method.Parameters.Length} Arguments But Receives {args.Count} Arguments");
            if (hasReturnValue ^ (method.ReturnType != ContractParameterType.Void)) throw new InvalidOperationException("The return value type does not match.");
            ExecutionContext context_new = LoadContract(contract, method, flags & callingFlags);
            state = context_new.GetState<ExecutionContextState>();
            state.CallingScriptHash = callingScriptHash;

            for (int i = args.Count - 1; i >= 0; i--)
                context_new.EvaluationStack.Push(args[i]);

            return context_new;
        }

        internal ContractTask CallFromNativeContract(UInt160 callingScriptHash, UInt160 hash, string method, params StackItem[] args)
        {
            ExecutionContext context_new = CallContractInternal(hash, method, CallFlags.All, false, args);
            ExecutionContextState state = context_new.GetState<ExecutionContextState>();
            state.CallingScriptHash = callingScriptHash;
            ContractTask task = new ContractTask();
            contractTasks.Add(context_new, task.GetAwaiter());
            return task;
        }

        internal ContractTask<T> CallFromNativeContract<T>(UInt160 callingScriptHash, UInt160 hash, string method, params StackItem[] args)
        {
            ExecutionContext context_new = CallContractInternal(hash, method, CallFlags.All, true, args);
            ExecutionContextState state = context_new.GetState<ExecutionContextState>();
            state.CallingScriptHash = callingScriptHash;
            ContractTask<T> task = new ContractTask<T>();
            contractTasks.Add(context_new, task.GetAwaiter());
            return task;
        }

        protected override void ContextUnloaded(ExecutionContext context)
        {
            base.ContextUnloaded(context);
            if (!contractTasks.Remove(context, out var awaiter)) return;
            if (UncaughtException is not null)
                throw new VMUnhandledException(UncaughtException);
            awaiter.SetResult(this);
        }

        public static ApplicationEngine Create(TriggerType trigger, IVerifiable container, DataCache snapshot, Block persistingBlock = null, ProtocolSettings settings = null, long gas = TestModeGas)
        {
            return applicationEngineProvider?.Create(trigger, container, snapshot, persistingBlock, settings, gas)
                  ?? new ApplicationEngine(trigger, container, snapshot, persistingBlock, settings, gas);
        }

        protected override void LoadContext(ExecutionContext context)
        {
            // Set default execution context state

            var state = context.GetState<ExecutionContextState>();
            state.ScriptHash ??= ((byte[])context.Script).ToScriptHash();
            invocationCounter.TryAdd(state.ScriptHash, 1);

            base.LoadContext(context);
        }

        public ExecutionContext LoadContract(ContractState contract, ContractMethodDescriptor method, CallFlags callFlags)
        {
            ExecutionContext context = LoadScript(contract.Script,
                rvcount: method.ReturnType == ContractParameterType.Void ? 0 : 1,
                initialPosition: method.Offset,
                configureState: p =>
                {
                    p.CallFlags = callFlags;
                    p.ScriptHash = contract.Hash;
                    p.Contract = contract;
                });

            // Call initialization
            var init = contract.Manifest.Abi.GetMethod("_initialize", 0);
            if (init != null)
            {
                LoadContext(context.Clone(init.Offset));
            }

            return context;
        }

        public ExecutionContext LoadScript(Script script, int rvcount = -1, int initialPosition = 0, Action<ExecutionContextState> configureState = null)
        {
            // Create and configure context
            ExecutionContext context = CreateContext(script, rvcount, initialPosition);
            configureState?.Invoke(context.GetState<ExecutionContextState>());
            // Load context
            LoadContext(context);
            return context;
        }

        protected override ExecutionContext LoadToken(ushort tokenId)
        {
            ValidateCallFlags(CallFlags.ReadStates | CallFlags.AllowCall);
            ContractState contract = CurrentContext.GetState<ExecutionContextState>().Contract;
            if (contract is null || tokenId >= contract.Nef.Tokens.Length)
                throw new InvalidOperationException();
            MethodToken token = contract.Nef.Tokens[tokenId];
            if (token.ParametersCount > CurrentContext.EvaluationStack.Count)
                throw new InvalidOperationException();
            StackItem[] args = new StackItem[token.ParametersCount];
            for (int i = 0; i < token.ParametersCount; i++)
                args[i] = Pop();
            return CallContractInternal(token.Hash, token.Method, token.CallFlags, token.HasReturnValue, args);
        }

        protected internal StackItem Convert(object value)
        {
            if (value is IDisposable disposable) Disposables.Add(disposable);
            return value switch
            {
                null => StackItem.Null,
                bool b => b,
                sbyte i => i,
                byte i => (BigInteger)i,
                short i => i,
                ushort i => (BigInteger)i,
                int i => i,
                uint i => i,
                long i => i,
                ulong i => i,
                Enum e => Convert(System.Convert.ChangeType(e, e.GetTypeCode())),
                byte[] data => data,
                string s => s,
                BigInteger i => i,
                JObject o => o.ToByteArray(false),
                IInteroperable interoperable => interoperable.ToStackItem(ReferenceCounter),
                ISerializable i => i.ToArray(),
                StackItem item => item,
                (object a, object b) => new Struct(ReferenceCounter) { Convert(a), Convert(b) },
                Array array => new VMArray(ReferenceCounter, array.OfType<object>().Select(p => Convert(p))),
                _ => StackItem.FromInterface(value)
            };
        }

        protected internal object Convert(StackItem item, InteropParameterDescriptor descriptor)
        {
            if (descriptor.IsArray)
            {
                Array av;
                if (item is VMArray array)
                {
                    av = Array.CreateInstance(descriptor.Type.GetElementType(), array.Count);
                    for (int i = 0; i < av.Length; i++)
                        av.SetValue(descriptor.Converter(array[i]), i);
                }
                else
                {
                    int count = (int)item.GetInteger();
                    if (count > Limits.MaxStackSize) throw new InvalidOperationException();
                    av = Array.CreateInstance(descriptor.Type.GetElementType(), count);
                    for (int i = 0; i < av.Length; i++)
                        av.SetValue(descriptor.Converter(Pop()), i);
                }
                return av;
            }
            else
            {
                object value = descriptor.Converter(item);
                if (descriptor.IsEnum)
                    value = Enum.ToObject(descriptor.Type, value);
                else if (descriptor.IsInterface)
                    value = ((InteropInterface)value).GetInterface<object>();
                return value;
            }
        }

        public override void Dispose()
        {
            if (disposables != null)
            {
                foreach (IDisposable disposable in disposables)
                    disposable.Dispose();
                disposables = null;
            }
            base.Dispose();
        }

        protected void ValidateCallFlags(CallFlags requiredCallFlags)
        {
            ExecutionContextState state = CurrentContext.GetState<ExecutionContextState>();
            if (!state.CallFlags.HasFlag(requiredCallFlags))
                throw new InvalidOperationException($"Cannot call this SYSCALL with the flag {state.CallFlags}.");
        }

        protected override void OnSysCall(uint method)
        {
            OnSysCall(services[method]);
        }

        protected virtual void OnSysCall(InteropDescriptor descriptor)
        {
            ValidateCallFlags(descriptor.RequiredCallFlags);
            AddGas(descriptor.FixedPrice * exec_fee_factor);

            object[] parameters = new object[descriptor.Parameters.Count];
            for (int i = 0; i < parameters.Length; i++)
                parameters[i] = Convert(Pop(), descriptor.Parameters[i]);

            object returnValue = descriptor.Handler.Invoke(this, parameters);
            if (descriptor.Handler.ReturnType != typeof(void))
                Push(Convert(returnValue));
        }

        protected override void PreExecuteInstruction()
        {
            if (CurrentContext.InstructionPointer < CurrentContext.Script.Length)
                AddGas(exec_fee_factor * OpCodePrices[CurrentContext.CurrentInstruction.OpCode]);
        }

        private static Block CreateDummyBlock(DataCache snapshot, ProtocolSettings settings)
        {
            UInt256 hash = NativeContract.Ledger.CurrentHash(snapshot);
            Block currentBlock = NativeContract.Ledger.GetBlock(snapshot, hash);
            return new Block
            {
                Header = new Header
                {
                    Version = 0,
                    PrevHash = hash,
                    MerkleRoot = new UInt256(),
                    Timestamp = currentBlock.Timestamp + settings.MillisecondsPerBlock,
                    Index = currentBlock.Index + 1,
                    NextConsensus = currentBlock.NextConsensus,
                    Witness = new Witness
                    {
                        InvocationScript = Array.Empty<byte>(),
                        VerificationScript = Array.Empty<byte>()
                    },
                },
                Transactions = Array.Empty<Transaction>()
            };
        }

        private static InteropDescriptor Register(string name, string handler, long fixedPrice, CallFlags requiredCallFlags)
        {
            MethodInfo method = typeof(ApplicationEngine).GetMethod(handler, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(ApplicationEngine).GetProperty(handler, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetMethod;
            InteropDescriptor descriptor = new()
            {
                Name = name,
                Handler = method,
                FixedPrice = fixedPrice,
                RequiredCallFlags = requiredCallFlags
            };
            services ??= new Dictionary<uint, InteropDescriptor>();
            services.Add(descriptor.Hash, descriptor);
            return descriptor;
        }

        internal static void ResetApplicationEngineProvider()
        {
            Exchange(ref applicationEngineProvider, null);
        }

        public static ApplicationEngine Run(byte[] script, DataCache snapshot, IVerifiable container = null, Block persistingBlock = null, ProtocolSettings settings = null, int offset = 0, long gas = TestModeGas)
        {
            persistingBlock ??= CreateDummyBlock(snapshot, settings ?? ProtocolSettings.Default);
            ApplicationEngine engine = Create(TriggerType.Application, container, snapshot, persistingBlock, settings, gas);
            engine.LoadScript(script, initialPosition: offset);
            engine.Execute();
            return engine;
        }

        internal static bool SetApplicationEngineProvider(IApplicationEngineProvider provider)
        {
            return CompareExchange(ref applicationEngineProvider, provider, null) is null;
        }
    }
}
