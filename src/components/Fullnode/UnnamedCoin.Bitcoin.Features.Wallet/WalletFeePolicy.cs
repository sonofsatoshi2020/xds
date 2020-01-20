using System;
using NBitcoin;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Features.Wallet.Interfaces;

namespace UnnamedCoin.Bitcoin.Features.Wallet
{
    public class WalletFeePolicy : IWalletFeePolicy
    {
        /// <summary>
        ///     If fee estimation does not have enough data to provide estimates, use this fee instead.
        ///     Has no effect if not using fee estimation
        ///     Override with -fallbackfee
        /// </summary>
        readonly FeeRate fallbackFee;

        /// <summary>Maximum transaction fee.</summary>
        readonly Money maxTxFee;

        /// <summary>
        ///     Min Relay Tx Fee
        /// </summary>
        readonly FeeRate minRelayTxFee;

        /// <summary>
        ///     Fees smaller than this (in satoshi) are considered zero fee (for transaction creation)
        ///     Override with -mintxfee
        /// </summary>
        readonly FeeRate minTxFee;

        /// <summary>
        ///     Transaction fee set by the user
        /// </summary>
        readonly FeeRate payTxFee;

        /// <summary>
        ///     Constructs a wallet fee policy.
        /// </summary>
        /// <param name="nodeSettings">Settings for the the node.</param>
        public WalletFeePolicy(NodeSettings nodeSettings)
        {
            this.minTxFee = nodeSettings.MinTxFeeRate;
            this.fallbackFee = nodeSettings.FallbackTxFeeRate;
            this.payTxFee = new FeeRate(0);
            this.maxTxFee = new Money(0.1M, MoneyUnit.BTC);
            this.minRelayTxFee = nodeSettings.MinRelayTxFeeRate;
        }

        /// <inheritdoc />
        public void Start()
        {
        }

        /// <inheritdoc />
        public void Stop()
        {
        }

        /// <inheritdoc />
        public Money GetRequiredFee(int txBytes)
        {
            return Math.Max(this.minTxFee.GetFee(txBytes), this.minRelayTxFee.GetFee(txBytes));
        }

        /// <inheritdoc />
        public Money GetMinimumFee(int txBytes, int confirmTarget)
        {
            // payTxFee is the user-set global for desired feerate
            return GetMinimumFee(txBytes, confirmTarget, this.payTxFee.GetFee(txBytes));
        }

        /// <inheritdoc />
        public Money GetMinimumFee(int txBytes, int confirmTarget, Money targetFee)
        {
            var nFeeNeeded = targetFee;
            // User didn't set: use -txconfirmtarget to estimate...
            if (nFeeNeeded == 0)
            {
                var estimateFoundTarget = confirmTarget;

                // TODO: the fee estimation is not ready for release for now use the fall back fee
                //nFeeNeeded = this.blockPolicyEstimator.EstimateSmartFee(confirmTarget, this.mempool, out estimateFoundTarget).GetFee(txBytes);
                // ... unless we don't have enough mempool data for estimatefee, then use fallbackFee
                if (nFeeNeeded == 0)
                    nFeeNeeded = this.fallbackFee.GetFee(txBytes);
            }

            // prevent user from paying a fee below minRelayTxFee or minTxFee
            nFeeNeeded = Math.Max(nFeeNeeded, GetRequiredFee(txBytes));
            // But always obey the maximum
            if (nFeeNeeded > this.maxTxFee)
                nFeeNeeded = this.maxTxFee;
            return nFeeNeeded;
        }

        /// <inheritdoc />
        public FeeRate GetFeeRate(int confirmTarget)
        {
            //this.blockPolicyEstimator.EstimateSmartFee(confirmTarget, this.mempool, out estimateFoundTarget).GetFee(txBytes);
            return this.fallbackFee;
        }
    }
}