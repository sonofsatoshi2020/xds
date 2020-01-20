using System;
using System.Net;
using UnnamedCoin.Bitcoin.P2P.Protocol;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents.Peer
{
    /// <summary>
    ///     A peer message failed to be sent.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class PeerMessageSendFailure : PeerEventBase
    {
        public PeerMessageSendFailure(IPEndPoint peerEndPoint, Message message, Exception exception) : base(
            peerEndPoint)
        {
            this.Message = message;
            this.Exception = exception;
        }

        /// <summary>
        ///     The failed message. Can be null if the exception was caused during the Message creation.
        ///     </value>
        public Message Message { get; }

        public Exception Exception { get; }
    }
}