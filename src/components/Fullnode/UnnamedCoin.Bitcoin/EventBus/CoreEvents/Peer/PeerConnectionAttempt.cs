using System.Net;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents.Peer
{
    /// <summary>
    ///     Event that is published whenever the node tries to connect to a peer.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class PeerConnectionAttempt : PeerEventBase
    {
        public PeerConnectionAttempt(bool inbound, IPEndPoint peerEndPoint) : base(peerEndPoint)
        {
            this.Inbound = inbound;
        }

        public bool Inbound { get; }
    }
}