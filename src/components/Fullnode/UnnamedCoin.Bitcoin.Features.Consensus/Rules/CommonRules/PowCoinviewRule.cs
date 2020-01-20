﻿using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Consensus.Rules;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <inheritdoc />
    public sealed class PowCoinviewRule : CoinViewRule
    {
        /// <summary>Consensus parameters.</summary>
        IConsensus consensus;

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            this.consensus = this.Parent.Network.Consensus;
        }

        /// <inheritdoc />
        public override void CheckBlockReward(RuleContext context, Money fees, int height, Block block)
        {
            var blockReward = fees + GetProofOfWorkReward(height);
            if (block.Transactions[0].TotalOut > blockReward)
            {
                this.Logger.LogTrace("(-)[BAD_COINBASE_AMOUNT]");
                ConsensusErrors.BadCoinbaseAmount.Throw();
            }
        }

        /// <inheritdoc />
        public override Money GetProofOfWorkReward(int height)
        {
            if (IsPremine(height))
                return this.consensus.PremineReward;

            if (this.consensus.ProofOfWorkReward == 0)
                return 0;

            var halvings = height / this.consensus.SubsidyHalvingInterval;

            // Force block reward to zero when right shift is undefined.
            if (halvings >= 64)
                return 0;

            var subsidy = this.consensus.ProofOfWorkReward;
            // Subsidy is cut in half every 210,000 blocks which will occur approximately every 4 years.
            subsidy >>= halvings;

            return subsidy;
        }

        protected override Money GetTransactionFee(UnspentOutputSet view, Transaction tx)
        {
            return view.GetValueIn(tx) - tx.TotalOut;
        }

        /// <inheritdoc />
        protected override bool IsTxFinal(Transaction transaction, RuleContext context)
        {
            if (transaction.IsCoinBase)
                return true;

            var index = context.ValidationContext.ChainedHeaderToValidate;

            var view = (context as UtxoRuleContext).UnspentOutputSet;

            var prevheights = new int[transaction.Inputs.Count];
            // Check that transaction is BIP68 final.
            // BIP68 lock checks (as opposed to nLockTime checks) must
            // be in ConnectBlock because they require the UTXO set.
            for (var i = 0; i < transaction.Inputs.Count; i++)
                prevheights[i] = (int) view.AccessCoins(transaction.Inputs[i].PrevOut.Hash).Height;

            return transaction.CheckSequenceLocks(prevheights, index, context.Flags.LockTimeFlags);
        }

        /// <inheritdoc />
        public override void CheckMaturity(UnspentOutputs coins, int spendHeight)
        {
            CheckCoinbaseMaturity(coins, spendHeight);
        }

        /// <inheritdoc />
        public override void UpdateCoinView(RuleContext context, Transaction transaction)
        {
            UpdateUTXOSet(context, transaction);
        }

        /// <inheritdoc />
        public override Task RunAsync(RuleContext context)
        {
            return base.RunAsync(context);
        }
    }
}