using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Features.Consensus.CoinViews;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Consensus.ProvenBlockHeaders
{
    /// <inheritdoc />
    public class RewindDataIndexCache : IRewindDataIndexCache
    {
        /// <summary>
        ///     Internal cache for rewind data index. Key is a TxId + N (N is an index of output in a transaction)
        ///     and value is a rewind data index.
        /// </summary>
        readonly ConcurrentDictionary<OutPoint, int> items;

        readonly Network network;

        /// <summary>
        ///     Performance counter to measure performance of the save and get operations.
        /// </summary>
        readonly BackendPerformanceCounter performanceCounter;

        /// <summary>
        ///     Number of blocks to keep in cache after the flush.
        ///     The number of items stored in cache is the sum of inputs used in every transaction in each of those blocks.
        /// </summary>
        int numberOfBlocksToKeep;

        public RewindDataIndexCache(IDateTimeProvider dateTimeProvider, Network network)
        {
            Guard.NotNull(dateTimeProvider, nameof(dateTimeProvider));

            this.network = network;

            this.items = new ConcurrentDictionary<OutPoint, int>();

            this.performanceCounter = new BackendPerformanceCounter(dateTimeProvider);
        }

        /// <inheritdoc />
        public void Initialize(int tipHeight, ICoinView coinView)
        {
            this.items.Clear();

            this.numberOfBlocksToKeep = (int) this.network.Consensus.MaxReorgLength;

            var heightToSyncTo = tipHeight > this.numberOfBlocksToKeep ? tipHeight - this.numberOfBlocksToKeep : 1;

            for (var rewindHeight = tipHeight; rewindHeight >= heightToSyncTo; rewindHeight--)
            {
                var rewindData = coinView.GetRewindData(rewindHeight);

                AddRewindData(rewindHeight, rewindData);
            }
        }

        /// <inheritdoc />
        public void Remove(int tipHeight, ICoinView coinView)
        {
            Flush(tipHeight);

            var bottomHeight = tipHeight > this.numberOfBlocksToKeep ? tipHeight - this.numberOfBlocksToKeep : 1;

            var rewindData = coinView.GetRewindData(bottomHeight);
            AddRewindData(bottomHeight, rewindData);
        }

        /// <inheritdoc />
        public void Save(Dictionary<OutPoint, int> indexData)
        {
            using (new StopwatchDisposable(o => this.performanceCounter.AddInsertTime(o)))
            {
                foreach (var indexRecord in indexData) this.items[indexRecord.Key] = indexRecord.Value;
            }
        }

        /// <inheritdoc />
        public void Flush(int tipHeight)
        {
            var heightToKeepItemsTo = tipHeight > this.numberOfBlocksToKeep ? tipHeight - this.numberOfBlocksToKeep : 1;
            ;

            var listOfItems = this.items.ToList();
            foreach (var item in listOfItems)
                if (item.Value < heightToKeepItemsTo || item.Value > tipHeight)
                    this.items.TryRemove(item.Key, out var unused);
        }

        /// <inheritdoc />
        public int? Get(uint256 transactionId, int transactionOutputIndex)
        {
            var key = new OutPoint(transactionId, transactionOutputIndex);

            if (this.items.TryGetValue(key, out var rewindDataIndex))
                return rewindDataIndex;

            return null;
        }

        /// <summary>
        ///     Adding rewind information for a block in to the cache, we only add the unspent outputs.
        ///     The cache key is [trxid-outputIndex] and the value is the height of the block on with the rewind data information
        ///     is kept.
        /// </summary>
        /// <param name="rewindHeight">Height of the rewind data.</param>
        /// <param name="rewindData">The data itself</param>
        void AddRewindData(int rewindHeight, RewindData rewindData)
        {
            if (rewindData == null)
                throw new ConsensusException($"Rewind data of height '{rewindHeight}' was not found!");

            if (rewindData.OutputsToRestore == null || rewindData.OutputsToRestore.Count == 0) return;

            foreach (var unspent in rewindData.OutputsToRestore)
                for (var outputIndex = 0; outputIndex < unspent.Outputs.Length; outputIndex++)
                {
                    var key = new OutPoint(unspent.TransactionId, outputIndex);
                    this.items[key] = rewindHeight;
                }
        }
    }
}