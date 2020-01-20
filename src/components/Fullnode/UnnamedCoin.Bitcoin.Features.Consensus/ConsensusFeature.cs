using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using UnnamedCoin.Bitcoin.Base;
using UnnamedCoin.Bitcoin.Base.Deployments;
using UnnamedCoin.Bitcoin.Builder.Feature;
using UnnamedCoin.Bitcoin.Configuration.Settings;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;
using UnnamedCoin.Bitcoin.Signals;

[assembly: InternalsVisibleTo("Stratis.Bitcoin.Features.Miner.Tests")]
[assembly: InternalsVisibleTo("Stratis.Bitcoin.Features.Consensus.Tests")]

namespace UnnamedCoin.Bitcoin.Features.Consensus
{
    public class ConsensusFeature : FullNodeFeature
    {
        readonly IChainState chainState;

        readonly IConnectionManager connectionManager;

        readonly IConsensusManager consensusManager;

        readonly NodeDeployments nodeDeployments;

        readonly ISignals signals;

        public ConsensusFeature(
            Network network,
            IChainState chainState,
            IConnectionManager connectionManager,
            ISignals signals,
            IConsensusManager consensusManager,
            NodeDeployments nodeDeployments)
        {
            this.chainState = chainState;
            this.connectionManager = connectionManager;
            this.signals = signals;
            this.consensusManager = consensusManager;
            this.nodeDeployments = nodeDeployments;

            this.chainState.MaxReorgLength = network.Consensus.MaxReorgLength;
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            var flags = this.nodeDeployments.GetFlags(this.consensusManager.Tip);

            if (flags.ScriptFlags.HasFlag(ScriptVerify.Witness))
            {
                // Add witness discovery as a requirement if witness is activated.
                this.connectionManager.AddDiscoveredNodesRequirement(NetworkPeerServices.NODE_WITNESS);

                // Set witness as a supported service if witness is activated.
                this.connectionManager.Parameters.Services |= NetworkPeerServices.NODE_WITNESS;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            ConsensusSettings.PrintHelp(network);
        }

        /// <summary>
        ///     Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            ConsensusSettings.BuildDefaultConfigurationFile(builder, network);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }
    }
}