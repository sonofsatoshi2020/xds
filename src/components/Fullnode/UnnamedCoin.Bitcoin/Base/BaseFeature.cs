using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Rules;
using UnnamedCoin.Bitcoin.AsyncWork;
using UnnamedCoin.Bitcoin.Base.Deployments;
using UnnamedCoin.Bitcoin.BlockPulling;
using UnnamedCoin.Bitcoin.Builder;
using UnnamedCoin.Bitcoin.Builder.Feature;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Configuration.Settings;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Consensus.Rules;
using UnnamedCoin.Bitcoin.Consensus.Validators;
using UnnamedCoin.Bitcoin.Controllers;
using UnnamedCoin.Bitcoin.EventBus;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.P2P;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol.Behaviors;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;
using UnnamedCoin.Bitcoin.Signals;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Base
{
    /// <summary>
    ///     Base node services, these are the services a node has to have.
    ///     The ConnectionManager feature is also part of the base but may go in a feature of its own.
    ///     The base features are the minimal components required to connect to peers and maintain the best chain.
    ///     <para>
    ///         The base node services for a node are:
    ///         <list type="bullet">
    ///             <item>the ConcurrentChain to keep track of the best chain,</item>
    ///             <item>the ConnectionManager to connect with the network,</item>
    ///             <item>DatetimeProvider and Cancellation,</item>
    ///             <item>CancellationProvider and Cancellation,</item>
    ///             <item>DataFolder,</item>
    ///             <item>ChainState.</item>
    ///         </list>
    ///     </para>
    /// </summary>
    public sealed class BaseFeature : FullNodeFeature
    {
        /// <summary>Provider for creating and managing background async loop tasks.</summary>
        readonly IAsyncProvider asyncProvider;

        readonly IBlockPuller blockPuller;
        readonly IBlockStore blockStore;

        /// <summary>Thread safe chain of block headers from genesis.</summary>
        readonly ChainIndexer chainIndexer;

        /// <summary>Access to the database of blocks.</summary>
        readonly IChainRepository chainRepository;

        /// <summary>Information about node's chain.</summary>
        readonly IChainState chainState;

        /// <summary>Manager of node's network connections.</summary>
        readonly IConnectionManager connectionManager;

        readonly IConsensusManager consensusManager;
        readonly IConsensusRuleEngine consensusRules;

        /// <summary>Locations of important folders and files on disk.</summary>
        readonly DataFolder dataFolder;

        /// <summary>Provider of time functions.</summary>
        readonly IDateTimeProvider dateTimeProvider;

        /// <inheritdoc cref="IFinalizedBlockInfoRepository" />
        readonly IFinalizedBlockInfoRepository finalizedBlockInfoRepository;

        /// <summary>Provider of IBD state.</summary>
        readonly IInitialBlockDownloadState initialBlockDownloadState;

        readonly IKeyValueRepository keyValueRepo;

        /// <summary>Logger for the node.</summary>
        readonly ILogger logger;

        /// <summary>Factory for creating loggers.</summary>
        readonly ILoggerFactory loggerFactory;

        /// <inheritdoc cref="Network" />
        readonly Network network;

        /// <summary>Global application life cycle control - triggers when application shuts down.</summary>
        readonly INodeLifetime nodeLifetime;

        /// <summary>User defined node settings.</summary>
        readonly NodeSettings nodeSettings;

        readonly INodeStats nodeStats;

        /// <inheritdoc cref="IPartialValidator" />
        readonly IPartialValidator partialValidator;

        /// <summary>A handler that can manage the lifetime of network peers.</summary>
        readonly IPeerBanning peerBanning;

        readonly IProvenBlockHeaderStore provenBlockHeaderStore;

        /// <summary>State of time synchronization feature that stores collected data samples.</summary>
        readonly ITimeSyncBehaviorState timeSyncBehaviorState;

        readonly ITipsManager tipsManager;

        /// <summary>Periodic task to save list of peers to disk.</summary>
        IAsyncLoop flushAddressManagerLoop;

        /// <summary>Periodic task to save the chain to the database.</summary>
        IAsyncLoop flushChainLoop;

        /// <summary>Manager of node's network peers.</summary>
        readonly IPeerAddressManager peerAddressManager;

        public BaseFeature(NodeSettings nodeSettings,
            DataFolder dataFolder,
            INodeLifetime nodeLifetime,
            ChainIndexer chainIndexer,
            IChainState chainState,
            IConnectionManager connectionManager,
            IChainRepository chainRepository,
            IFinalizedBlockInfoRepository finalizedBlockInfo,
            IDateTimeProvider dateTimeProvider,
            IAsyncProvider asyncProvider,
            ITimeSyncBehaviorState timeSyncBehaviorState,
            ILoggerFactory loggerFactory,
            IInitialBlockDownloadState initialBlockDownloadState,
            IPeerBanning peerBanning,
            IPeerAddressManager peerAddressManager,
            IConsensusManager consensusManager,
            IConsensusRuleEngine consensusRules,
            IPartialValidator partialValidator,
            IBlockPuller blockPuller,
            IBlockStore blockStore,
            Network network,
            ITipsManager tipsManager,
            IKeyValueRepository keyValueRepo,
            INodeStats nodeStats,
            IProvenBlockHeaderStore provenBlockHeaderStore = null)
        {
            this.chainState = Guard.NotNull(chainState, nameof(chainState));
            this.chainRepository = Guard.NotNull(chainRepository, nameof(chainRepository));
            this.finalizedBlockInfoRepository = Guard.NotNull(finalizedBlockInfo, nameof(finalizedBlockInfo));
            this.nodeSettings = Guard.NotNull(nodeSettings, nameof(nodeSettings));
            this.dataFolder = Guard.NotNull(dataFolder, nameof(dataFolder));
            this.nodeLifetime = Guard.NotNull(nodeLifetime, nameof(nodeLifetime));
            this.chainIndexer = Guard.NotNull(chainIndexer, nameof(chainIndexer));
            this.connectionManager = Guard.NotNull(connectionManager, nameof(connectionManager));
            this.consensusManager = consensusManager;
            this.consensusRules = consensusRules;
            this.blockPuller = blockPuller;
            this.blockStore = blockStore;
            this.network = network;
            this.nodeStats = nodeStats;
            this.provenBlockHeaderStore = provenBlockHeaderStore;
            this.partialValidator = partialValidator;
            this.peerBanning = Guard.NotNull(peerBanning, nameof(peerBanning));
            this.tipsManager = Guard.NotNull(tipsManager, nameof(tipsManager));
            this.keyValueRepo = Guard.NotNull(keyValueRepo, nameof(keyValueRepo));

            this.peerAddressManager = Guard.NotNull(peerAddressManager, nameof(peerAddressManager));
            this.peerAddressManager.PeerFilePath = this.dataFolder;

            this.initialBlockDownloadState = initialBlockDownloadState;
            this.dateTimeProvider = dateTimeProvider;
            this.asyncProvider = asyncProvider;
            this.timeSyncBehaviorState = timeSyncBehaviorState;
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
        }

        /// <inheritdoc />
        public override async Task InitializeAsync()
        {
            // TODO rewrite chain starting logic. Tips manager should be used.

            await StartChainAsync().ConfigureAwait(false);

            if (this.provenBlockHeaderStore != null)
            {
                var initializedAt = await this.provenBlockHeaderStore.InitializeAsync(this.chainIndexer.Tip);
                this.chainIndexer.Initialize(initializedAt);
            }

            var connectionParameters = this.connectionManager.Parameters;
            connectionParameters.IsRelay = this.connectionManager.ConnectionSettings.RelayTxes;

            connectionParameters.TemplateBehaviors.Add(new PingPongBehavior());
            connectionParameters.TemplateBehaviors.Add(new EnforcePeerVersionCheckBehavior(this.chainIndexer,
                this.nodeSettings, this.network, this.loggerFactory));
            connectionParameters.TemplateBehaviors.Add(new ConsensusManagerBehavior(this.chainIndexer,
                this.initialBlockDownloadState, this.consensusManager, this.peerBanning, this.loggerFactory));

            // TODO: Once a proper rate limiting strategy has been implemented, this check will be removed.
            if (!this.network.IsRegTest())
                connectionParameters.TemplateBehaviors.Add(new RateLimitingBehavior(this.dateTimeProvider,
                    this.loggerFactory, this.peerBanning));

            connectionParameters.TemplateBehaviors.Add(new PeerBanningBehavior(this.loggerFactory, this.peerBanning,
                this.nodeSettings));
            connectionParameters.TemplateBehaviors.Add(new BlockPullerBehavior(this.blockPuller,
                this.initialBlockDownloadState, this.dateTimeProvider, this.loggerFactory));
            connectionParameters.TemplateBehaviors.Add(new ConnectionManagerBehavior(this.connectionManager,
                this.loggerFactory));

            StartAddressManager(connectionParameters);

            if (this.connectionManager.ConnectionSettings.SyncTimeEnabled)
                connectionParameters.TemplateBehaviors.Add(new TimeSyncBehavior(this.timeSyncBehaviorState,
                    this.dateTimeProvider, this.loggerFactory));
            else
                this.logger.LogDebug("Time synchronization with peers is disabled.");

            // Block store must be initialized before consensus manager.
            // This may be a temporary solution until a better way is found to solve this dependency.
            this.blockStore.Initialize();

            this.consensusRules.Initialize(this.chainIndexer.Tip);

            await this.consensusManager.InitializeAsync(this.chainIndexer.Tip).ConfigureAwait(false);

            this.chainState.ConsensusTip = this.consensusManager.Tip;

            this.nodeStats.RegisterStats(
                sb => sb.Append(this.asyncProvider.GetStatistics(!this.nodeSettings.Log.DebugArgs.Any())),
                StatsType.Component, GetType().Name, 100);
        }

        /// <summary>
        ///     Initializes node's chain repository.
        ///     Creates periodic task to persist changes to the database.
        /// </summary>
        async Task StartChainAsync()
        {
            if (!Directory.Exists(this.dataFolder.ChainPath))
            {
                this.logger.LogInformation("Creating {0}.", this.dataFolder.ChainPath);
                Directory.CreateDirectory(this.dataFolder.ChainPath);
            }

            if (!Directory.Exists(this.dataFolder.KeyValueRepositoryPath))
            {
                this.logger.LogInformation("Creating {0}.", this.dataFolder.KeyValueRepositoryPath);
                Directory.CreateDirectory(this.dataFolder.KeyValueRepositoryPath);
            }

            this.logger.LogInformation("Loading finalized block height.");
            await this.finalizedBlockInfoRepository.LoadFinalizedBlockInfoAsync(this.network).ConfigureAwait(false);

            this.logger.LogInformation("Loading chain.");
            var chainTip = await this.chainRepository.LoadAsync(this.chainIndexer.Genesis).ConfigureAwait(false);
            this.chainIndexer.Initialize(chainTip);

            this.logger.LogInformation("Chain loaded at height {0}.", this.chainIndexer.Height);

            this.flushChainLoop = this.asyncProvider.CreateAndRunAsyncLoop("FlushChain", async token =>
                {
                    await this.chainRepository.SaveAsync(this.chainIndexer).ConfigureAwait(false);

                    if (this.provenBlockHeaderStore != null)
                        await this.provenBlockHeaderStore.SaveAsync().ConfigureAwait(false);
                },
                this.nodeLifetime.ApplicationStopping,
                TimeSpan.FromMinutes(1.0),
                TimeSpan.FromMinutes(1.0));
        }

        /// <summary>
        ///     Initializes node's address manager. Loads previously known peers from the file
        ///     or creates new peer file if it does not exist. Creates periodic task to persist changes
        ///     in peers to disk.
        /// </summary>
        void StartAddressManager(NetworkPeerConnectionParameters connectionParameters)
        {
            var addressManagerBehaviour = new PeerAddressManagerBehaviour(this.dateTimeProvider,
                this.peerAddressManager, this.peerBanning, this.loggerFactory);
            connectionParameters.TemplateBehaviors.Add(addressManagerBehaviour);

            if (File.Exists(Path.Combine(this.dataFolder.AddressManagerFilePath, PeerAddressManager.PeerFileName)))
            {
                this.logger.LogInformation($"Loading peers from : {this.dataFolder.AddressManagerFilePath}.");
                this.peerAddressManager.LoadPeers();
            }

            this.flushAddressManagerLoop = this.asyncProvider.CreateAndRunAsyncLoop("Periodic peer flush", token =>
                {
                    this.peerAddressManager.SavePeers();
                    return Task.CompletedTask;
                },
                this.nodeLifetime.ApplicationStopping,
                TimeSpan.FromMinutes(5.0),
                TimeSpan.FromMinutes(5.0));
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            this.logger.LogInformation("Flushing peers.");
            this.flushAddressManagerLoop.Dispose();

            this.logger.LogInformation("Disposing peer address manager.");
            this.peerAddressManager.Dispose();

            if (this.flushChainLoop != null)
            {
                this.logger.LogInformation("Flushing headers chain.");
                this.flushChainLoop.Dispose();
            }

            this.logger.LogInformation("Disposing time sync behavior.");
            this.timeSyncBehaviorState.Dispose();

            this.logger.LogInformation("Disposing block puller.");
            this.blockPuller.Dispose();

            this.logger.LogInformation("Disposing partial validator.");
            this.partialValidator.Dispose();

            this.logger.LogInformation("Disposing consensus manager.");
            this.consensusManager.Dispose();

            this.logger.LogInformation("Disposing consensus rules.");
            this.consensusRules.Dispose();

            this.logger.LogInformation("Saving chain repository.");
            this.chainRepository.SaveAsync(this.chainIndexer).GetAwaiter().GetResult();
            this.chainRepository.Dispose();

            if (this.provenBlockHeaderStore != null)
            {
                this.logger.LogInformation("Saving proven header store.");
                this.provenBlockHeaderStore.SaveAsync().GetAwaiter().GetResult();
                this.provenBlockHeaderStore.Dispose();
            }

            this.logger.LogInformation("Disposing finalized block info repository.");
            this.finalizedBlockInfoRepository.Dispose();

            this.logger.LogInformation("Disposing address indexer.");

            this.logger.LogInformation("Disposing block store.");
            this.blockStore.Dispose();

            this.keyValueRepo.Dispose();
        }
    }

    /// <summary>
    ///     A class providing extension methods for <see cref="IFullNodeBuilder" />.
    /// </summary>
    public static class FullNodeBuilderBaseFeatureExtension
    {
        /// <summary>
        ///     Makes the full node use all the required features - <see cref="BaseFeature" />.
        /// </summary>
        /// <param name="fullNodeBuilder">Builder responsible for creating the node.</param>
        /// <returns>Full node builder's interface to allow fluent code.</returns>
        public static IFullNodeBuilder UseBaseFeature(this IFullNodeBuilder fullNodeBuilder)
        {
            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<BaseFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton(fullNodeBuilder.Network.Consensus.ConsensusFactory);
                        services.AddSingleton<DBreezeSerializer>();
                        services.AddSingleton(fullNodeBuilder.NodeSettings.LoggerFactory);
                        services.AddSingleton(fullNodeBuilder.NodeSettings.DataFolder);
                        services.AddSingleton<INodeLifetime, NodeLifetime>();
                        services.AddSingleton<IPeerBanning, PeerBanning>();
                        services.AddSingleton<FullNodeFeatureExecutor>();
                        services.AddSingleton<ISignals, Signals.Signals>();
                        services.AddSingleton<ISubscriptionErrorHandler, DefaultSubscriptionErrorHandler>();
                        services.AddSingleton<FullNode>().AddSingleton(provider =>
                        {
                            return provider.GetService<FullNode>() as IFullNode;
                        });
                        services.AddSingleton(new ChainIndexer(fullNodeBuilder.Network));
                        services.AddSingleton(DateTimeProvider.Default);
                        services.AddSingleton<IInvalidBlockHashStore, InvalidBlockHashStore>();
                        services.AddSingleton<IChainState, ChainState>();
                        services.AddSingleton<IChainRepository, ChainRepository>();
                        services.AddSingleton<IFinalizedBlockInfoRepository, FinalizedBlockInfoRepository>();
                        services.AddSingleton<ITimeSyncBehaviorState, TimeSyncBehaviorState>();
                        services.AddSingleton<NodeDeployments>();
                        services.AddSingleton<IInitialBlockDownloadState, InitialBlockDownloadState>();
                        services.AddSingleton<IKeyValueRepository, KeyValueRepository>();
                        services.AddSingleton<ITipsManager, TipsManager>();
                        services.AddSingleton<IAsyncProvider, AsyncProvider>();

                        // Consensus
                        services.AddSingleton<ConsensusSettings>();
                        services.AddSingleton<ICheckpoints, Checkpoints>();
                        services.AddSingleton<ConsensusRulesContainer>();

                        foreach (var ruleType in fullNodeBuilder.Network.Consensus.ConsensusRules.HeaderValidationRules)
                            services.AddSingleton(typeof(IHeaderValidationConsensusRule), ruleType);

                        foreach (var ruleType in fullNodeBuilder.Network.Consensus.ConsensusRules
                            .IntegrityValidationRules)
                            services.AddSingleton(typeof(IIntegrityValidationConsensusRule), ruleType);

                        foreach (var ruleType in fullNodeBuilder.Network.Consensus.ConsensusRules
                            .PartialValidationRules)
                            services.AddSingleton(typeof(IPartialValidationConsensusRule), ruleType);

                        foreach (var ruleType in fullNodeBuilder.Network.Consensus.ConsensusRules.FullValidationRules)
                            services.AddSingleton(typeof(IFullValidationConsensusRule), ruleType);

                        // Connection
                        services.AddSingleton<INetworkPeerFactory, NetworkPeerFactory>();
                        services.AddSingleton<NetworkPeerConnectionParameters>();
                        services.AddSingleton<IConnectionManager, ConnectionManager>();
                        services.AddSingleton<ConnectionManagerSettings>();
                        services.AddSingleton(new PayloadProvider().DiscoverPayloads());
                        services.AddSingleton<IVersionProvider, VersionProvider>();
                        services.AddSingleton<IBlockPuller, BlockPuller>();

                        // Peer address manager
                        services.AddSingleton<IPeerAddressManager, PeerAddressManager>();
                        services.AddSingleton<IPeerConnector, PeerConnectorAddNode>();
                        services.AddSingleton<IPeerConnector, PeerConnectorConnectNode>();
                        services.AddSingleton<IPeerConnector, PeerConnectorDiscovery>();
                        services.AddSingleton<IPeerDiscovery, PeerDiscovery>();
                        services.AddSingleton<ISelfEndpointTracker, SelfEndpointTracker>();

                        // Consensus
                        // Consensus manager is created like that due to CM's constructor being internal. This is done
                        // in order to prevent access to CM creation and CHT usage from another features. CHT is supposed
                        // to be used only by CM and no other component.
                        services.AddSingleton<IConsensusManager>(provider => new ConsensusManager(
                            provider.GetService<IChainedHeaderTree>(),
                            provider.GetService<Network>(),
                            provider.GetService<ILoggerFactory>(),
                            provider.GetService<IChainState>(),
                            provider.GetService<IIntegrityValidator>(),
                            provider.GetService<IPartialValidator>(),
                            provider.GetService<IFullValidator>(),
                            provider.GetService<IConsensusRuleEngine>(),
                            provider.GetService<IFinalizedBlockInfoRepository>(),
                            provider.GetService<ISignals>(),
                            provider.GetService<IPeerBanning>(),
                            provider.GetService<IInitialBlockDownloadState>(),
                            provider.GetService<ChainIndexer>(),
                            provider.GetService<IBlockPuller>(),
                            provider.GetService<IBlockStore>(),
                            provider.GetService<IConnectionManager>(),
                            provider.GetService<INodeStats>(),
                            provider.GetService<INodeLifetime>(),
                            provider.GetService<ConsensusSettings>(),
                            provider.GetService<IDateTimeProvider>()));

                        services.AddSingleton<IChainedHeaderTree, ChainedHeaderTree>();
                        services.AddSingleton<IHeaderValidator, HeaderValidator>();
                        services.AddSingleton<IIntegrityValidator, IntegrityValidator>();
                        services.AddSingleton<IPartialValidator, PartialValidator>();
                        services.AddSingleton<IFullValidator, FullValidator>();

                        // Console
                        services.AddSingleton<INodeStats, NodeStats>();

                        // Controller
                        services.AddTransient<NodeController>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}