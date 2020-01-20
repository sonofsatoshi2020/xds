using System.Linq;
using UnnamedCoin.Bitcoin.P2P;
using UnnamedCoin.Bitcoin.P2P.Peer;

namespace UnnamedCoin.Bitcoin.Utilities.Extensions
{
    public static class NodeConnectionParameterExtensions
    {
        public static PeerAddressManagerBehaviour PeerAddressManagerBehaviour(
            this NetworkPeerConnectionParameters parameters)
        {
            return parameters.TemplateBehaviors.OfType<PeerAddressManagerBehaviour>().FirstOrDefault();
        }
    }
}