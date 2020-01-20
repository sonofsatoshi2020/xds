using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using ConcurrentCollections;
using NBitcoin;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.P2P.Peer
{
    public class NetworkPeerEventArgs : EventArgs
    {
        public NetworkPeerEventArgs(INetworkPeer peer, bool added)
        {
            this.Added = added;
            this.Peer = peer;
        }

        public bool Added { get; }

        public INetworkPeer Peer { get; }
    }

    public interface IReadOnlyNetworkPeerCollection : IEnumerable<INetworkPeer>
    {
        INetworkPeer FindByEndpoint(IPEndPoint endpoint);

        /// <summary>
        ///     Returns all connected peers from a given IP address (the port is irrelevant).
        /// </summary>
        /// <param name="ip">The IP address to filter on.</param>
        /// <returns>The set of connected peers that matched the given IP address.</returns>
        List<INetworkPeer> FindByIp(IPAddress ip);

        INetworkPeer FindLocal();
    }

    public class NetworkPeerCollection : IEnumerable<INetworkPeer>, IReadOnlyNetworkPeerCollection
    {
        readonly ConcurrentHashSet<INetworkPeer> networkPeers;

        public NetworkPeerCollection()
        {
            this.networkPeers = new ConcurrentHashSet<INetworkPeer>(new NetworkPeerComparer());
        }

        public int Count => this.networkPeers.Count;


        public IEnumerator<INetworkPeer> GetEnumerator()
        {
            return this.networkPeers.GetEnumerator();
        }


        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public INetworkPeer FindLocal()
        {
            return FindByIp(IPAddress.Loopback).FirstOrDefault();
        }

        public List<INetworkPeer> FindByIp(IPAddress ip)
        {
            ip = ip.EnsureIPv6();
            return this.networkPeers.Where(n => n.MatchRemoteIPAddress(ip)).ToList();
        }

        public INetworkPeer FindByEndpoint(IPEndPoint endpoint)
        {
            var ip = endpoint.Address.EnsureIPv6();
            var port = endpoint.Port;
            return this.networkPeers.FirstOrDefault(n => n.MatchRemoteIPAddress(ip, port));
        }

        public void Add(INetworkPeer peer)
        {
            Guard.NotNull(peer, nameof(peer));

            this.networkPeers.Add(peer);
        }

        public void Remove(INetworkPeer peer)
        {
            this.networkPeers.TryRemove(peer);
        }

        public INetworkPeer FindById(int peerId)
        {
            return this.networkPeers.FirstOrDefault(n => n.Connection.Id == peerId);
        }

        /// <summary>
        ///     Provides a comparer to specify how peers are compared for equality.
        /// </summary>
        public class NetworkPeerComparer : IEqualityComparer<INetworkPeer>
        {
            public bool Equals(INetworkPeer peerA, INetworkPeer peerB)
            {
                if (peerA == null || peerB == null)
                    return peerA == null && peerB == null;

                return peerA.RemoteSocketAddress.MapToIPv6().ToString() ==
                       peerB.RemoteSocketAddress.MapToIPv6().ToString() &&
                       peerA.RemoteSocketPort == peerB.RemoteSocketPort;
            }

            public int GetHashCode(INetworkPeer peer)
            {
                if (peer == null)
                    return 0;

                return peer.RemoteSocketPort.GetHashCode() ^
                       peer.RemoteSocketAddress.MapToIPv6().ToString().GetHashCode();
            }
        }
    }
}