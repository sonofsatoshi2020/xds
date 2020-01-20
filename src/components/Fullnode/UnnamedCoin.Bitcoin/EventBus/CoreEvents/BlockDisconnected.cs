using UnnamedCoin.Bitcoin.Primitives;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents
{
    /// <summary>
    ///     Event that is executed when a block is disconnected from a consensus chain.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class BlockDisconnected : EventBase
    {
        public BlockDisconnected(ChainedHeaderBlock disconnectedBlock)
        {
            this.DisconnectedBlock = disconnectedBlock;
        }

        public ChainedHeaderBlock DisconnectedBlock { get; }
    }
}