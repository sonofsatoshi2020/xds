using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol.Behaviors;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.Extensions;

namespace UnnamedCoin.Bitcoin.Connection
{
    public interface IConnectionManagerBehavior : INetworkPeerBehavior
    {
        bool Whitelisted { get; }

        bool OneTry { get; }
    }

    public class ConnectionManagerBehavior : NetworkPeerBehavior, IConnectionManagerBehavior
    {
        readonly IConnectionManager connectionManager;

        /// <summary>
        ///     Instance logger that we use for logging of INFO level messages that are visible on the console.
        ///     <para>Unlike <see cref="logger" />, this one is created without prefix for the nicer console output.</para>
        /// </summary>
        readonly ILogger infoLogger;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Logger factory to create loggers.</summary>
        readonly ILoggerFactory loggerFactory;

        public ConnectionManagerBehavior(IConnectionManager connectionManager, ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger(GetType().FullName, $"[{GetHashCode():x}] ");
            this.infoLogger = loggerFactory.CreateLogger(GetType().FullName);
            this.loggerFactory = loggerFactory;

            this.connectionManager = connectionManager;
        }

        public bool Whitelisted { get; internal set; }

        public bool OneTry { get; internal set; }


        public override object Clone()
        {
            return new ConnectionManagerBehavior(this.connectionManager, this.loggerFactory)
            {
                OneTry = this.OneTry,
                Whitelisted = this.Whitelisted
            };
        }


        protected override void AttachCore()
        {
            this.AttachedPeer.StateChanged.Register(OnStateChangedAsync);

            var peer = this.AttachedPeer;
            if (peer != null)
                if (this.connectionManager.ConnectionSettings.Whitelist.Exists(e => e.MatchIpOnly(peer.PeerEndPoint)))
                    this.Whitelisted = true;
        }

        async Task OnStateChangedAsync(INetworkPeer peer, NetworkPeerState oldState)
        {
            try
            {
                if (peer.State == NetworkPeerState.HandShaked)
                {
                    this.connectionManager.AddConnectedPeer(peer);
                    this.infoLogger.LogInformation("Peer '{0}' connected ({1}), agent '{2}', height {3}",
                        peer.RemoteSocketEndpoint, peer.Inbound ? "inbound" : "outbound", peer.PeerVersion.UserAgent,
                        peer.PeerVersion.StartHeight);

                    peer.SendMessage(new SendHeadersPayload());
                }

                if (peer.State == NetworkPeerState.Failed || peer.State == NetworkPeerState.Offline)
                {
                    this.infoLogger.LogInformation("Peer '{0}' ({1}) offline, reason: '{2}.{3}'",
                        peer.RemoteSocketEndpoint, peer.Inbound ? "inbound" : "outbound",
                        peer.DisconnectReason?.Reason ?? "unknown",
                        peer.DisconnectReason?.Exception?.Message != null
                            ? string.Format(" {0}.", peer.DisconnectReason.Exception.Message)
                            : string.Empty);

                    this.connectionManager.RemoveConnectedPeer(peer, "Peer offline");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }


        protected override void DetachCore()
        {
            this.AttachedPeer.StateChanged.Unregister(OnStateChangedAsync);

            if (this.AttachedPeer.Connection != null)
                this.connectionManager.PeerDisconnected(this.AttachedPeer.Connection.Id);
        }
    }
}