using System;
using System.Threading.Tasks;
using NBitcoin;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.Wallet.Broadcasting;

namespace UnnamedCoin.Bitcoin.Features.Wallet.Interfaces
{
    public interface IBroadcasterManager
    {
        Task BroadcastTransactionAsync(Transaction transaction);

        event EventHandler<TransactionBroadcastEntry> TransactionStateChanged;

        TransactionBroadcastEntry GetTransaction(uint256 transactionHash);

        void AddOrUpdate(Transaction transaction, State state, MempoolError mempoolError = null);
    }
}