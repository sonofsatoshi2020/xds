using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Base.Deployments;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Features.Consensus;
using UnnamedCoin.Bitcoin.Features.Consensus.Interfaces;
using UnnamedCoin.Bitcoin.Features.Consensus.Rules.CommonRules;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Interfaces;
using UnnamedCoin.Bitcoin.Mining;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Miner
{
    /// <summary>
    ///     Defines how a proof of work block will be built on a proof of stake network.
    /// </summary>
    public sealed class PosPowBlockDefinition : BlockDefinition
    {
        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Database of stake related data for the current blockchain.</summary>
        readonly IStakeChain stakeChain;

        /// <summary>Provides functionality for checking validity of PoS blocks.</summary>
        readonly IStakeValidator stakeValidator;

        /// <summary>
        ///     The POS rule to determine the allowed drift in time between nodes.
        /// </summary>
        PosFutureDriftRule futureDriftRule;

        public PosPowBlockDefinition(
            IConsensusManager consensusManager,
            IDateTimeProvider dateTimeProvider,
            ILoggerFactory loggerFactory,
            ITxMempool mempool,
            MempoolSchedulerLock mempoolLock,
            Network network,
            MinerSettings minerSettings,
            IStakeChain stakeChain,
            IStakeValidator stakeValidator,
            NodeDeployments nodeDeployments)
            : base(consensusManager, dateTimeProvider, loggerFactory, mempool, mempoolLock, minerSettings, network,
                nodeDeployments)
        {
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.stakeChain = stakeChain;
            this.stakeValidator = stakeValidator;
        }

        /// <inheritdoc />
        public override void AddToBlock(TxMempoolEntry mempoolEntry)
        {
            AddTransactionToBlock(mempoolEntry.Transaction);
            UpdateBlockStatistics(mempoolEntry);
            UpdateTotalFees(mempoolEntry.Fee);
        }

        /// <inheritdoc />
        public override BlockTemplate Build(ChainedHeader chainTip, Script scriptPubKey)
        {
            OnBuild(chainTip, scriptPubKey);

            return this.BlockTemplate;
        }

        /// <inheritdoc />
        public override void UpdateHeaders()
        {
            UpdateBaseHeaders();

            this.block.Header.Bits =
                this.stakeValidator.GetNextTargetRequired(this.stakeChain, this.ChainTip, this.Network.Consensus,
                    false);
        }

        /// <inheritdoc />
        protected override bool TestPackage(TxMempoolEntry entry, long packageSize, long packageSigOpsCost)
        {
            if (this.futureDriftRule == null)
                this.futureDriftRule = this.ConsensusManager.ConsensusRules.GetRule<PosFutureDriftRule>();

            var adjustedTime = this.DateTimeProvider.GetAdjustedTimeAsUnixTimestamp();
            var latestValidTime = adjustedTime + this.futureDriftRule.GetFutureDrift(adjustedTime);

            // We can include txes with timestamp greater than header's timestamp and those txes are invalid to have in block.
            // However this is needed in order to avoid recreation of block template on every attempt to find kernel.
            // When kernel is found txes with timestamp greater than header's timestamp are removed.
            if (entry.Transaction is IPosTransactionWithTime posTrx)
                if (posTrx.Time > latestValidTime)
                {
                    this.logger.LogDebug(
                        "Transaction '{0}' has timestamp of {1} but latest valid tx time that can be mined is {2}.",
                        entry.TransactionHash, posTrx.Time, latestValidTime);
                    this.logger.LogTrace("(-)[TOO_EARLY_TO_MINE_TX]:false");
                    return false;
                }

            return base.TestPackage(entry, packageSize, packageSigOpsCost);
        }
    }
}