using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConcurrentCollections;
using NBitcoin;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.Wallet.Interfaces;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Wallet.Broadcasting
{
    public abstract class BroadcasterManagerBase : IBroadcasterManager
    {
        /// <summary> Connection manager for managing node connections.</summary>
        protected readonly IConnectionManager connectionManager;

        public BroadcasterManagerBase(IConnectionManager connectionManager)
        {
            Guard.NotNull(connectionManager, nameof(connectionManager));

            this.connectionManager = connectionManager;
            this.Broadcasts = new ConcurrentHashSet<TransactionBroadcastEntry>();
        }

        ConcurrentHashSet<TransactionBroadcastEntry> Broadcasts { get; }
        public event EventHandler<TransactionBroadcastEntry> TransactionStateChanged;

        public TransactionBroadcastEntry GetTransaction(uint256 transactionHash)
        {
            var txEntry = this.Broadcasts.FirstOrDefault(x => x.Transaction.GetHash() == transactionHash);
            return txEntry ?? null;
        }

        public void AddOrUpdate(Transaction transaction, State state, MempoolError mempoolError = null)
        {
            var broadcastEntry = this.Broadcasts.FirstOrDefault(x => x.Transaction.GetHash() == transaction.GetHash());

            if (broadcastEntry == null)
            {
                broadcastEntry = new TransactionBroadcastEntry(transaction, state, mempoolError);
                this.Broadcasts.Add(broadcastEntry);
                OnTransactionStateChanged(broadcastEntry);
            }
            else if (broadcastEntry.State != state)
            {
                broadcastEntry.State = state;
                OnTransactionStateChanged(broadcastEntry);
            }
        }

        public abstract Task BroadcastTransactionAsync(Transaction transaction);

        public void OnTransactionStateChanged(TransactionBroadcastEntry entry)
        {
            TransactionStateChanged?.Invoke(this, entry);
        }

        /// <summary>
        ///     Sends transaction to peers.
        /// </summary>
        /// <param name="transaction">Transaction that will be propagated.</param>
        /// <param name="peers">Peers to whom we will propagate the transaction.</param>
        protected async Task PropagateTransactionToPeersAsync(Transaction transaction, List<INetworkPeer> peers)
        {
            AddOrUpdate(transaction, State.ToBroadcast);

            var invPayload = new InvPayload(transaction);

            foreach (var peer in peers)
                try
                {
                    await peer.SendMessageAsync(invPayload).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
        }

        protected bool IsPropagated(Transaction transaction)
        {
            var broadcastEntry = GetTransaction(transaction.GetHash());
            return broadcastEntry != null && broadcastEntry.State == State.Propagated;
        }
    }
}