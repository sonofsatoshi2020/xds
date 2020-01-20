using NBitcoin;

namespace UnnamedCoin.Bitcoin.EventBus.CoreEvents
{
    /// <summary>
    ///     Event that is executed when a transaction is received from another peer.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class TransactionReceived : EventBase
    {
        public TransactionReceived(Transaction receivedTransaction)
        {
            this.ReceivedTransaction = receivedTransaction;
        }

        public Transaction ReceivedTransaction { get; }
    }
}