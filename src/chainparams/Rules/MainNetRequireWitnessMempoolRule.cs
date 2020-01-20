using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Interfaces;

namespace ChainParams.Rules
{
    /// <summary>
    /// Checks weather the transaction has witness.
    /// </summary>
    public class MainNetRequireWitnessMempoolRule : MempoolRule
    {
        public MainNetRequireWitnessMempoolRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            IConsensusRuleEngine consensusRules,
            ILoggerFactory loggerFactory) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
        }

        public override void CheckTransaction(MempoolValidationContext context)
        {
            if (!context.Transaction.HasWitness)
            {
                this.logger.LogTrace($"(-)[FAIL_{nameof(MainNetRequireWitnessMempoolRule)}]".ToUpperInvariant());
                MainNetConsensusErrors.MissingWitness.Throw();
            }
        }
    }
}