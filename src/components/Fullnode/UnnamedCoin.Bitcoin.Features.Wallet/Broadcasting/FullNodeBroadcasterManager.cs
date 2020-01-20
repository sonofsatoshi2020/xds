using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Interfaces;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Wallet.Broadcasting
{
    public class FullNodeBroadcasterManager : BroadcasterManagerBase
    {
        /// <summary>Memory pool validator for validating transactions.</summary>
        readonly IMempoolValidator mempoolValidator;

        public FullNodeBroadcasterManager(IConnectionManager connectionManager, IMempoolValidator mempoolValidator) :
            base(connectionManager)
        {
            Guard.NotNull(mempoolValidator, nameof(mempoolValidator));

            this.mempoolValidator = mempoolValidator;
        }

        /// <inheritdoc />
        public override async Task BroadcastTransactionAsync(Transaction transaction)
        {
            Guard.NotNull(transaction, nameof(transaction));

            if (IsPropagated(transaction))
                return;

            var state = new MempoolValidationState(false);

            if (!await this.mempoolValidator.AcceptToMemoryPool(state, transaction).ConfigureAwait(false))
                AddOrUpdate(transaction, State.CantBroadcast, state.Error);
            else
                await PropagateTransactionToPeersAsync(transaction, this.connectionManager.ConnectedPeers.ToList())
                    .ConfigureAwait(false);
        }
    }
}