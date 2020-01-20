using UnnamedCoin.Bitcoin.Consensus.Rules;
using UnnamedCoin.Bitcoin.Features.Consensus.CoinViews;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Consensus.Rules
{
    /// <summary>
    ///     Rules that provide easy access to the <see cref="CoinView" /> which is the store for a PoW system.
    /// </summary>
    public abstract class UtxoStoreConsensusRule : FullValidationConsensusRule
    {
        protected CoinviewHelper coinviewHelper;

        /// <summary>Allow access to the POS parent.</summary>
        protected PowConsensusRuleEngine PowParent;

        /// <inheritdoc />
        public override void Initialize()
        {
            this.PowParent = this.Parent as PowConsensusRuleEngine;
            Guard.NotNull(this.PowParent, nameof(this.PowParent));

            this.coinviewHelper = new CoinviewHelper();
        }
    }
}