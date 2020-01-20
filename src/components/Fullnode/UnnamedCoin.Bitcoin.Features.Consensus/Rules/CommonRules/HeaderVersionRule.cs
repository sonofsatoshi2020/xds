using NBitcoin;
using UnnamedCoin.Bitcoin.Base.Deployments;
using UnnamedCoin.Bitcoin.Consensus.Rules;

namespace UnnamedCoin.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <summary>A base skeleton class that is implemented by networks to define and verify the version of blocks.</summary>
    public abstract class HeaderVersionRule : HeaderValidationConsensusRule
    {
        /// <summary>
        ///     Computes what the block version of a newly created block should be, given a previous header and the
        ///     current set of BIP9 deployments defined in the consensus.
        /// </summary>
        /// <param name="prevChainedHeader">The header of the previous block in the chain.</param>
        /// <remarks>
        ///     This method is currently used during block creation only. Different nodes may not implement
        ///     BIP9, or may disagree about what the current valid set of deployments are. It is therefore not strictly
        ///     possible to validate a block version number in anything more than general terms.
        /// </remarks>
        public int ComputeBlockVersion(ChainedHeader prevChainedHeader)
        {
            var version = ThresholdConditionCache.VersionbitsTopBits;
            var thresholdConditionCache = new ThresholdConditionCache(this.Parent.Network.Consensus);

            for (var deployment = 0; deployment < thresholdConditionCache.ArraySize; deployment++)
            {
                var state = thresholdConditionCache.GetState(prevChainedHeader, deployment);
                if (state == ThresholdState.LockedIn || state == ThresholdState.Started)
                    version |= thresholdConditionCache.Mask(deployment);
            }

            return (int) version;
        }
    }
}