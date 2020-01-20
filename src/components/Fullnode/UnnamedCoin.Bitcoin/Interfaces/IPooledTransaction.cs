using System.Threading.Tasks;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.Interfaces
{
    public interface IPooledTransaction
    {
        Task<Transaction> GetTransaction(uint256 trxid);
    }
}