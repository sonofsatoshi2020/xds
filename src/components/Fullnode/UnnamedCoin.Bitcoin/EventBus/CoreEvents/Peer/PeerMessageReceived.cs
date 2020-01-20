using System.Net;
using UnnamedCoin.Bitcoin.P2P.Protocol;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents.Peer
{
    /// <summary>
    ///     A peer message has been received and parsed
    /// </summary>
    /// <seealso cref="EventBase" />
    public class PeerMessageReceived : PeerEventBase
    {
        public PeerMessageReceived(IPEndPoint peerEndPoint, Message message, int messageSize) : base(peerEndPoint)
        {
            this.Message = message;
            this.MessageSize = messageSize;
        }

        public Message Message { get; }

        public int MessageSize { get; }
    }
}