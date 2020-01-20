using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.AsyncWork;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Configuration.Settings;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.Extensions;

namespace UnnamedCoin.Bitcoin.P2P
{
    /// <summary>
    ///     The connector used to connect to peers specified with the -addnode argument
    /// </summary>
    public sealed class PeerConnectorAddNode : PeerConnector
    {
        readonly ILogger logger;

        public PeerConnectorAddNode(
            IAsyncProvider asyncProvider,
            IDateTimeProvider dateTimeProvider,
            ILoggerFactory loggerFactory,
            Network network,
            INetworkPeerFactory networkPeerFactory,
            INodeLifetime nodeLifetime,
            NodeSettings nodeSettings,
            ConnectionManagerSettings connectionSettings,
            IPeerAddressManager peerAddressManager,
            ISelfEndpointTracker selfEndpointTracker) :
            base(asyncProvider, dateTimeProvider, loggerFactory, network, networkPeerFactory, nodeLifetime,
                nodeSettings, connectionSettings, peerAddressManager, selfEndpointTracker)
        {
            this.logger = loggerFactory.CreateLogger(GetType().FullName);

            this.Requirements.RequiredServices = NetworkPeerServices.Nothing;
        }

        /// <summary>This connector is always started.</summary>
        public override bool CanStartConnect => true;

        /// <inheritdoc />
        protected override void OnInitialize()
        {
            var addNodes = this.ConnectionSettings.RetrieveAddNodes();

            this.MaxOutboundConnections = addNodes.Count;

            // Add the endpoints from the -addnode arg to the address manager.
            foreach (var ipEndpoint in addNodes)
                this.PeerAddressManager.AddPeer(ipEndpoint.MapToIpv6(), IPAddress.Loopback);
        }

        /// <inheritdoc />
        protected override void OnStartConnect()
        {
            this.CurrentParameters.PeerAddressManagerBehaviour().Mode =
                PeerAddressManagerBehaviourMode.AdvertiseDiscover;
        }

        /// <inheritdoc />
        protected override TimeSpan CalculateConnectionInterval()
        {
            return TimeSpans.Second;
        }

        /// <summary>
        ///     Only connect to nodes as specified in the -addnode arg.
        /// </summary>
        public override async Task OnConnectAsync()
        {
            var addNodes = this.ConnectionSettings.RetrieveAddNodes();

            await addNodes.ForEachAsync(this.ConnectionSettings.MaxOutboundConnections,
                this.NodeLifetime.ApplicationStopping,
                async (ipEndpoint, cancellation) =>
                {
                    if (this.NodeLifetime.ApplicationStopping.IsCancellationRequested)
                        return;

                    var peerAddress = this.PeerAddressManager.FindPeer(ipEndpoint);
                    if (peerAddress != null)
                    {
                        this.logger.LogDebug("Attempting connection to {0}.", peerAddress.Endpoint);

                        await ConnectAsync(peerAddress).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
        }
    }
}