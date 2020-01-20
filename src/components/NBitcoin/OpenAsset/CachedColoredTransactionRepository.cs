using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NBitcoin.OpenAsset
{
    public class CachedColoredTransactionRepository : IColoredTransactionRepository
    {
        readonly Dictionary<uint256, ColoredTransaction> _ColoredTransactions =
            new Dictionary<uint256, ColoredTransaction>();

        readonly Queue<uint256> _EvictionQueue = new Queue<uint256>();
        readonly IColoredTransactionRepository _Inner;
        readonly ReaderWriterLock _lock = new ReaderWriterLock();

        public CachedColoredTransactionRepository(IColoredTransactionRepository inner)
        {
            if (inner == null)
                throw new ArgumentNullException("inner");
            this._Inner = inner;
            this.Transactions = new CachedTransactionRepository(inner.Transactions);
            this.MaxCachedTransactions = 1000;
        }

        public int MaxCachedTransactions
        {
            get => this.Transactions.MaxCachedTransactions;
            set => this.Transactions.MaxCachedTransactions = value;
        }

        public bool WriteThrough
        {
            get => this.Transactions.WriteThrough;
            set => this.Transactions.WriteThrough = value;
        }

        public bool ReadThrough
        {
            get => this.Transactions.ReadThrough;
            set => this.Transactions.ReadThrough = value;
        }

        public ColoredTransaction GetFromCache(uint256 txId)
        {
            using (this._lock.LockRead())
            {
                return this._ColoredTransactions.TryGet(txId);
            }
        }

        #region IColoredTransactionRepository Members

        public CachedTransactionRepository Transactions { get; }

        ITransactionRepository IColoredTransactionRepository.Transactions => this.Transactions;

        void EvictIfNecessary(uint256 txId)
        {
            this._EvictionQueue.Enqueue(txId);
            while (this._ColoredTransactions.Count > this.MaxCachedTransactions && this._EvictionQueue.Count > 0)
                this._ColoredTransactions.Remove(this._EvictionQueue.Dequeue());
        }

        public async Task<ColoredTransaction> GetAsync(uint256 txId)
        {
            ColoredTransaction result = null;
            bool found;
            using (this._lock.LockRead())
            {
                found = this._ColoredTransactions.TryGetValue(txId, out result);
            }

            if (!found)
            {
                result = await this._Inner.GetAsync(txId).ConfigureAwait(false);
                if (this.ReadThrough)
                    using (this._lock.LockWrite())
                    {
                        this._ColoredTransactions.AddOrReplace(txId, result);
                        EvictIfNecessary(txId);
                    }
            }

            return result;
        }

        public Task PutAsync(uint256 txId, ColoredTransaction tx)
        {
            if (this.WriteThrough)
                using (this._lock.LockWrite())
                {
                    if (!this._ColoredTransactions.ContainsKey(txId))
                    {
                        this._ColoredTransactions.AddOrReplace(txId, tx);
                        EvictIfNecessary(txId);
                    }
                    else
                    {
                        this._ColoredTransactions[txId] = tx;
                    }
                }

            return this._Inner.PutAsync(txId, tx);
        }

        #endregion
    }
}