using System.Net;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents.Peer
{
    /// <summary>
    ///     Event that is published whenever a peer connects to the node.
    ///     This happens prior to any Payload they have to exchange.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class PeerConnected : PeerEventBase
    {
        public PeerConnected(bool inbound, IPEndPoint peerEndPoint) : base(peerEndPoint)
        {
            this.Inbound = inbound;
        }

        public bool Inbound { get; }
    }
}