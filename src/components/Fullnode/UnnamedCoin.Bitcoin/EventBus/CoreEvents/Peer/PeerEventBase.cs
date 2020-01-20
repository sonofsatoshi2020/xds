using System.Net;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents.Peer
{
    /// <summary>
    ///     Base peer event.
    /// </summary>
    /// <seealso cref="EventBase" />
    public abstract class PeerEventBase : EventBase
    {
        public PeerEventBase(IPEndPoint peerEndPoint)
        {
            this.PeerEndPoint = peerEndPoint;
        }

        /// <summary>
        ///     Gets the peer end point.
        /// </summary>
        /// <value>
        ///     The peer end point.
        /// </value>
        public IPEndPoint PeerEndPoint { get; }
    }
}