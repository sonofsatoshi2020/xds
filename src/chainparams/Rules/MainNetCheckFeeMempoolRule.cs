using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Interfaces;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Rules;

namespace ChainParams.Rules
{
    /// <summary>
    /// Validates the transaction fee is valid, so that the transaction can be mined eventually.
    /// Checks whether the fee meets minimum fee, free transactions have sufficient priority, and absurdly high fees.
    /// </summary>
    public class MainNetCheckFeeMempoolRule : CheckFeeMempoolRule
    {
        public MainNetCheckFeeMempoolRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
        }

        public override void CheckTransaction(MempoolValidationContext context)
        {
            Debug.Assert(this.network.AbsoluteMinTxFee.HasValue);

            long consensusRejectFee = this.network.AbsoluteMinTxFee.Value;
            if (context.Fees < consensusRejectFee)
            {
                this.logger.LogTrace("(-)[FAIL_ABSOLUTE_MIN_TX_FEE_NOT_MET]");
                context.State.Fail(MempoolErrors.MinFeeNotMet, $" {context.Fees} < {consensusRejectFee}").Throw();
            }

            // calling the base class here allows for customized behavior above the AbsoluteMinTxFee threshold.
            base.CheckTransaction(context);
        }
    }
}