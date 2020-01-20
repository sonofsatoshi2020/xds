using System;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Behaviors
{
    public interface INetworkPeerBehavior : IDisposable
    {
        INetworkPeer AttachedPeer { get; }

        void Attach(INetworkPeer peer);

        void Detach();

        INetworkPeerBehavior Clone();
    }

    public abstract class NetworkPeerBehavior : INetworkPeerBehavior
    {
        readonly object cs = new object();

        public INetworkPeer AttachedPeer { get; private set; }


        public void Attach(INetworkPeer peer)
        {
            Guard.NotNull(peer, nameof(peer));

            if (this.AttachedPeer != null)
                throw new InvalidOperationException("Behavior already attached to a peer");

            lock (this.cs)
            {
                if (Disconnected(peer))
                    return;

                this.AttachedPeer = peer;

                AttachCore();
            }
        }


        public void Detach()
        {
            lock (this.cs)
            {
                if (this.AttachedPeer == null)
                    return;

                DetachCore();
            }
        }

        /// <inheritdoc />
        public virtual void Dispose()
        {
            this.AttachedPeer = null;
        }


        INetworkPeerBehavior INetworkPeerBehavior.Clone()
        {
            return (INetworkPeerBehavior) Clone();
        }

        protected abstract void AttachCore();

        protected abstract void DetachCore();

        public abstract object Clone();


        protected void AssertNotAttached()
        {
            if (this.AttachedPeer != null)
                throw new InvalidOperationException("Can't modify the behavior while it is attached");
        }

        static bool Disconnected(INetworkPeer peer)
        {
            return peer.State == NetworkPeerState.Created || peer.State == NetworkPeerState.Disconnecting ||
                   peer.State == NetworkPeerState.Failed || peer.State == NetworkPeerState.Offline;
        }
    }
}