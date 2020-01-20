using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Base.Deployments;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Interfaces;
using UnnamedCoin.Bitcoin.Mining;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Miner
{
    public class PowBlockDefinition : BlockDefinition
    {
        readonly IConsensusRuleEngine consensusRules;
        readonly ILogger logger;

        public PowBlockDefinition(
            IConsensusManager consensusManager,
            IDateTimeProvider dateTimeProvider,
            ILoggerFactory loggerFactory,
            ITxMempool mempool,
            MempoolSchedulerLock mempoolLock,
            MinerSettings minerSettings,
            Network network,
            IConsensusRuleEngine consensusRules,
            NodeDeployments nodeDeployments,
            BlockDefinitionOptions options = null)
            : base(consensusManager, dateTimeProvider, loggerFactory, mempool, mempoolLock, minerSettings, network,
                nodeDeployments)
        {
            this.consensusRules = consensusRules;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
        }

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

            this.block.Header.Bits = this.block.Header.GetWorkRequired(this.Network, this.ChainTip);
        }
    }
}