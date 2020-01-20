using System;
using System.Threading.Tasks;

namespace NBitcoin
{
    public class NoSqlTransactionRepository : ITransactionRepository
    {
        public NoSqlTransactionRepository(Network network)
            : this(new InMemoryNoSqlRepository(network))
        {
        }

        public NoSqlTransactionRepository(NoSqlRepository repository)
        {
            this.Repository = repository ?? throw new ArgumentNullException("repository");
        }

        public NoSqlRepository Repository { get; }

        public Task<Transaction> GetAsync(uint256 txId)
        {
            return this.Repository.GetAsync<Transaction>(GetId(txId));
        }

        public Task PutAsync(uint256 txId, Transaction tx)
        {
            return this.Repository.PutAsync(GetId(txId), tx);
        }

        string GetId(uint256 txId)
        {
            return "tx-" + txId;
        }
    }
}