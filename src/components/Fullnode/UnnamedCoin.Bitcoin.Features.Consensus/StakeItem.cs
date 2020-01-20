using NBitcoin;

namespace UnnamedCoin.Bitcoin.Features.Consensus
{
    public class StakeItem
    {
        public uint256 BlockId;

        public BlockStake BlockStake;

        public long Height;

        public bool InStore;
    }
}