using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.Extensions;

namespace UnnamedCoin.Bitcoin.P2P
{
    /// <summary>
    ///     This manager keeps a set of peers discovered on the network in cache and on disk.
    ///     <para>
    ///         The manager updates peer state according to how recent they have been connected to or not.
    ///     </para>
    /// </summary>
    public sealed class PeerAddressManager : IPeerAddressManager
    {
        /// <summary>The file name of the peers file.</summary>
        internal const string PeerFileName = "peers.json";

        const int MaxAddressesToStoreFromSingleIp = 1500;

        /// <summary>Provider of time functions.</summary>
        readonly IDateTimeProvider dateTimeProvider;

        /// <summary>An object capable of storing a list of <see cref="PeerAddress" />s to the file system.</summary>
        readonly FileStorage<List<PeerAddress>> fileStorage;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Key value store that indexes all discovered peers by their end point.</summary>
        readonly ConcurrentDictionary<IPEndPoint, PeerAddress> peerInfoByPeerAddress;

        /// <summary>Constructor used by dependency injection.</summary>
        public PeerAddressManager(IDateTimeProvider dateTimeProvider, DataFolder peerFilePath,
            ILoggerFactory loggerFactory, ISelfEndpointTracker selfEndpointTracker)
        {
            this.dateTimeProvider = dateTimeProvider;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.peerInfoByPeerAddress = new ConcurrentDictionary<IPEndPoint, PeerAddress>();
            this.PeerFilePath = peerFilePath;
            this.PeerSelector = new PeerSelector(this.dateTimeProvider, loggerFactory, this.peerInfoByPeerAddress,
                selfEndpointTracker);
            this.fileStorage = new FileStorage<List<PeerAddress>>(this.PeerFilePath.AddressManagerFilePath);
        }

        /// <inheritdoc />
        public ICollection<PeerAddress> Peers => this.peerInfoByPeerAddress.Values;

        /// <inheritdoc />
        public DataFolder PeerFilePath { get; set; }

        /// <summary>Peer selector instance, used to select peers to connect to.</summary>
        public IPeerSelector PeerSelector { get; }

        /// <inheritdoc />
        public void LoadPeers()
        {
            var loadedPeers = this.fileStorage.LoadByFileName(PeerFileName);

            this.logger.LogDebug("{0} peers were loaded.", loadedPeers.Count);

            foreach (var peer in loadedPeers)
            {
                // If no longer banned reset ban details.
                if (peer.BanUntil.HasValue && peer.BanUntil < this.dateTimeProvider.GetUtcNow())
                {
                    peer.UnBan();

                    this.logger.LogDebug("{0} no longer banned.", peer.Endpoint);
                }

                // Reset the peer if the attempt threshold has been reached and the attempt window has lapsed.
                if (peer.CanResetAttempts)
                    peer.ResetAttempts();

                this.peerInfoByPeerAddress.TryAdd(peer.Endpoint, peer);
            }
        }

        /// <inheritdoc />
        public void SavePeers()
        {
            if (!this.peerInfoByPeerAddress.Any())
                return;

            this.fileStorage.SaveToFile(this.peerInfoByPeerAddress.Values.ToList(), PeerFileName);
        }

        /// <inheritdoc />
        public PeerAddress AddPeer(IPEndPoint endPoint, IPAddress source)
        {
            var peerAddress = AddPeerWithoutCleanup(endPoint, source);

            EnsureMaxItemsPerSource(source);

            return peerAddress;
        }

        /// <inheritdoc />
        public void AddPeers(IEnumerable<IPEndPoint> endPoints, IPAddress source)
        {
            foreach (var endPoint in endPoints)
                AddPeerWithoutCleanup(endPoint, source);

            EnsureMaxItemsPerSource(source);
        }

        /// <inheritdoc />
        public void RemovePeer(IPEndPoint endPoint)
        {
            this.peerInfoByPeerAddress.TryRemove(endPoint.MapToIpv6(), out var addr);
        }

        /// <inheritdoc />
        public void PeerAttempted(IPEndPoint endpoint, DateTime peerAttemptedAt)
        {
            var peer = FindPeer(endpoint);
            if (peer == null)
                return;

            if (peer.CanResetAttempts)
                peer.ResetAttempts();

            peer.SetAttempted(peerAttemptedAt);
        }

        /// <inheritdoc />
        public void PeerConnected(IPEndPoint endpoint, DateTimeOffset peerConnectedAt)
        {
            var peer = FindPeer(endpoint);

            peer?.SetConnected(peerConnectedAt);
        }

        /// <inheritdoc />
        public void PeerDiscoveredFrom(IPEndPoint endpoint, DateTime peerDiscoveredFrom)
        {
            var peer = FindPeer(endpoint);

            peer?.SetDiscoveredFrom(peerDiscoveredFrom);
        }

        /// <inheritdoc />
        public void PeerHandshaked(IPEndPoint endpoint, DateTimeOffset peerHandshakedAt)
        {
            var peer = FindPeer(endpoint);

            peer?.SetHandshaked(peerHandshakedAt);
        }

        /// <inheritdoc />
        public void PeerSeen(IPEndPoint endpoint, DateTime peerSeenAt)
        {
            var peer = FindPeer(endpoint);

            peer?.SetLastSeen(peerSeenAt);
        }

        /// <inheritdoc />
        public PeerAddress FindPeer(IPEndPoint endPoint)
        {
            var peer = this.peerInfoByPeerAddress.Skip(0).SingleOrDefault(p => p.Key.Match(endPoint));
            return peer.Value;
        }

        /// <inheritdoc />
        public List<PeerAddress> FindPeersByIp(IPEndPoint endPoint)
        {
            var peers = this.peerInfoByPeerAddress.Skip(0).Where(p => p.Key.MatchIpOnly(endPoint));
            return peers.Select(p => p.Value).ToList();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            SavePeers();
        }

        PeerAddress AddPeerWithoutCleanup(IPEndPoint endPoint, IPAddress source)
        {
            if (!endPoint.Address.IsRoutable(true))
            {
                this.logger.LogTrace("(-)[PEER_NOT_ADDED_ISROUTABLE]:{0}", endPoint);
                return null;
            }

            var ipv6EndPoint = endPoint.MapToIpv6();

            var peerToAdd = PeerAddress.Create(ipv6EndPoint, source.MapToIPv6());
            var added = this.peerInfoByPeerAddress.TryAdd(ipv6EndPoint, peerToAdd);
            if (added)
            {
                this.logger.LogTrace("(-)[PEER_ADDED]:{0}", endPoint);
                return peerToAdd;
            }

            this.logger.LogTrace("(-)[PEER_NOT_ADDED_ALREADY_EXISTS]:{0}", endPoint);
            return null;
        }

        void EnsureMaxItemsPerSource(IPAddress source)
        {
            var itemsFromSameSource = this.peerInfoByPeerAddress.Values
                .Where(x => x.Loopback.Equals(source.MapToIPv6())).Select(x => x.Endpoint);
            var itemsToRemove = itemsFromSameSource.Skip(MaxAddressesToStoreFromSingleIp).ToList();

            if (itemsToRemove.Count > 0)
                foreach (var toRemove in itemsToRemove)
                    RemovePeer(toRemove);
        }
    }
}