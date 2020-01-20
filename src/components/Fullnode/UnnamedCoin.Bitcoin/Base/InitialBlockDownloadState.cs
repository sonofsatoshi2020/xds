using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Configuration.Settings;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Base
{
    /// <summary>
    ///     Provides IBD (Initial Block Download) state.
    /// </summary>
    /// <seealso cref="IInitialBlockDownloadState" />
    public class InitialBlockDownloadState : IInitialBlockDownloadState
    {
        /// <summary>Information about node's chain.</summary>
        readonly IChainState chainState;

        /// <summary>Provider of block header hash checkpoints.</summary>
        readonly ICheckpoints checkpoints;

        /// <summary>User defined consensus settings.</summary>
        readonly ConsensusSettings consensusSettings;

        /// <summary>A provider of the date and time.</summary>
        readonly IDateTimeProvider dateTimeProvider;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        readonly Network network;

        /// <summary>
        ///     Creates a new instance of the <see cref="InitialBlockDownloadState" /> class.
        /// </summary>
        /// <param name="chainState">Information about node's chain.</param>
        /// <param name="network">Specification of the network the node runs on - regtest/testnet/mainnet.</param>
        /// <param name="consensusSettings">Configurable settings for the consensus feature.</param>
        /// <param name="checkpoints">Provider of block header hash checkpoints.</param>
        /// <param name="loggerFactory">Provides us with a logger.</param>
        public InitialBlockDownloadState(IChainState chainState, Network network, ConsensusSettings consensusSettings,
            ICheckpoints checkpoints, ILoggerFactory loggerFactory, IDateTimeProvider dateTimeProvider)
        {
            this.network = network;
            this.consensusSettings = consensusSettings;
            this.chainState = chainState;
            this.checkpoints = checkpoints;
            this.dateTimeProvider = dateTimeProvider;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
        }

        /// <inheritdoc />
        public bool IsInitialBlockDownload()
        {
            if (this.chainState == null)
                return false;

            if (this.chainState.ConsensusTip == null)
                return true;

            if (this.checkpoints.GetLastCheckpointHeight() > this.chainState.ConsensusTip.Height)
                return true;

            if (this.chainState.ConsensusTip.ChainWork < (this.network.Consensus.MinimumChainWork ?? uint256.Zero))
                return true;

            this.logger.LogDebug("BlockTimeUnixSeconds={0}, DateTimeProviderTime={1}, ConsensusSettingsMaxTipAge={2}",
                this.chainState.ConsensusTip.Header.BlockTime.ToUnixTimeSeconds(),
                this.dateTimeProvider.GetTime(),
                this.consensusSettings.MaxTipAge);

            if (this.chainState.ConsensusTip.Header.BlockTime.ToUnixTimeSeconds() <
                this.dateTimeProvider.GetTime() - this.consensusSettings.MaxTipAge)
                return true;

            return false;
        }
    }
}