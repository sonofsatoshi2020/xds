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
    public class PosBlockDefinition : BlockDefinition
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

        public PosBlockDefinition(
            IConsensusManager consensusManager,
            IDateTimeProvider dateTimeProvider,
            ILoggerFactory loggerFactory,
            ITxMempool mempool,
            MempoolSchedulerLock mempoolLock,
            MinerSettings minerSettings,
            Network network,
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

            this.coinbase.Outputs[0].ScriptPubKey = new Script();
            this.coinbase.Outputs[0].Value = Money.Zero;

            return this.BlockTemplate;
        }

        /// <inheritdoc />
        public override void UpdateHeaders()
        {
            UpdateBaseHeaders();

            this.block.Header.Bits =
                this.stakeValidator.GetNextTargetRequired(this.stakeChain, this.ChainTip, this.Network.Consensus, true);
        }

        /// <inheritdoc />
        protected override bool TestPackage(TxMempoolEntry entry, long packageSize, long packageSigOpsCost)
        {
            if (this.futureDriftRule == null)
                this.futureDriftRule = this.ConsensusManager.ConsensusRules.GetRule<PosFutureDriftRule>();

            if (entry.Transaction is IPosTransactionWithTime posTrx)
            {
                var adjustedTime = this.DateTimeProvider.GetAdjustedTimeAsUnixTimestamp();

                if (posTrx.Time > adjustedTime + this.futureDriftRule.GetFutureDrift(adjustedTime))
                    return false;

                if (posTrx.Time > ((PosTransaction) this.block.Transactions[0]).Time)
                    return false;
            }

            return base.TestPackage(entry, packageSize, packageSigOpsCost);
        }
    }
}