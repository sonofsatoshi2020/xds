using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.P2P;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol;
using UnnamedCoin.Bitcoin.P2P.Protocol.Behaviors;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Connection
{
    /// <summary>
    ///     If the light wallet is only connected to nodes behind
    ///     it cannot progress progress to the tip to get the full balance
    ///     this behaviour will make sure place is kept for nodes higher then
    ///     current tip.
    /// </summary>
    public class DropNodesBehaviour : NetworkPeerBehavior
    {
        readonly ChainIndexer chainIndexer;

        readonly IConnectionManager connection;

        readonly decimal dropThreshold;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Logger factory to create loggers.</summary>
        readonly ILoggerFactory loggerFactory;

        public DropNodesBehaviour(ChainIndexer chainIndexer, IConnectionManager connectionManager,
            ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger(GetType().FullName, $"[{GetHashCode():x}] ");
            this.loggerFactory = loggerFactory;

            this.chainIndexer = chainIndexer;
            this.connection = connectionManager;

            // 80% of current max connections, the last 20% will only
            // connect to nodes ahead of the current best chain.
            this.dropThreshold = 0.8M;
        }

        Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            if (message.Message.Payload is VersionPayload version)
            {
                IPeerConnector peerConnector = null;
                if (this.connection.ConnectionSettings.Connect.Any())
                    peerConnector = this.connection.PeerConnectors.First(pc => pc is PeerConnectorConnectNode);
                else
                    peerConnector = this.connection.PeerConnectors.First(pc => pc is PeerConnectorDiscovery);

                // Find how much 20% max nodes.
                var thresholdCount = Math.Round(peerConnector.MaxOutboundConnections * this.dropThreshold,
                    MidpointRounding.ToEven);

                if (thresholdCount < this.connection.ConnectedPeers.Count())
                    if (version.StartHeight < this.chainIndexer.Height)
                        peer.Disconnect($"Node at height = {version.StartHeight} too far behind current height");
            }

            return Task.CompletedTask;
        }


        protected override void AttachCore()
        {
            this.AttachedPeer.MessageReceived.Register(OnMessageReceivedAsync);
        }


        protected override void DetachCore()
        {
            this.AttachedPeer.MessageReceived.Unregister(OnMessageReceivedAsync);
        }


        public override object Clone()
        {
            return new DropNodesBehaviour(this.chainIndexer, this.connection, this.loggerFactory);
        }
    }
}