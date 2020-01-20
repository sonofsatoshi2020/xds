﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Protocol;
using UnnamedCoin.Bitcoin.AsyncWork;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Configuration.Logging;
using UnnamedCoin.Bitcoin.Configuration.Settings;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.P2P;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.Extensions;

namespace UnnamedCoin.Bitcoin.Connection
{
    public sealed class ConnectionManager : IConnectionManager
    {
        /// <summary>The maximum number of entries in an 'inv' protocol message.</summary>
        public const int MaxInventorySize = 50000;

        readonly IAsyncProvider asyncProvider;

        readonly NetworkPeerCollection connectedPeers;

        readonly IAsyncDelegateDequeuer<INetworkPeer> connectedPeersQueue;

        /// <summary>Provider of time functions.</summary>
        readonly IDateTimeProvider dateTimeProvider;

        /// <summary>Traffic statistics from peers that have been disconnected.</summary>
        readonly PerformanceCounter disconnectedPerfCounter;

        readonly List<IPEndPoint> ipRangeFilteringEndpointExclusions;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Logger factory to create loggers.</summary>
        readonly ILoggerFactory loggerFactory;

        /// <summary>Maintains a list of connected peers and ensures their proper disposal.</summary>
        readonly NetworkPeerDisposer networkPeerDisposer;

        /// <summary>Global application life cycle control - triggers when application shuts down.</summary>
        readonly INodeLifetime nodeLifetime;

        /// <summary>Manager class that handles peers and their respective states.</summary>
        readonly IPeerAddressManager peerAddressManager;

        /// <summary>Async loop that discovers new peers to connect to.</summary>
        readonly IPeerDiscovery peerDiscovery;

        /// <summary>Registry of endpoints used to identify this node.</summary>
        readonly ISelfEndpointTracker selfEndpointTracker;

        readonly IVersionProvider versionProvider;

        IConsensusManager consensusManager;

        public ConnectionManager(IDateTimeProvider dateTimeProvider,
            ILoggerFactory loggerFactory,
            Network network,
            INetworkPeerFactory networkPeerFactory,
            NodeSettings nodeSettings,
            INodeLifetime nodeLifetime,
            NetworkPeerConnectionParameters parameters,
            IPeerAddressManager peerAddressManager,
            IEnumerable<IPeerConnector> peerConnectors,
            IPeerDiscovery peerDiscovery,
            ISelfEndpointTracker selfEndpointTracker,
            ConnectionManagerSettings connectionSettings,
            IVersionProvider versionProvider,
            INodeStats nodeStats,
            IAsyncProvider asyncProvider)
        {
            this.connectedPeers = new NetworkPeerCollection();
            this.dateTimeProvider = dateTimeProvider;
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.Network = network;
            this.NetworkPeerFactory = networkPeerFactory;
            this.NodeSettings = nodeSettings;
            this.nodeLifetime = nodeLifetime;
            this.asyncProvider = asyncProvider;
            this.peerAddressManager = peerAddressManager;
            this.PeerConnectors = peerConnectors;
            this.peerDiscovery = peerDiscovery;
            this.ConnectionSettings = connectionSettings;
            this.networkPeerDisposer = new NetworkPeerDisposer(this.loggerFactory, this.asyncProvider);
            this.Servers = new List<NetworkPeerServer>();

            this.Parameters = parameters;
            this.Parameters.ConnectCancellation = this.nodeLifetime.ApplicationStopping;
            this.selfEndpointTracker = selfEndpointTracker;
            this.versionProvider = versionProvider;
            this.ipRangeFilteringEndpointExclusions = new List<IPEndPoint>();
            this.connectedPeersQueue =
                asyncProvider.CreateAndRunAsyncDelegateDequeuer<INetworkPeer>(
                    $"{nameof(ConnectionManager)}-{nameof(this.connectedPeersQueue)}", OnPeerAdded);
            this.disconnectedPerfCounter = new PerformanceCounter();

            this.Parameters.UserAgent =
                $"{this.ConnectionSettings.Agent}:{versionProvider.GetVersion()} ({(int) this.NodeSettings.ProtocolVersion})";

            this.Parameters.Version = this.NodeSettings.ProtocolVersion;

            nodeStats.RegisterStats(AddComponentStats, StatsType.Component, GetType().Name, 1100);
        }

        /// <inheritdoc />
        public Network Network { get; }

        /// <inheritdoc />
        public INetworkPeerFactory NetworkPeerFactory { get; }

        /// <inheritdoc />
        public NodeSettings NodeSettings { get; }

        /// <inheritdoc />
        public ConnectionManagerSettings ConnectionSettings { get; }

        /// <inheritdoc />
        public NetworkPeerConnectionParameters Parameters { get; }

        /// <inheritdoc />
        public IEnumerable<IPeerConnector> PeerConnectors { get; }

        public IReadOnlyNetworkPeerCollection ConnectedPeers => this.connectedPeers;

        /// <inheritdoc />
        public List<NetworkPeerServer> Servers { get; }

        /// <inheritdoc />
        public void Initialize(IConsensusManager consensusManager)
        {
            this.consensusManager = consensusManager;
            AddExternalIpToSelfEndpoints();

            if (this.ConnectionSettings.Listen)
                this.peerDiscovery.DiscoverPeers(this);

            foreach (var peerConnector in this.PeerConnectors)
            {
                peerConnector.Initialize(this);
                peerConnector.StartConnectAsync();
            }

            if (this.ConnectionSettings.Listen)
                StartNodeServer();

            // If external IP address supplied this overrides all.
            if (this.ConnectionSettings.ExternalEndpoint != null)
            {
                if (this.ConnectionSettings.ExternalEndpoint.Address.Equals(IPAddress.Loopback))
                    this.selfEndpointTracker.UpdateAndAssignMyExternalAddress(this.ConnectionSettings.ExternalEndpoint,
                        false);
                else
                    this.selfEndpointTracker.UpdateAndAssignMyExternalAddress(this.ConnectionSettings.ExternalEndpoint,
                        true);
            }
            else
            {
                // If external IP address not supplied take first routable bind address and set score to 10.
                var nodeServerEndpoint = this.ConnectionSettings.Bind
                    ?.FirstOrDefault(x => x.Endpoint.Address.IsRoutable(false))?.Endpoint;
                if (nodeServerEndpoint != null)
                    this.selfEndpointTracker.UpdateAndAssignMyExternalAddress(nodeServerEndpoint, false, 10);
                else
                    this.selfEndpointTracker.UpdateAndAssignMyExternalAddress(
                        new IPEndPoint(IPAddress.Parse("0.0.0.0").MapToIPv6Ex(), this.ConnectionSettings.Port), false);
            }
        }

        /// <inheritdoc />
        public void AddDiscoveredNodesRequirement(NetworkPeerServices services)
        {
            var peerConnector = this.PeerConnectors.FirstOrDefault(pc => pc is PeerConnectorDiscovery);
            if (peerConnector != null && !peerConnector.Requirements.RequiredServices.HasFlag(services))
            {
                peerConnector.Requirements.RequiredServices |= services;
                foreach (var peer in peerConnector.ConnectorPeers)
                {
                    if (peer.Inbound)
                        continue;

                    if (!peer.PeerVersion.Services.HasFlag(services))
                        peer.Disconnect("The peer does not support the required services requirement.");
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.logger.LogInformation("Stopping peer discovery.");
            this.peerDiscovery?.Dispose();

            foreach (var peerConnector in this.PeerConnectors)
                peerConnector.Dispose();

            foreach (var server in this.Servers)
                server.Dispose();

            this.networkPeerDisposer.Dispose();
        }

        /// <inheritdoc />
        public void AddConnectedPeer(INetworkPeer peer)
        {
            this.connectedPeers.Add(peer);
            this.connectedPeersQueue.Enqueue(peer);
        }

        /// <inheritdoc />
        public void RemoveConnectedPeer(INetworkPeer peer, string reason)
        {
            this.connectedPeers.Remove(peer);
            this.disconnectedPerfCounter.Add(peer.Counter);
        }

        /// <inheritdoc />
        public void PeerDisconnected(int networkPeerId)
        {
            this.consensusManager.PeerDisconnected(networkPeerId);
        }

        public INetworkPeer FindNodeByEndpoint(IPEndPoint ipEndpoint)
        {
            return this.connectedPeers.FindByEndpoint(ipEndpoint);
        }

        public INetworkPeer FindNodeById(int peerId)
        {
            return this.connectedPeers.FindById(peerId);
        }

        /// <summary>
        ///     Adds a node to the -addnode collection.
        ///     <para>
        ///         Usually called via RPC.
        ///     </para>
        /// </summary>
        /// <param name="ipEndpoint">The endpoint of the peer to add.</param>
        public void AddNodeAddress(IPEndPoint ipEndpoint, bool excludeFromIpRangeFiltering = false)
        {
            Guard.NotNull(ipEndpoint, nameof(ipEndpoint));

            if (excludeFromIpRangeFiltering && !this.ipRangeFilteringEndpointExclusions.Any(ip => ip.Match(ipEndpoint)))
            {
                this.logger.LogDebug("{0} will be excluded from IP range filtering.", ipEndpoint);
                this.ipRangeFilteringEndpointExclusions.Add(ipEndpoint);
            }

            this.peerAddressManager.AddPeer(ipEndpoint.MapToIpv6(), IPAddress.Loopback);

            if (!this.ConnectionSettings.RetrieveAddNodes().Any(p => p.Match(ipEndpoint)))
            {
                this.ConnectionSettings.AddAddNode(ipEndpoint);
                var addNodeConnector = this.PeerConnectors.FirstOrDefault(pc => pc is PeerConnectorAddNode);

                if (addNodeConnector != null)
                    addNodeConnector.MaxOutboundConnections++;
            }
            else
            {
                this.logger.LogDebug("The endpoint already exists in the add node collection.");
            }
        }

        /// <summary>
        ///     Disconnect a peer.
        ///     <para>
        ///         Usually called via RPC.
        ///     </para>
        /// </summary>
        /// <param name="ipEndpoint">The endpoint of the peer to disconnect.</param>
        public void RemoveNodeAddress(IPEndPoint ipEndpoint)
        {
            var peer = this.connectedPeers.FindByEndpoint(ipEndpoint);

            if (peer != null)
            {
                peer.Disconnect("Requested by user");
                RemoveConnectedPeer(peer, "Requested by user");
            }

            this.peerAddressManager.RemovePeer(ipEndpoint);

            // There appears to be a race condition that causes the endpoint or endpoint's address property to be null when
            // trying to remove it from the connection manager's add node collection.
            if (ipEndpoint == null)
            {
                this.logger.LogTrace("(-)[IPENDPOINT_NULL]");
                throw new ArgumentNullException(nameof(ipEndpoint));
            }

            if (ipEndpoint.Address == null)
            {
                this.logger.LogTrace("(-)[IPENDPOINT_ADDRESS_NULL]");
                throw new ArgumentNullException(nameof(ipEndpoint.Address));
            }

            if (this.ConnectionSettings.RetrieveAddNodes().Any(ip => ip == null))
            {
                this.logger.LogTrace("(-)[ADDNODE_CONTAINS_NULLS]");
                throw new ArgumentNullException("The addnode collection contains null entries.");
            }

            foreach (var endpoint in this.ConnectionSettings.RetrieveAddNodes().Where(a => a.Address == null))
            {
                this.logger.LogTrace("(-)[IPENDPOINT_ADDRESS_NULL]:{0}", endpoint);
                throw new ArgumentNullException("The addnode collection contains endpoints with null addresses.");
            }

            // Create a copy of the nodes to remove. This avoids errors due to both modifying the collection and iterating it.
            var matchingAddNodes = this.ConnectionSettings.RetrieveAddNodes().Where(p => p.Match(ipEndpoint)).ToList();
            foreach (var m in matchingAddNodes)
                this.ConnectionSettings.RemoveAddNode(m);
        }

        public async Task<INetworkPeer> ConnectAsync(IPEndPoint ipEndpoint)
        {
            var existingConnection =
                this.connectedPeers.FirstOrDefault(connectedPeer => connectedPeer.PeerEndPoint.Match(ipEndpoint));

            if (existingConnection != null)
            {
                this.logger.LogDebug("{0} is already connected.");
                return existingConnection;
            }

            var cloneParameters = this.Parameters.Clone();

            var connectionManagerBehavior =
                cloneParameters.TemplateBehaviors.OfType<ConnectionManagerBehavior>().SingleOrDefault();
            if (connectionManagerBehavior != null) connectionManagerBehavior.OneTry = true;

            var peer = await this.NetworkPeerFactory
                .CreateConnectedNetworkPeerAsync(ipEndpoint, cloneParameters, this.networkPeerDisposer)
                .ConfigureAwait(false);

            try
            {
                this.peerAddressManager.PeerAttempted(ipEndpoint, this.dateTimeProvider.GetUtcNow());
                await peer.VersionHandshakeAsync(this.nodeLifetime.ApplicationStopping).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                peer.Disconnect("Connection failed");
                this.logger.LogTrace("(-)[ERROR]");
                throw e;
            }

            return peer;
        }

        /// <summary>
        ///     If -externalip was set on startup, put it in the registry of known selves so
        ///     we can avoid connecting to our own node.
        /// </summary>
        void AddExternalIpToSelfEndpoints()
        {
            if (this.ConnectionSettings.ExternalEndpoint != null)
                this.selfEndpointTracker.Add(this.ConnectionSettings.ExternalEndpoint);
        }

        void StartNodeServer()
        {
            var logs = new StringBuilder();
            logs.AppendLine("Node listening on:");

            foreach (var listen in this.ConnectionSettings.Bind)
            {
                var cloneParameters = this.Parameters.Clone();
                var server = this.NetworkPeerFactory.CreateNetworkPeerServer(listen.Endpoint,
                    this.ConnectionSettings.ExternalEndpoint, this.Parameters.Version);

                this.Servers.Add(server);
                var cmb =
                    cloneParameters.TemplateBehaviors.Single(x => x is IConnectionManagerBehavior) as
                        ConnectionManagerBehavior;
                cmb.Whitelisted = listen.Whitelisted;

                server.InboundNetworkPeerConnectionParameters = cloneParameters;
                try
                {
                    server.Listen();
                }
                catch (SocketException e)
                {
                    this.logger.LogCritical(
                        "Unable to listen on port {0} (you can change the port using '-port=[number]'). Error message: {1}",
                        listen.Endpoint.Port, e.Message);
                    throw e;
                }

                logs.Append(listen.Endpoint.Address + ":" + listen.Endpoint.Port);
                if (listen.Whitelisted)
                    logs.Append(" (whitelisted)");

                logs.AppendLine();
            }

            this.logger.LogInformation(logs.ToString());
        }

        void AddComponentStats(StringBuilder builder)
        {
            // The total traffic will be the sum of the disconnected peers' traffic and the currently connected peers' traffic.
            var totalRead = this.disconnectedPerfCounter.ReadBytes;
            var totalWritten = this.disconnectedPerfCounter.WrittenBytes;

            void AddPeerInfo(StringBuilder peerBuilder, INetworkPeer peer)
            {
                var chainHeadersBehavior = peer.Behavior<ConsensusManagerBehavior>();
                var connectionManagerBehavior = peer.Behavior<ConnectionManagerBehavior>();

                var peerHeights = "(r/s/c):" +
                                  $"{(chainHeadersBehavior.BestReceivedTip != null ? chainHeadersBehavior.BestReceivedTip.Height.ToString() : peer.PeerVersion != null ? peer.PeerVersion.StartHeight + "*" : "-")}" +
                                  $"/{(chainHeadersBehavior.BestSentHeader != null ? chainHeadersBehavior.BestSentHeader.Height.ToString() : peer.PeerVersion != null ? peer.PeerVersion.StartHeight + "*" : "-")}" +
                                  $"/{chainHeadersBehavior.GetCachedItemsCount()}";

                var peerTraffic =
                    $"R/S MB: {peer.Counter.ReadBytes.BytesToMegaBytes()}/{peer.Counter.WrittenBytes.BytesToMegaBytes()}";
                totalRead += peer.Counter.ReadBytes;
                totalWritten += peer.Counter.WrittenBytes;

                var agent = peer.PeerVersion != null ? peer.PeerVersion.UserAgent : "[Unknown]";
                peerBuilder.AppendLine(
                    (peer.Inbound ? "IN  " : "OUT ") + "Peer:" +
                    (peer.RemoteSocketEndpoint + ", ").PadRight(LoggingConfiguration.ColumnLength + 15)
                    + peerHeights.PadRight(LoggingConfiguration.ColumnLength + 14)
                    + peerTraffic.PadRight(LoggingConfiguration.ColumnLength + 7)
                    + " agent:" + agent);
            }

            var oneTryBuilder = new StringBuilder();
            var whiteListedBuilder = new StringBuilder();
            var addNodeBuilder = new StringBuilder();
            var connectBuilder = new StringBuilder();
            var otherBuilder = new StringBuilder();
            var addNodeDict = this.ConnectionSettings.RetrieveAddNodes().ToDictionary(ep => ep.MapToIpv6(), ep => ep);
            var connectDict = this.ConnectionSettings.Connect.ToDictionary(ep => ep.MapToIpv6(), ep => ep);

            foreach (var peer in this.ConnectedPeers)
            {
                var added = false;

                var connectionManagerBehavior = peer.Behavior<ConnectionManagerBehavior>();
                if (connectionManagerBehavior.OneTry)
                {
                    AddPeerInfo(oneTryBuilder, peer);
                    added = true;
                }

                if (connectionManagerBehavior.Whitelisted)
                {
                    AddPeerInfo(whiteListedBuilder, peer);
                    added = true;
                }

                if (connectDict.ContainsKey(peer.PeerEndPoint))
                {
                    AddPeerInfo(connectBuilder, peer);
                    added = true;
                }

                if (addNodeDict.ContainsKey(peer.PeerEndPoint))
                {
                    AddPeerInfo(addNodeBuilder, peer);
                    added = true;
                }

                if (!added) AddPeerInfo(otherBuilder, peer);
            }

            var inbound = this.ConnectedPeers.Count(x => x.Inbound);

            builder.AppendLine();
            builder.AppendLine(
                $"======Connection====== agent {this.Parameters.UserAgent} [in:{inbound} out:{this.ConnectedPeers.Count() - inbound}] [recv: {totalRead.BytesToMegaBytes()} MB sent: {totalWritten.BytesToMegaBytes()} MB]");

            if (whiteListedBuilder.Length > 0)
            {
                builder.AppendLine(">>> Whitelisted:");
                builder.Append(whiteListedBuilder.ToString());
                builder.AppendLine("<<<");
            }

            if (addNodeBuilder.Length > 0)
            {
                builder.AppendLine(">>> AddNode:");
                builder.Append(addNodeBuilder.ToString());
                builder.AppendLine("<<<");
            }

            if (oneTryBuilder.Length > 0)
            {
                builder.AppendLine(">>> OneTry:");
                builder.Append(oneTryBuilder.ToString());
                builder.AppendLine("<<<");
            }

            if (connectBuilder.Length > 0)
            {
                builder.AppendLine(">>> Connect:");
                builder.Append(connectBuilder.ToString());
                builder.AppendLine("<<<");
            }

            if (otherBuilder.Length > 0)
                builder.Append(otherBuilder.ToString());
        }

        string ToKBSec(ulong bytesPerSec)
        {
            var speed = bytesPerSec / 1024.0;
            return speed.ToString("0.00") + " KB/S";
        }

        Task OnPeerAdded(INetworkPeer peer, CancellationToken cancellationToken)
        {
            // Code in this method is a quick and dirty fix for the race condition described here: https://github.com/stratisproject/StratisBitcoinFullNode/issues/2864
            // TODO race condition should be eliminated instead of fixing its consequences.

            if (ShouldDisconnect(peer))
                peer.Disconnect("Peer from the same network group.");

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Determines if the peer should be disconnected.
        ///     Peer should be disconnected in case it's IP is from the same group in which any other peer
        ///     is and the peer wasn't added using -connect or -addNode command line arguments.
        /// </summary>
        bool ShouldDisconnect(INetworkPeer peer)
        {
            // Don't disconnect if range filtering is not turned on.
            if (!this.ConnectionSettings.IpRangeFiltering)
            {
                this.logger.LogTrace("(-)[IP_RANGE_FILTERING_OFF]:false");
                return false;
            }

            // Don't disconnect if this peer has a local host address.
            if (peer.PeerEndPoint.Address.IsLocal())
            {
                this.logger.LogTrace("(-)[IP_IS_LOCAL]:false");
                return false;
            }

            // Don't disconnect if this peer is in -addnode or -connect.
            if (this.ConnectionSettings.RetrieveAddNodes().Union(this.ConnectionSettings.Connect)
                .Any(ep => peer.PeerEndPoint.MatchIpOnly(ep)))
            {
                this.logger.LogTrace("(-)[ADD_NODE_OR_CONNECT]:false");
                return false;
            }

            // Don't disconnect if this peer is in the exclude from IP range filtering group.
            if (this.ipRangeFilteringEndpointExclusions.Any(ip => ip.MatchIpOnly(peer.PeerEndPoint)))
            {
                this.logger.LogTrace("(-)[PEER_IN_IPRANGEFILTER_EXCLUSIONS]:false");
                return false;
            }

            var peerGroup = peer.PeerEndPoint.MapToIpv6().Address.GetGroup();

            foreach (var connectedPeer in this.ConnectedPeers)
            {
                if (peer == connectedPeer)
                    continue;

                var group = connectedPeer.PeerEndPoint.MapToIpv6().Address.GetGroup();

                if (peerGroup.SequenceEqual(group))
                {
                    this.logger.LogTrace("(-)[SAME_GROUP]:true");
                    return true;
                }
            }

            return false;
        }
    }
}