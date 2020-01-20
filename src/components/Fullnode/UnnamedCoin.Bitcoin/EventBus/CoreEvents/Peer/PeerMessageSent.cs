using System.Net;
using UnnamedCoin.Bitcoin.P2P.Protocol;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents.Peer
{
    /// <summary>
    ///     A peer message has been sent successfully.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class PeerMessageSent : PeerEventBase
    {
        public PeerMessageSent(IPEndPoint peerEndPoint, Message message, int size) : base(peerEndPoint)
        {
            this.Message = message;
            this.Size = size;
        }

        /// <summary>
        ///     Gets the sent message.
        /// </summary>
        public Message Message { get; }

        /// <summary>
        ///     Gets the raw size of the message, in bytes.
        /// </summary>
        public int Size { get; }
    }
}