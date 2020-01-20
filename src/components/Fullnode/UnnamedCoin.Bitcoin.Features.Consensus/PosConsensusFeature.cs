using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Base;
using UnnamedCoin.Bitcoin.Base.Deployments;
using UnnamedCoin.Bitcoin.Builder.Feature;
using UnnamedCoin.Bitcoin.Configuration.Settings;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Features.Consensus.Behaviors;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.Signals;

[assembly: InternalsVisibleTo("Stratis.Bitcoin.Features.Miner.Tests")]
[assembly: InternalsVisibleTo("Stratis.Bitcoin.Features.Consensus.Tests")]

namespace UnnamedCoin.Bitcoin.Features.Consensus
{
    public class PosConsensusFeature : ConsensusFeature
    {
        readonly ChainIndexer chainIndexer;
        readonly IChainState chainState;
        readonly ICheckpoints checkpoints;
        readonly IConnectionManager connectionManager;
        readonly ConnectionManagerSettings connectionManagerSettings;
        readonly IConsensusManager consensusManager;
        readonly IInitialBlockDownloadState initialBlockDownloadState;
        readonly ILoggerFactory loggerFactory;
        readonly Network network;
        readonly NodeDeployments nodeDeployments;
        readonly IPeerBanning peerBanning;
        readonly IProvenBlockHeaderStore provenBlockHeaderStore;

        public PosConsensusFeature(
            Network network,
            IChainState chainState,
            IConnectionManager connectionManager,
            IConsensusManager consensusManager,
            NodeDeployments nodeDeployments,
            ChainIndexer chainIndexer,
            IInitialBlockDownloadState initialBlockDownloadState,
            IPeerBanning peerBanning,
            ISignals signals,
            ILoggerFactory loggerFactory,
            ICheckpoints checkpoints,
            IProvenBlockHeaderStore provenBlockHeaderStore,
            ConnectionManagerSettings connectionManagerSettings) : base(network, chainState, connectionManager, signals,
            consensusManager, nodeDeployments)
        {
            this.network = network;
            this.chainState = chainState;
            this.connectionManager = connectionManager;
            this.consensusManager = consensusManager;
            this.nodeDeployments = nodeDeployments;
            this.chainIndexer = chainIndexer;
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.peerBanning = peerBanning;
            this.loggerFactory = loggerFactory;
            this.checkpoints = checkpoints;
            this.provenBlockHeaderStore = provenBlockHeaderStore;
            this.connectionManagerSettings = connectionManagerSettings;

            this.chainState.MaxReorgLength = network.Consensus.MaxReorgLength;
        }

        /// <summary>
        ///     Prints command-line help. Invoked via reflection.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public new static void PrintHelp(Network network)
        {
            ConsensusFeature.PrintHelp(network);
        }

        /// <summary>
        ///     Get the default configuration. Invoked via reflection.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public new static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            ConsensusFeature.BuildDefaultConfigurationFile(builder, network);
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            base.InitializeAsync();

            var connectionParameters = this.connectionManager.Parameters;

            var defaultConsensusManagerBehavior =
                connectionParameters.TemplateBehaviors.FirstOrDefault(behavior => behavior is ConsensusManagerBehavior);
            if (defaultConsensusManagerBehavior == null)
                throw new MissingServiceException(typeof(ConsensusManagerBehavior),
                    "Missing expected ConsensusManagerBehavior.");

            // Replace default ConsensusManagerBehavior with ProvenHeadersConsensusManagerBehavior
            connectionParameters.TemplateBehaviors.Remove(defaultConsensusManagerBehavior);
            connectionParameters.TemplateBehaviors.Add(new ProvenHeadersConsensusManagerBehavior(this.chainIndexer,
                this.initialBlockDownloadState, this.consensusManager, this.peerBanning, this.loggerFactory,
                this.network, this.chainState, this.checkpoints, this.provenBlockHeaderStore,
                this.connectionManagerSettings));

            connectionParameters.TemplateBehaviors.Add(
                new ProvenHeadersReservedSlotsBehavior(this.connectionManager, this.loggerFactory));

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }
    }
}