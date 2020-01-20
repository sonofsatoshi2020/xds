using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NBitcoin.OpenAsset
{
    class NoDuplicateColoredTransactionRepository : IColoredTransactionRepository, ITransactionRepository
    {
        readonly IColoredTransactionRepository _Inner;

        readonly Dictionary<string, Task> _Tasks = new Dictionary<string, Task>();
        readonly ReaderWriterLock @lock = new ReaderWriterLock();

        public NoDuplicateColoredTransactionRepository(IColoredTransactionRepository inner)
        {
            if (inner == null)
                throw new ArgumentNullException("inner");
            this._Inner = inner;
        }

        Task<T> Request<T>(string key, Func<Task<T>> wrapped)
        {
            Task<T> task = null;
            using (this.@lock.LockRead())
            {
                task = this._Tasks.TryGet(key) as Task<T>;
            }

            if (task != null)
                return task;
            using (this.@lock.LockWrite())
            {
                task = this._Tasks.TryGet(key) as Task<T>;
                if (task != null)
                    return task;
                task = wrapped();
                this._Tasks.Add(key, task);
            }

            task.ContinueWith(_ =>
            {
                using (this.@lock.LockWrite())
                {
                    this._Tasks.Remove(key);
                }
            });
            return task;
        }

        #region IColoredTransactionRepository Members

        public ITransactionRepository Transactions => this;

        public Task<ColoredTransaction> GetAsync(uint256 txId)
        {
            return Request("c" + txId, () => this._Inner.GetAsync(txId));
        }

        public Task PutAsync(uint256 txId, ColoredTransaction tx)
        {
            return this._Inner.PutAsync(txId, tx);
        }

        #endregion

        #region ITransactionRepository Members

        Task<Transaction> ITransactionRepository.GetAsync(uint256 txId)
        {
            return Request("t" + txId, () => this._Inner.Transactions.GetAsync(txId));
        }

        public Task PutAsync(uint256 txId, Transaction tx)
        {
            return this._Inner.Transactions.PutAsync(txId, tx);
        }

        #endregion
    }
}