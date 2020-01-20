using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Protocol;
using UnnamedCoin.Bitcoin.AsyncWork;
using UnnamedCoin.Bitcoin.Configuration.Settings;
using UnnamedCoin.Bitcoin.EventBus.CoreEvents.Peer;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.Signals;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.Extensions;

namespace UnnamedCoin.Bitcoin.P2P.Peer
{
    public class NetworkPeerServer : IDisposable
    {
        /// <summary>Configuration related to incoming and outgoing connections.</summary>
        readonly ConnectionManagerSettings connectionManagerSettings;

        /// <summary>Provider of IBD state.</summary>
        readonly IInitialBlockDownloadState initialBlockDownloadState;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Maintains a list of connected peers and ensures their proper disposal.</summary>
        readonly NetworkPeerDisposer networkPeerDisposer;

        /// <summary>Factory for creating P2P network peers.</summary>
        readonly INetworkPeerFactory networkPeerFactory;

        /// <summary>Cancellation that is triggered on shutdown to stop all pending operations.</summary>
        readonly CancellationTokenSource serverCancel;

        /// <summary>Used to publish application events.</summary>
        readonly ISignals signals;

        /// <summary>TCP server listener accepting inbound connections.</summary>
        readonly TcpListener tcpListener;

        /// <summary>Task accepting new clients in a loop.</summary>
        Task acceptTask;

        /// <summary>
        ///     Initializes instance of a network peer server.
        /// </summary>
        /// <param name="network">Specification of the network the node runs on - regtest/testnet/mainnet.</param>
        /// <param name="localEndPoint">IP address and port to listen on.</param>
        /// <param name="externalEndPoint">IP address and port that the server is reachable from the Internet on.</param>
        /// <param name="version">Version of the network protocol that the server should run.</param>
        /// <param name="loggerFactory">Factory for creating loggers.</param>
        /// <param name="networkPeerFactory">Factory for creating P2P network peers.</param>
        /// <param name="initialBlockDownloadState">Provider of IBD state.</param>
        /// <param name="connectionManagerSettings">Configuration related to incoming and outgoing connections.</param>
        public NetworkPeerServer(Network network,
            IPEndPoint localEndPoint,
            IPEndPoint externalEndPoint,
            ProtocolVersion version,
            ILoggerFactory loggerFactory,
            INetworkPeerFactory networkPeerFactory,
            IInitialBlockDownloadState initialBlockDownloadState,
            ConnectionManagerSettings connectionManagerSettings,
            IAsyncProvider asyncProvider)
        {
            this.logger = loggerFactory.CreateLogger(GetType().FullName, $"[{localEndPoint}] ");
            this.signals = asyncProvider.Signals;
            this.networkPeerFactory = networkPeerFactory;
            this.networkPeerDisposer = new NetworkPeerDisposer(loggerFactory, asyncProvider);
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.connectionManagerSettings = connectionManagerSettings;

            this.InboundNetworkPeerConnectionParameters = new NetworkPeerConnectionParameters();

            this.LocalEndpoint = Utils.EnsureIPv6(localEndPoint);
            this.ExternalEndpoint = Utils.EnsureIPv6(externalEndPoint);

            this.Network = network;
            this.Version = version;

            this.serverCancel = new CancellationTokenSource();

            this.tcpListener = new TcpListener(this.LocalEndpoint);
            this.tcpListener.Server.LingerState = new LingerOption(true, 0);
            this.tcpListener.Server.NoDelay = true;
            this.tcpListener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);

            this.acceptTask = Task.CompletedTask;

            this.logger.LogDebug("Network peer server ready to listen on '{0}'.", this.LocalEndpoint);
        }

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        public Network Network { get; }

        /// <summary>Version of the protocol that the server is running.</summary>
        public ProtocolVersion Version { get; }

        /// <summary>The parameters that will be cloned and applied for each peer connecting to <see cref="NetworkPeerServer" />.</summary>
        public NetworkPeerConnectionParameters InboundNetworkPeerConnectionParameters { get; set; }

        /// <summary>IP address and port, on which the server listens to incoming connections.</summary>
        public IPEndPoint LocalEndpoint { get; }

        /// <summary>IP address and port of the external network interface that is accessible from the Internet.</summary>
        public IPEndPoint ExternalEndpoint { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            this.serverCancel.Cancel();

            this.logger.LogDebug("Stopping TCP listener.");
            this.tcpListener.Stop();

            this.logger.LogDebug("Waiting for accepting task to complete.");
            this.acceptTask.Wait();

            if (this.networkPeerDisposer.ConnectedPeersCount > 0)
                this.logger.LogInformation("Waiting for {0} connected clients to finish.",
                    this.networkPeerDisposer.ConnectedPeersCount);

            this.networkPeerDisposer.Dispose();
        }

        /// <summary>
        ///     Starts listening on the server's initialized endpoint.
        /// </summary>
        public void Listen()
        {
            try
            {
                this.tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                this.tcpListener.Start();
                this.acceptTask = AcceptClientsAsync();
            }
            catch (Exception e)
            {
                this.logger.LogDebug("Exception occurred: {0}", e.ToString());
                throw;
            }
        }

        /// <summary>
        ///     Implements loop accepting connections from newly connected clients.
        /// </summary>
        async Task AcceptClientsAsync()
        {
            this.logger.LogDebug("Accepting incoming connections.");

            try
            {
                while (!this.serverCancel.IsCancellationRequested)
                {
                    var tcpClient = await this.tcpListener.AcceptTcpClientAsync()
                        .WithCancellationAsync(this.serverCancel.Token).ConfigureAwait(false);

                    (var successful, var reason) = AllowClientConnection(tcpClient);
                    if (!successful)
                    {
                        this.signals.Publish(new PeerConnectionAttemptFailed(true,
                            (IPEndPoint) tcpClient.Client.RemoteEndPoint, reason));
                        this.logger.LogDebug("Connection from client '{0}' was rejected and will be closed.",
                            tcpClient.Client.RemoteEndPoint);
                        tcpClient.Close();
                        continue;
                    }

                    this.logger.LogDebug("Connection accepted from client '{0}'.", tcpClient.Client.RemoteEndPoint);

                    var connectedPeer = this.networkPeerFactory.CreateNetworkPeer(tcpClient,
                        CreateNetworkPeerConnectionParameters(), this.networkPeerDisposer);
                    this.signals.Publish(new PeerConnected(connectedPeer.Inbound, connectedPeer.PeerEndPoint));
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.LogDebug("Shutdown detected, stop accepting connections.");
            }
            catch (Exception e)
            {
                this.logger.LogDebug("Exception occurred: {0}", e.ToString());
            }
        }

        /// <summary>
        ///     Initializes connection parameters using the server's initialized values.
        /// </summary>
        /// <returns>Initialized connection parameters.</returns>
        NetworkPeerConnectionParameters CreateNetworkPeerConnectionParameters()
        {
            var myExternal = this.ExternalEndpoint;
            var param2 = this.InboundNetworkPeerConnectionParameters.Clone();
            param2.Version = this.Version;
            param2.AddressFrom = myExternal;
            return param2;
        }

        /// <summary>
        ///     Check if the client is allowed to connect based on certain criteria.
        /// </summary>
        /// <returns>When criteria is met returns <c>true</c>, to allow connection.</returns>
        (bool successful, string reason) AllowClientConnection(TcpClient tcpClient)
        {
            if (this.networkPeerDisposer.ConnectedInboundPeersCount >=
                this.connectionManagerSettings.MaxInboundConnections)
            {
                this.logger.LogTrace("(-)[MAX_CONNECTION_THRESHOLD_REACHED]:false");
                return (false, "Inbound Refused: Max Connection Threshold Reached.");
            }

            if (!this.initialBlockDownloadState.IsInitialBlockDownload())
            {
                this.logger.LogTrace("(-)[IBD_COMPLETE_ALLOW_CONNECTION]:true");
                return (true, "Inbound Accepted: IBD Complete.");
            }

            var clientLocalEndPoint = tcpClient.Client.LocalEndPoint as IPEndPoint;
            var clientRemoteEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;

            var endpointCanBeWhiteListed = this.connectionManagerSettings.Bind.Where(x => x.Whitelisted)
                .Any(x => x.Endpoint.Contains(clientLocalEndPoint));

            if (endpointCanBeWhiteListed)
            {
                this.logger.LogTrace("(-)[ENDPOINT_WHITELISTED_ALLOW_CONNECTION]:true");
                return (true, "Inbound Accepted: Whitelisted endpoint connected during IBD.");
            }

            this.logger.LogDebug("Node '{0}' is not whitelisted via endpoint '{1}' during initial block download.",
                clientRemoteEndPoint, clientLocalEndPoint);

            return (false, "Inbound Refused: Non Whitelisted endpoint connected during IBD.");
        }
    }
}