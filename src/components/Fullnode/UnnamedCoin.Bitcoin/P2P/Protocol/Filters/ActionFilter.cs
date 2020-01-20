using System;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Filters
{
    public class ActionFilter : INetworkPeerFilter
    {
        readonly Action<IncomingMessage, Action> onIncoming;
        readonly Action<INetworkPeer, Payload, Action> onSending;

        public ActionFilter(Action<IncomingMessage, Action> onIncoming = null,
            Action<INetworkPeer, Payload, Action> onSending = null)
        {
            this.onIncoming = onIncoming ?? ((m, n) => n());
            this.onSending = onSending ?? ((m, p, n) => n());
        }

        public void OnReceivingMessage(IncomingMessage message, Action next)
        {
            this.onIncoming(message, next);
        }

        public void OnSendingMessage(INetworkPeer peer, Payload payload, Action next)
        {
            this.onSending(peer, payload, next);
        }
    }
}