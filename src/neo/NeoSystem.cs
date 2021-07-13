using Akka.Actor;
using Akka.Event;
using Neo.IO.Caching;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Neo
{
    /// <summary>
    /// Represents the basic unit that contains all the components required for running of a NEO node.
    /// </summary>
    public class NeoSystem : IDisposable
    {
        /// <summary>
        /// Triggered when a service is added to the <see cref="NeoSystem"/>.
        /// </summary>
        public event EventHandler<object> ServiceAdded;

        /// <summary>
        /// The protocol settings of the <see cref="NeoSystem"/>.
        /// </summary>
        public ProtocolSettings Settings { get; }

        /// <summary>
        /// The <see cref="Akka.Actor.ActorSystem"/> used to create actors for the <see cref="NeoSystem"/>.
        /// </summary>
        public ActorSystem ActorSystem { get; } = ActorSystem.Create(nameof(NeoSystem),
            $"akka {{ log-dead-letters = off , loglevel = warning, loggers = [ \"{typeof(Logger).AssemblyQualifiedName}\" ] }}" +
            $"blockchain-mailbox {{ mailbox-type: \"{typeof(BlockchainMailbox).AssemblyQualifiedName}\" }}" +
            $"task-manager-mailbox {{ mailbox-type: \"{typeof(TaskManagerMailbox).AssemblyQualifiedName}\" }}" +
            $"remote-node-mailbox {{ mailbox-type: \"{typeof(RemoteNodeMailbox).AssemblyQualifiedName}\" }}");

        /// <summary>
        /// The genesis block of the NEO blockchain.
        /// </summary>
        public Block GenesisBlock { get; }

        /// <summary>
        /// The <see cref="Ledger.Blockchain"/> actor of the <see cref="NeoSystem"/>.
        /// </summary>
        public IActorRef Blockchain { get; }

        /// <summary>
        /// The <see cref="Network.P2P.LocalNode"/> actor of the <see cref="NeoSystem"/>.
        /// </summary>
        public IActorRef LocalNode { get; }

        /// <summary>
        /// The <see cref="Network.P2P.TaskManager"/> actor of the <see cref="NeoSystem"/>.
        /// </summary>
        public IActorRef TaskManager { get; }

        /// <summary>
        /// The transaction router actor of the <see cref="NeoSystem"/>.
        /// </summary>
        public IActorRef TxRouter;

        /// <summary>
        /// A readonly view of the store.
        /// </summary>
        /// <remarks>
        /// It doesn't need to be disposed because the <see cref="ISnapshot"/> inside it is null.
        /// </remarks>
        public DataCache StoreView => new SnapshotCache(store);

        /// <summary>
        /// The memory pool of the <see cref="NeoSystem"/>.
        /// </summary>
        public MemoryPool MemPool { get; }

        /// <summary>
        /// The header cache of the <see cref="NeoSystem"/>.
        /// </summary>
        public HeaderCache HeaderCache { get; } = new();

        internal RelayCache RelayCache { get; } = new(100);

        private ImmutableList<object> services = ImmutableList<object>.Empty;
        private ImmutableList<Plugin> plugins = ImmutableList<Plugin>.Empty;
        private readonly string storage_engine;
        private readonly IStore store;
        private ChannelsConfig start_message = null;
        private int suspend = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="NeoSystem"/> class.
        /// </summary>
        /// <param name="settings">The protocol settings of the <see cref="NeoSystem"/>.</param>
        /// <param name="storageEngine">The storage engine used to create the <see cref="IStore"/> objects. If this parameter is <see langword="null"/>, a default in-memory storage engine will be used.</param>
        /// <param name="storagePath">The path of the storage. If <paramref name="storageEngine"/> is the default in-memory storage engine, this parameter is ignored.</param>
        /// <param name="plugins">Collection of Plugins to preload into NeoSystem instance</param>
        public NeoSystem(ProtocolSettings settings, string storageEngine = null, string storagePath = null, IEnumerable<Plugin> plugins = null)
        {
            this.plugins = plugins == null ? ImmutableList<Plugin>.Empty : plugins.ToImmutableList();

            ActorSystem.ActorSelection("/system/log*").Tell(new Logger.SetNeoSystem() { NeoSystem = this });

            AppDomain.CurrentDomain.UnhandledException += (sender, args) => CurrentDomain_UnhandledException(this, sender, args);

            this.Settings = settings;
            this.GenesisBlock = CreateGenesisBlock(settings);
            this.storage_engine = storageEngine;
            this.store = LoadStore(storagePath);
            this.MemPool = new MemoryPool(this);
            this.Blockchain = ActorSystem.ActorOf(Ledger.Blockchain.Props(this));
            this.LocalNode = ActorSystem.ActorOf(Network.P2P.LocalNode.Props(this));
            this.TaskManager = ActorSystem.ActorOf(Network.P2P.TaskManager.Props(this));
            this.TxRouter = ActorSystem.ActorOf(TransactionRouter.Props(this));
            foreach (var plugin in this.plugins)
                plugin.OnSystemLoaded(this);
            Blockchain.Ask(new Blockchain.Initialize()).Wait();
        }

        /// <summary>
        /// Creates the genesis block for the NEO blockchain.
        /// </summary>
        /// <param name="settings">The <see cref="ProtocolSettings"/> of the NEO system.</param>
        /// <returns>The genesis block.</returns>
        public static Block CreateGenesisBlock(ProtocolSettings settings) => new()
        {
            Header = new Header
            {
                PrevHash = UInt256.Zero,
                MerkleRoot = UInt256.Zero,
                Timestamp = (new DateTime(2016, 7, 15, 15, 8, 21, DateTimeKind.Utc)).ToTimestampMS(),
                Nonce = 2083236893, // nonce from the Bitcoin genesis block.
                Index = 0,
                PrimaryIndex = 0,
                NextConsensus = Contract.GetBFTAddress(settings.StandbyValidators),
                Witness = new Witness
                {
                    InvocationScript = Array.Empty<byte>(),
                    VerificationScript = new[] { (byte)OpCode.PUSH1 }
                },
            },
            Transactions = Array.Empty<Transaction>()
        };

        private void CurrentDomain_UnhandledException(NeoSystem that, object sender, UnhandledExceptionEventArgs e)
        {
            if (object.ReferenceEquals(this, that))
            {
                Log("UnhandledException", LogLevel.Fatal, e.ExceptionObject);
            }
        }

        public void Dispose()
        {
            foreach (var p in plugins) p.Dispose();

            EnsureStoped(LocalNode);
            // Dispose will call ActorSystem.Terminate()
            ActorSystem.Dispose();
            ActorSystem.WhenTerminated.Wait();
            HeaderCache.Dispose();
            store.Dispose();
        }

        /// <summary>
        /// Adds a service to the <see cref="NeoSystem"/>.
        /// </summary>
        /// <param name="service">The service object to be added.</param>
        public void AddService(object service)
        {
            ImmutableInterlocked.Update(ref services, p => p.Add(service));
            ServiceAdded?.Invoke(this, service);
        }

        /// <summary>
        /// Adds a Plugin to the <see cref="NeoSystem"/>.
        /// </summary>
        /// <param name="plugin">The plugin object to be added.</param>
        public void AddPlugin(Plugin plugin)
        {
            ImmutableInterlocked.Update(ref plugins, p => p.Add(plugin));
            plugin.OnSystemLoaded(this);
        }

        class Logger : ReceiveActor
        {
            public class SetNeoSystem { public NeoSystem NeoSystem { get; init; } }

            NeoSystem system = null;

            public Logger()
            {
                Receive<InitializeLogger>(_ => Sender.Tell(new LoggerInitialized()));
                Receive<SetNeoSystem>(sns => Interlocked.CompareExchange(ref system, sns.NeoSystem, null));
                Receive<LogEvent>(e =>
                {
                    var logLevel = e.LogLevel() switch
                    {
                        Akka.Event.LogLevel.DebugLevel => Neo.LogLevel.Debug,
                        Akka.Event.LogLevel.ErrorLevel => Neo.LogLevel.Error,
                        Akka.Event.LogLevel.InfoLevel => Neo.LogLevel.Info,
                        Akka.Event.LogLevel.WarningLevel => Neo.LogLevel.Warning,
                        _ => Neo.LogLevel.Info,
                    };
                    system?.Log(e.LogSource, logLevel, e.Message);
                });
            }
        }

        public IEnumerable<IMemoryPoolTxObserverPlugin> TxObserverPlugins => plugins.OfType<IMemoryPoolTxObserverPlugin>();
        public IEnumerable<IPersistencePlugin> PersistencePlugins => plugins.OfType<IPersistencePlugin>();
        public IEnumerable<IP2PPlugin> P2PPlugins => plugins.OfType<IP2PPlugin>();

        /// <summary>
        /// Sends a message to all plugins. It can be handled by <see cref="Plugin.OnMessage"/>.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns><see langword="true"/> if the <paramref name="message"/> is handled by a plugin; otherwise, <see langword="false"/>.</returns>
        public bool SendPluginMessage(object message)
        {
            for (int i = 0; i < plugins.Count; i++)
            {
                if (plugins[i].OnMessage(message)) return true;
            }

            return false;
        }

        public void Log(string source, LogLevel level, object message)
        {
            for (int i = 0; i < plugins.Count; i++)
            {
                if (plugins[i] is ILogPlugin logPlugin)
                {
                    logPlugin.Log(source, level, message);
                }
            }
        }

        /// <summary>
        /// Gets a specified type of service object from the <see cref="NeoSystem"/>.
        /// </summary>
        /// <typeparam name="T">The type of the service object.</typeparam>
        /// <param name="filter">An action used to filter the service objects. This parameter can be <see langword="null"/>.</param>
        /// <returns>The service object found.</returns>
        public T GetService<T>(Func<T, bool> filter = null)
        {
            IEnumerable<T> result = services.OfType<T>();
            if (filter is null)
                return result.FirstOrDefault();
            else
                return result.FirstOrDefault(filter);
        }

        /// <summary>
        /// Blocks the current thread until the specified actor has stopped.
        /// </summary>
        /// <param name="actor">The actor to wait.</param>
        public void EnsureStoped(IActorRef actor)
        {
            using Inbox inbox = Inbox.Create(ActorSystem);
            inbox.Watch(actor);
            ActorSystem.Stop(actor);
            inbox.Receive(TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Loads an <see cref="IStore"/> at the specified path.
        /// </summary>
        /// <param name="path">The path of the storage.</param>
        /// <returns>The loaded <see cref="IStore"/>.</returns>
        public IStore LoadStore(string path)
        {
            if (string.IsNullOrEmpty(storage_engine) || storage_engine == nameof(MemoryStore))
                return new MemoryStore();

            for (int i = 0; i < plugins.Count; i++)
            {
                if (plugins[i] is IStorageProvider storageProvider
                    && plugins[i].Name == storage_engine)
                {
                    return storageProvider.GetStore(path);
                }
            }

            throw new InvalidOperationException($"{storage_engine} store not found");
        }

        /// <summary>
        /// Resumes the startup process of <see cref="LocalNode"/>.
        /// </summary>
        /// <returns><see langword="true"/> if the startup process is resumed; otherwise, <see langword="false"/>.</returns>
        public bool ResumeNodeStartup()
        {
            if (Interlocked.Decrement(ref suspend) != 0)
                return false;
            if (start_message != null)
            {
                LocalNode.Tell(start_message);
                start_message = null;
            }
            return true;
        }

        /// <summary>
        /// Starts the <see cref="LocalNode"/> with the specified configuration.
        /// </summary>
        /// <param name="config">The configuration used to start the <see cref="LocalNode"/>.</param>
        public void StartNode(ChannelsConfig config)
        {
            start_message = config;

            if (suspend == 0)
            {
                LocalNode.Tell(start_message);
                start_message = null;
            }
        }

        /// <summary>
        /// Suspends the startup process of <see cref="LocalNode"/>.
        /// </summary>
        public void SuspendNodeStartup()
        {
            Interlocked.Increment(ref suspend);
        }

        /// <summary>
        /// Gets a snapshot of the blockchain storage.
        /// </summary>
        /// <returns></returns>
        public SnapshotCache GetSnapshot()
        {
            return new SnapshotCache(store.GetSnapshot());
        }

        /// <summary>
        /// Determines whether the specified transaction exists in the memory pool or storage.
        /// </summary>
        /// <param name="hash">The hash of the transaction</param>
        /// <returns><see langword="true"/> if the transaction exists; otherwise, <see langword="false"/>.</returns>
        public bool ContainsTransaction(UInt256 hash)
        {
            if (MemPool.ContainsKey(hash)) return true;
            return NativeContract.Ledger.ContainsTransaction(StoreView, hash);
        }
    }
}
