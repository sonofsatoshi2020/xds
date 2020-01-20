using System.Collections.Generic;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.Mining
{
    public sealed class BlockTemplate
    {
        public BlockTemplate(Network network)
        {
            this.Block = network.CreateBlock();
        }

        public Block Block { get; set; }

        public Money TotalFee { get; set; }

        public Dictionary<uint256, Money> FeeDetails { get; set; }
    }
}