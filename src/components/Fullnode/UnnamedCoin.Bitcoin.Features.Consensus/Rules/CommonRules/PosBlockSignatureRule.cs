using System;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Consensus.Rules;

namespace UnnamedCoin.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <summary>
    ///     A rule that will validate the signature of a PoS block.
    /// </summary>
    public class PosBlockSignatureRule : IntegrityValidationConsensusRule
    {
        /// <inheritdoc />
        /// <exception cref="ConsensusErrors.BadBlockSignature">The block signature is invalid.</exception>
        public override void Run(RuleContext context)
        {
            var block = context.ValidationContext.BlockToValidate;

            if (!(block is PosBlock posBlock))
            {
                this.Logger.LogTrace("(-)[INVALID_CAST]");
                throw new InvalidCastException();
            }

            // Check proof-of-stake block signature.
            if (!CheckBlockSignature(posBlock))
            {
                this.Logger.LogTrace("(-)[BAD_SIGNATURE]");
                ConsensusErrors.BadBlockSignature.Throw();
            }
        }

        /// <summary>
        ///     Checks if block signature is valid.
        /// </summary>
        /// <param name="block">The block.</param>
        /// <returns><c>true</c> if the signature is valid, <c>false</c> otherwise.</returns>
        bool CheckBlockSignature(PosBlock block)
        {
            if (BlockStake.IsProofOfWork(block))
            {
                var res = block.BlockSignature.IsEmpty();
                this.Logger.LogTrace("(-)[POW]:{0}", res);
                return res;
            }

            var consensusRules = (PosConsensusRuleEngine) this.Parent;
            return consensusRules.StakeValidator.CheckStakeSignature(block.BlockSignature, block.GetHash(),
                block.Transactions[1]);
        }
    }
}