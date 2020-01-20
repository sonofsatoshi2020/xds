using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.AsyncWork;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.Extensions;

namespace UnnamedCoin.Bitcoin.P2P
{
    /// <summary>
    ///     Contract for <see cref="PeerDiscovery" />.
    /// </summary>
    public interface IPeerDiscovery : IDisposable
    {
        /// <summary>
        ///     Starts the peer discovery process.
        /// </summary>
        void DiscoverPeers(IConnectionManager connectionManager);
    }

    /// <summary>Async loop that discovers new peers to connect to.</summary>
    public sealed class PeerDiscovery : IPeerDiscovery
    {
        const int TargetAmountOfPeersToDiscover = 2000;

        /// <summary>Factory for creating background async loop tasks.</summary>
        readonly IAsyncProvider asyncProvider;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Logger factory to create loggers.</summary>
        readonly ILoggerFactory loggerFactory;

        /// <summary>The network the node is running on.</summary>
        readonly Network network;

        /// <summary>Factory for creating P2P network peers.</summary>
        readonly INetworkPeerFactory networkPeerFactory;

        /// <summary>Global application life cycle control - triggers when application shuts down.</summary>
        readonly INodeLifetime nodeLifetime;

        /// <summary>User defined node settings.</summary>
        readonly NodeSettings nodeSettings;

        /// <summary>Peer address manager instance, see <see cref="IPeerAddressManager" />.</summary>
        readonly IPeerAddressManager peerAddressManager;

        /// <summary>The parameters cloned from the connection manager.</summary>
        NetworkPeerConnectionParameters currentParameters;

        /// <summary>
        ///     The async loop for discovering from DNS seeds & seed nodes. We need to wait upon it before we can shut down
        ///     this connector.
        /// </summary>
        IAsyncLoop discoverFromDnsSeedsLoop;

        /// <summary>
        ///     The async loop for performing discovery on actual peers. We need to wait upon it before we can shut down this
        ///     connector.
        /// </summary>
        IAsyncLoop discoverFromPeersLoop;

        public PeerDiscovery(
            IAsyncProvider asyncProvider,
            ILoggerFactory loggerFactory,
            Network network,
            INetworkPeerFactory networkPeerFactory,
            INodeLifetime nodeLifetime,
            NodeSettings nodeSettings,
            IPeerAddressManager peerAddressManager)
        {
            this.asyncProvider = asyncProvider;
            this.loggerFactory = loggerFactory;
            this.logger = this.loggerFactory.CreateLogger(GetType().FullName);
            this.peerAddressManager = peerAddressManager;
            this.network = network;
            this.networkPeerFactory = networkPeerFactory;
            this.nodeLifetime = nodeLifetime;
            this.nodeSettings = nodeSettings;
        }

        /// <inheritdoc />
        public void DiscoverPeers(IConnectionManager connectionManager)
        {
            // If peers are specified in the -connect arg then discovery does not happen.
            if (connectionManager.ConnectionSettings.Connect.Any())
                return;

            if (!connectionManager.Parameters.PeerAddressManagerBehaviour().Mode
                .HasFlag(PeerAddressManagerBehaviourMode.Discover))
                return;

            this.currentParameters =
                connectionManager.Parameters
                    .Clone(); // TODO we shouldn't add all the behaviors, only those that we need.

            this.discoverFromDnsSeedsLoop = this.asyncProvider.CreateAndRunAsyncLoop(nameof(DiscoverFromDnsSeedsAsync),
                async token =>
                {
                    if (this.peerAddressManager.Peers.Count < TargetAmountOfPeersToDiscover)
                        await DiscoverFromDnsSeedsAsync();
                },
                this.nodeLifetime.ApplicationStopping,
                TimeSpan.FromHours(1));

            this.discoverFromPeersLoop = this.asyncProvider.CreateAndRunAsyncLoop(nameof(DiscoverPeersAsync),
                async token =>
                {
                    if (this.peerAddressManager.Peers.Count < TargetAmountOfPeersToDiscover)
                        await DiscoverPeersAsync();
                },
                this.nodeLifetime.ApplicationStopping,
                TimeSpans.TenSeconds);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.discoverFromPeersLoop?.Dispose();
            this.discoverFromDnsSeedsLoop?.Dispose();
        }

        /// <summary>
        ///     See <see cref="DiscoverPeers" />. This loop deals with discovery from DNS seeds and seed nodes as opposed to peers.
        /// </summary>
        async Task DiscoverFromDnsSeedsAsync()
        {
            var peersToDiscover = new List<IPEndPoint>();

            // First see if we need to do DNS discovery at all. We may have peers from a previous cycle that still need to be tried.
            if (this.peerAddressManager.Peers.Select(a => !a.Attempted).Any())
            {
                this.logger.LogTrace("(-)[SKIP_DISCOVERY_UNATTEMPTED_PEERS_REMAINING]");
                return;
            }

            // At this point there are either no peers that we know of, or all the ones we do know of have been attempted & failed.
            AddDNSSeedNodes(peersToDiscover);
            AddSeedNodes(peersToDiscover);

            if (peersToDiscover.Count == 0)
            {
                this.logger.LogTrace("(-)[NO_DNS_SEED_ADDRESSES]");
                return;
            }

            // Randomise the order prior to attempting connections.
            peersToDiscover = peersToDiscover.OrderBy(a => RandomUtils.GetInt32()).ToList();

            await ConnectToDiscoveryCandidatesAsync(peersToDiscover).ConfigureAwait(false);
        }

        /// <summary>
        ///     See <see cref="DiscoverPeers" />. This loop deals with discovery from peers as opposed to DNS seeds and seed nodes.
        /// </summary>
        async Task DiscoverPeersAsync()
        {
            var peersToDiscover = new List<IPEndPoint>();

            // The peer selector returns a quantity of peers for discovery already in random order.
            var foundPeers = this.peerAddressManager.PeerSelector.SelectPeersForDiscovery(1000).ToList();
            peersToDiscover.AddRange(foundPeers.Select(p => p.Endpoint));

            if (peersToDiscover.Count == 0)
            {
                this.logger.LogTrace("(-)[NO_ADDRESSES]");
                return;
            }

            await ConnectToDiscoveryCandidatesAsync(peersToDiscover).ConfigureAwait(false);
        }

        async Task ConnectToDiscoveryCandidatesAsync(List<IPEndPoint> peersToDiscover)
        {
            await peersToDiscover.ForEachAsync(5, this.nodeLifetime.ApplicationStopping,
                async (endPoint, cancellation) =>
                {
                    using (var connectTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        this.logger.LogDebug("Attempting to discover from : '{0}'", endPoint);

                        connectTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

                        INetworkPeer networkPeer = null;

                        // Try to connect to a peer with only the address-sharing behaviour, to learn about their peers and disconnect within 5 seconds.

                        try
                        {
                            var clonedParameters = this.currentParameters.Clone();
                            clonedParameters.ConnectCancellation = connectTokenSource.Token;

                            var addressManagerBehaviour = clonedParameters.TemplateBehaviors
                                .OfType<PeerAddressManagerBehaviour>().FirstOrDefault();
                            clonedParameters.TemplateBehaviors.Clear();
                            clonedParameters.TemplateBehaviors.Add(addressManagerBehaviour);

                            networkPeer = await this.networkPeerFactory
                                .CreateConnectedNetworkPeerAsync(endPoint, clonedParameters).ConfigureAwait(false);
                            await networkPeer.VersionHandshakeAsync(connectTokenSource.Token).ConfigureAwait(false);

                            this.peerAddressManager.PeerDiscoveredFrom(endPoint, DateTimeProvider.Default.GetUtcNow());

                            connectTokenSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                        }
                        catch
                        {
                        }
                        finally
                        {
                            networkPeer?.Disconnect("Discovery job done");
                            networkPeer?.Dispose();
                        }

                        this.logger.LogDebug("Discovery from '{0}' finished", endPoint);
                    }
                }).ConfigureAwait(false);
        }

        /// <summary>
        ///     Add peers to the address manager from the network's DNS seed nodes.
        /// </summary>
        void AddDNSSeedNodes(List<IPEndPoint> endPoints)
        {
            foreach (var seed in this.network.DNSSeeds)
                try
                {
                    // We want to try to ensure we get a fresh set of results from the seeder each time we query it.
                    var ipAddresses = seed.GetAddressNodes(true);
                    endPoints.AddRange(ipAddresses.Select(ip => new IPEndPoint(ip, this.network.DefaultPort)));
                }
                catch (Exception)
                {
                    this.logger.LogWarning("Error getting seed node addresses from {0}.", seed.Host);
                }
        }

        /// <summary>
        ///     Add peers to the address manager from the network's seed nodes.
        /// </summary>
        void AddSeedNodes(List<IPEndPoint> endPoints)
        {
            endPoints.AddRange(this.network.SeedNodes.Select(ipAddress => ipAddress.Endpoint));
        }
    }
}