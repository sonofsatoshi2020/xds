using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Interfaces;

namespace ChainParams.Rules
{
    /// <summary>
    /// Checks if transactions match the white-listing criteria. This rule and <see cref="MainNetOutputNotWhitelistedRule"/> must correspond.
    /// </summary>
    public class MainNetOutputNotWhitelistedMempoolRule : MempoolRule
    {
        public MainNetOutputNotWhitelistedMempoolRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            IConsensusRuleEngine consensusRules,
            ILoggerFactory loggerFactory) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
        }

        public override void CheckTransaction(MempoolValidationContext context)
        {
            if (context.Transaction.IsCoinStake || (context.Transaction.IsCoinBase && context.Transaction.Outputs[0].IsEmpty)) // also check the coinbase tx in PoW blocks
                return;

            foreach (var output in context.Transaction.Outputs)
            {
                if (MainNetOutputNotWhitelistedRule.IsOutputWhitelisted(output))
                    continue;

                this.logger.LogTrace($"(-)[FAIL_{nameof(MainNetOutputNotWhitelistedMempoolRule)}]".ToUpperInvariant());
                context.State.Fail(new MempoolError(MainNetConsensusErrors.OutputNotWhitelisted)).Throw();
            }
        }
    }
}
