using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnnamedCoin.Bitcoin.Consensus.Rules;

namespace ChainParams.Rules
{
    /// <summary>
    /// Checks if all transaction in the block have witness.
    /// </summary>
    public class MainNetRequireWitnessRule : PartialValidationConsensusRule
    {
        public override Task RunAsync(RuleContext context)
        {
            var block = context.ValidationContext.BlockToValidate;

            foreach (var tx in block.Transactions)
            {
                if (!tx.HasWitness)
                {
                    this.Logger.LogTrace($"(-)[FAIL_{nameof(MainNetRequireWitnessRule)}]".ToUpperInvariant());
                    MainNetConsensusErrors.MissingWitness.Throw();
                }
            }

            return Task.CompletedTask;
        }
    }
}