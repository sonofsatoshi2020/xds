using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Features.Consensus;
using UnnamedCoin.Bitcoin.Features.Consensus.Rules.CommonRules;
using UnnamedCoin.Bitcoin.Utilities;

namespace ChainParams.Rules
{
    public sealed class MainNetPosCoinviewRule : PosCoinviewRule
    {
        /// <inheritdoc />
        public override Money GetProofOfWorkReward(int height)
        {
            int halvings = height / this.consensus.SubsidyHalvingInterval;

            if (halvings >= 64)
                return 0;

            Money subsidy = this.consensus.ProofOfWorkReward;

            subsidy >>= halvings;

            return subsidy;
        }

        /// <inheritdoc />
        public override Money GetProofOfStakeReward(int height)
        {
            int halvings = height / this.consensus.SubsidyHalvingInterval;

            if (halvings >= 64)
                return 0;

            Money subsidy = this.consensus.ProofOfStakeReward;

            subsidy >>= halvings;

            return subsidy;
        }

        protected override Money GetTransactionFee(UnspentOutputSet view, Transaction tx)
        {
            Money fee = base.GetTransactionFee(view, tx);

            if (!tx.IsProtocolTransaction())
            {
                if (fee < this.Parent.Network.AbsoluteMinTxFee)
                {
                    this.Logger.LogTrace($"(-)[FAIL_{nameof(MainNetRequireWitnessRule)}]".ToUpperInvariant());
                    MainNetConsensusErrors.FeeBelowAbsoluteMinTxFee.Throw();
                }
            }


            return fee;
        }

        protected override void CheckInputValidity(Transaction transaction, UnspentOutputs coins)
        {
            return;
        }
    }
}
