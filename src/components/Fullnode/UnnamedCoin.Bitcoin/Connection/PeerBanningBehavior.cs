using Microsoft.Extensions.Logging;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol.Behaviors;

namespace UnnamedCoin.Bitcoin.Connection
{
    /// <summary>
    ///     A behaviour that will manage the lifetime of peers.
    /// </summary>
    public class PeerBanningBehavior : NetworkPeerBehavior
    {
        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Logger factory to create loggers.</summary>
        readonly ILoggerFactory loggerFactory;

        /// <summary>The node settings.</summary>
        readonly NodeSettings nodeSettings;

        /// <summary>Handle the lifetime of a peer.</summary>
        readonly IPeerBanning peerBanning;

        public PeerBanningBehavior(ILoggerFactory loggerFactory, IPeerBanning peerBanning, NodeSettings nodeSettings)
        {
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.loggerFactory = loggerFactory;
            this.peerBanning = peerBanning;
            this.nodeSettings = nodeSettings;
        }

        /// <inheritdoc />
        protected override void DetachCore()
        {
        }

        /// <inheritdoc />
        public override object Clone()
        {
            return new PeerBanningBehavior(this.loggerFactory, this.peerBanning, this.nodeSettings);
        }

        /// <inheritdoc />
        protected override void AttachCore()
        {
            var peer = this.AttachedPeer;
            var peerBehavior = peer.Behavior<IConnectionManagerBehavior>();
            if (peer.State == NetworkPeerState.Connected && !peerBehavior.Whitelisted)
                if (this.peerBanning.IsBanned(peer.RemoteSocketEndpoint))
                {
                    this.logger.LogDebug("Peer '{0}' was previously banned.", peer.RemoteSocketEndpoint);
                    peer.Disconnect("A banned node tried to connect.");
                    this.logger.LogTrace("(-)[PEER_BANNED]");
                }
        }
    }
}