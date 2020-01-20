using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Consensus.Rules;

namespace UnnamedCoin.Bitcoin.Features.Consensus.Rules.CommonRules
{
    public class CheckSigOpsRule : PartialValidationConsensusRule
    {
        /// <inheritdoc />
        /// <exception cref="ConsensusErrors.BadBlockSigOps">The block contains more signature check operations than allowed.</exception>
        public override Task RunAsync(RuleContext context)
        {
            if (context.SkipValidation)
                return Task.CompletedTask;

            var block = context.ValidationContext.BlockToValidate;
            var options = this.Parent.Network.Consensus.Options;

            long nSigOps = 0;
            foreach (var tx in block.Transactions)
                nSigOps += GetLegacySigOpCount(tx);

            if (nSigOps * options.WitnessScaleFactor > options.MaxBlockSigopsCost)
            {
                this.Logger.LogTrace("(-)[BAD_BLOCK_SIGOPS]");
                ConsensusErrors.BadBlockSigOps.Throw();
            }

            return Task.CompletedTask;
        }

        long GetLegacySigOpCount(Transaction tx)
        {
            long nSigOps = 0;
            foreach (var txin in tx.Inputs)
                nSigOps += txin.ScriptSig.GetSigOpCount(false);

            foreach (var txout in tx.Outputs)
                nSigOps += txout.ScriptPubKey.GetSigOpCount(false);

            return nSigOps;
        }
    }
}