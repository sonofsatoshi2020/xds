using System;
using System.Threading.Tasks;
using NBitcoin;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Interfaces
{
    /// <summary>
    ///     Interface <see cref="ProvenBlockHeader" /> provider.
    /// </summary>
    public interface IProvenBlockHeaderProvider : IDisposable
    {
        /// <summary>
        ///     Height of the block which is currently the tip of the <see cref="ProvenBlockHeader" />.
        /// </summary>
        HashHeightPair TipHashHeight { get; }

        /// <summary>
        ///     Get a <see cref="ProvenBlockHeader" /> corresponding to a block.
        /// </summary>
        /// <param name="blockHeight"> Height used to retrieve the <see cref="ProvenBlockHeader" />.</param>
        /// <returns><see cref="ProvenBlockHeader" /> retrieved.</returns>
        Task<ProvenBlockHeader> GetAsync(int blockHeight);
    }
}