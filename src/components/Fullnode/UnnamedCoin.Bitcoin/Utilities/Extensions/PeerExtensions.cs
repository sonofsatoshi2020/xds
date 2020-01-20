using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.P2P.Peer;

namespace UnnamedCoin.Bitcoin.Utilities.Extensions
{
    public static class PeerExtensions
    {
        public static bool IsWhitelisted(this INetworkPeer peer)
        {
            return peer.Behavior<IConnectionManagerBehavior>()?.Whitelisted == true;
        }
    }
}