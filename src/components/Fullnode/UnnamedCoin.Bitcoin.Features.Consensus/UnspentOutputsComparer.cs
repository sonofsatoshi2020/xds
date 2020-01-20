using System.Collections.Generic;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Consensus
{
    public class UnspentOutputsComparer : IComparer<UnspentOutputs>
    {
        readonly UInt256Comparer Comparer = new UInt256Comparer();
        public static UnspentOutputsComparer Instance { get; } = new UnspentOutputsComparer();

        public int Compare(UnspentOutputs x, UnspentOutputs y)
        {
            return this.Comparer.Compare(x.TransactionId, y.TransactionId);
        }
    }
}