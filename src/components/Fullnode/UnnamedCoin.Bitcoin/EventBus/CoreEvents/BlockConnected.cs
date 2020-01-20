using UnnamedCoin.Bitcoin.Primitives;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents
{
    /// <summary>
    ///     Event that is executed when a block is connected to a consensus chain.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class BlockConnected : EventBase
    {
        public BlockConnected(ChainedHeaderBlock connectedBlock)
        {
            this.ConnectedBlock = connectedBlock;
        }

        public ChainedHeaderBlock ConnectedBlock { get; }
    }
}