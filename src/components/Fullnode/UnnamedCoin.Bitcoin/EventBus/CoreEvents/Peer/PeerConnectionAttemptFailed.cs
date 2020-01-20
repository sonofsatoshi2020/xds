using System.Net;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents.Peer
{
    /// <summary>
    ///     Event that is published whenever a peer connection attempt failed.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class PeerConnectionAttemptFailed : PeerEventBase
    {
        public PeerConnectionAttemptFailed(bool inbound, IPEndPoint peerEndPoint, string reason) : base(peerEndPoint)
        {
            this.Inbound = inbound;
            this.Reason = reason;
        }

        public bool Inbound { get; }

        public string Reason { get; }
    }
}