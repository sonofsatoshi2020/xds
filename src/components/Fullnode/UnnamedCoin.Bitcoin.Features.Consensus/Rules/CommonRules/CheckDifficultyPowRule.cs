using Microsoft.Extensions.Logging;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Consensus.Rules;

namespace UnnamedCoin.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <summary>Calculate the difficulty for a POW network and check that it is correct.</summary>
    public class CheckDifficultyPowRule : HeaderValidationConsensusRule
    {
        /// <inheritdoc />
        /// <exception cref="ConsensusErrors.HighHash"> Thrown if block doesn't have a valid PoS header.</exception>
        public override void Run(RuleContext context)
        {
            if (!context.ValidationContext.ChainedHeaderToValidate.Header.CheckProofOfWork())
                ConsensusErrors.HighHash.Throw();

            var nextWorkRequired =
                context.ValidationContext.ChainedHeaderToValidate.GetWorkRequired(this.Parent.Network.Consensus);

            var header = context.ValidationContext.ChainedHeaderToValidate.Header;

            // Check proof of work.
            if (header.Bits != nextWorkRequired)
            {
                this.Logger.LogTrace("(-)[BAD_DIFF_BITS]");
                ConsensusErrors.BadDiffBits.Throw();
            }
        }
    }
}