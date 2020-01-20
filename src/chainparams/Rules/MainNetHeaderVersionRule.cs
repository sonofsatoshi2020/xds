using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Base.Deployments;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Consensus.Rules;
using UnnamedCoin.Bitcoin.Features.Consensus.Rules.CommonRules;
using UnnamedCoin.Bitcoin.Utilities;

namespace ChainParams.Rules
{
    /// <summary>
    /// Checks if <see cref="MainNet"/> network block's header has a valid block version.
    /// </summary>
    public class MainNetHeaderVersionRule : HeaderVersionRule
    {
        /// <inheritdoc />
        /// <exception cref="ConsensusErrors.BadVersion">Thrown if block's version is outdated or otherwise invalid.</exception>
        public override void Run(RuleContext context)
        {
            Guard.NotNull(context.ValidationContext.ChainedHeaderToValidate, nameof(context.ValidationContext.ChainedHeaderToValidate));

            ChainedHeader chainedHeader = context.ValidationContext.ChainedHeaderToValidate;

            // ODX will always use BIP9 enabled blocks.
            if ((chainedHeader.Header.Version & ThresholdConditionCache.VersionbitsTopMask) != ThresholdConditionCache.VersionbitsTopBits)
            {
                this.Logger.LogTrace("(-)[BAD_VERSION]");
                ConsensusErrors.BadVersion.Throw();
            }
        }
    }
}