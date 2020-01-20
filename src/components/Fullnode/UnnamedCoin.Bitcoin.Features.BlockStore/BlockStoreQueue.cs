using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.AsyncWork;
using UnnamedCoin.Bitcoin.Base;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.Primitives;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.Extensions;

namespace UnnamedCoin.Bitcoin.Features.BlockStore
{
    /// <summary>
    ///     Saves blocks to the database in batches, removes reorged blocks from the database.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The batch is saved when total serialized size of all blocks in a batch reaches
    ///         <see cref="BatchThresholdSizeBytes" />,
    ///         or when more than <see cref="BatchMaxSaveIntervalSeconds" /> passed since last batch was saved, or when node is
    ///         shutting down.
    ///     </para>
    ///     <para>
    ///         When we save new blocks to the database, in case <see cref="IBlockRepository" /> contains blocks that
    ///         are no longer a part of our best chain, they are removed from the database.
    ///     </para>
    ///     <para>
    ///         When block store is being initialized we delete blocks that are not on the best chain.
    ///     </para>
    /// </remarks>
    public class BlockStoreQueue : IBlockStoreQueue
    {
        /// <summary>Maximum interval between saving batches.</summary>
        /// <remarks>
        ///     Interval value is a prime number that wasn't used as an interval in any other component. That prevents having
        ///     CPU consumption spikes.
        /// </remarks>
        const int BatchMaxSaveIntervalSeconds = 17;

        readonly IAsyncProvider asyncProvider;

        /// <summary>Batch of blocks which should be saved in the database.</summary>
        /// <remarks>Write access should be protected by <see cref="blocksCacheLock" />.</remarks>
        readonly List<ChainedHeaderBlock> batch;

        /// <inheritdoc cref="IBlockRepository" />
        readonly IBlockRepository blockRepository;

        /// <summary>Protects the batch from being modifying while <see cref="GetBlock" /> method is using the batch.</summary>
        readonly object blocksCacheLock;

        /// <summary>Queue which contains blocks that should be saved to the database.</summary>
        readonly IAsyncQueue<ChainedHeaderBlock> blocksQueue;

        readonly IBlockStoreQueueFlushCondition blockStoreQueueFlushCondition;

        readonly CancellationTokenSource cancellation;

        /// <inheritdoc cref="ChainIndexer" />
        readonly ChainIndexer chainIndexer;

        /// <inheritdoc cref="IChainState" />
        readonly IChainState chainState;

        /// <inheritdoc cref="ILogger" />
        readonly ILogger logger;

        /// <summary>
        ///     Represents all blocks currently in the queue & pending batch, so that <see cref="GetBlock" /> is able to
        ///     return a value directly after enqueuing.
        /// </summary>
        /// <remarks>Write access should be protected by <see cref="blocksCacheLock" />.</remarks>
        readonly Dictionary<uint256, ChainedHeaderBlock> pendingBlocksCache;

        /// <inheritdoc cref="StoreSettings" />
        readonly StoreSettings storeSettings;

        /// <summary>Maximum number of bytes the batch can hold until the downloaded blocks are stored to the disk.</summary>
        internal long BatchThresholdSizeBytes;

        /// <summary>The current pending blocks size in bytes.</summary>
        long blocksQueueSizeBytes;

        /// <summary>The current batch size in bytes.</summary>
        long currentBatchSizeBytes;

        /// <summary>Task that runs <see cref="DequeueBlocksContinuouslyAsync" />.</summary>
        Task dequeueLoopTask;

        Exception saveAsyncLoopException;

        /// <summary>The highest stored block in the repository.</summary>
        ChainedHeader storeTip;

        public BlockStoreQueue(
            ChainIndexer chainIndexer,
            IChainState chainState,
            IBlockStoreQueueFlushCondition blockStoreQueueFlushCondition,
            StoreSettings storeSettings,
            IBlockRepository blockRepository,
            ILoggerFactory loggerFactory,
            INodeStats nodeStats,
            IAsyncProvider asyncProvider)
        {
            Guard.NotNull(blockStoreQueueFlushCondition, nameof(blockStoreQueueFlushCondition));
            Guard.NotNull(chainIndexer, nameof(chainIndexer));
            Guard.NotNull(chainState, nameof(chainState));
            Guard.NotNull(storeSettings, nameof(storeSettings));
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(blockRepository, nameof(blockRepository));
            Guard.NotNull(nodeStats, nameof(nodeStats));

            this.blockStoreQueueFlushCondition = blockStoreQueueFlushCondition;
            this.chainIndexer = chainIndexer;
            this.chainState = chainState;
            this.storeSettings = storeSettings;
            this.blockRepository = blockRepository;
            this.asyncProvider = asyncProvider;
            this.batch = new List<ChainedHeaderBlock>();
            this.blocksCacheLock = new object();
            this.blocksQueue = asyncProvider.CreateAsyncQueue<ChainedHeaderBlock>();
            this.pendingBlocksCache = new Dictionary<uint256, ChainedHeaderBlock>();
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.cancellation = new CancellationTokenSource();
            this.saveAsyncLoopException = null;

            this.BatchThresholdSizeBytes = storeSettings.MaxCacheSize * 1024 * 1024;

            nodeStats.RegisterStats(AddComponentStats, StatsType.Component, GetType().Name);
        }

        /// <inheritdoc />
        public ChainedHeader BlockStoreCacheTip { get; private set; }

        /// <summary>
        ///     Initializes the <see cref="BlockStoreQueue" />.
        ///     <para>
        ///         If <see cref="storeTip" /> is <c>null</c>, the store is out of sync. This can happen when:
        ///     </para>
        ///     <list>
        ///         <item>The node crashed.</item>
        ///         <item>The node was not closed down properly.</item>
        ///     </list>
        ///     <para>
        ///         To recover we walk back the chain until a common block header is found and set the
        ///         <see cref="BlockStoreQueue" />'s <see cref="storeTip" /> to that.
        ///     </para>
        /// </summary>
        public void Initialize()
        {
            this.blockRepository.Initialize();

            if (this.storeSettings.ReIndex)
            {
                this.blockRepository.SetTxIndex(this.storeSettings.TxIndex);
                this.blockRepository.ReIndex();
            }

            var initializationTip = this.chainIndexer.GetHeader(this.blockRepository.TipHashAndHeight.Hash);
            SetStoreTip(initializationTip);

            if (this.storeTip == null)
                RecoverStoreTip();

            this.logger.LogDebug("Initialized block store tip at '{0}'.", this.storeTip);

            if (this.storeSettings.TxIndex != this.blockRepository.TxIndex)
            {
                if (this.storeTip != this.chainIndexer.Genesis)
                {
                    this.logger.LogTrace("(-)[REBUILD_REQUIRED]");
                    throw new BlockStoreException(
                        "You need to rebuild the block store database using -reindex to change -txindex");
                }

                // We only reach here in the case where we are syncing with a database with no blocks.
                // Always set the TxIndex here.
                this.blockRepository.SetTxIndex(this.storeSettings.TxIndex);
            }

            // Throw if block store was initialized after the consensus.
            // This is needed in order to rewind consensus in case it is initialized ahead of the block store.
            if (this.chainState.ConsensusTip != null)
            {
                this.logger.LogCritical("Block store initialized after the consensus!");
                this.logger.LogTrace("(-)[INITIALIZATION_ERROR]");
                throw new BlockStoreException("Block store initialized after consensus!");
            }

            this.BlockStoreCacheTip = initializationTip;

            // Start dequeuing.
            this.currentBatchSizeBytes = 0;
            this.dequeueLoopTask = DequeueBlocksContinuouslyAsync();

            this.asyncProvider.RegisterTask($"{nameof(BlockStoreQueue)}.{nameof(this.dequeueLoopTask)}",
                this.dequeueLoopTask);
        }

        /// <inheritdoc />
        public Transaction GetTransactionById(uint256 trxid)
        {
            // Only look for transactions if they're indexed.
            if (!this.storeSettings.TxIndex)
            {
                this.logger.LogTrace("(-)[TX_INDEX_DISABLED]:null");
                return default;
            }

            lock (this.blocksCacheLock)
            {
                foreach (var chainedHeaderBlock in this.pendingBlocksCache.Values)
                {
                    var tx = chainedHeaderBlock.Block.Transactions.FirstOrDefault(x => x.GetHash() == trxid);

                    if (tx != null)
                    {
                        this.logger.LogDebug("Transaction '{0}' was found in the pending blocks cache.", trxid);
                        return tx;
                    }
                }
            }

            return this.blockRepository.GetTransactionById(trxid);
        }

        /// <inheritdoc />
        public Transaction[] GetTransactionsByIds(uint256[] trxids, CancellationToken cancellation = default)
        {
            // Only look for transactions if they're indexed.
            if (!this.storeSettings.TxIndex)
            {
                this.logger.LogTrace("(-)[TX_INDEX_DISABLED]:null");
                return null;
            }

            var txes = new Transaction[trxids.Length];

            lock (this.blocksCacheLock)
            {
                for (var i = 0; i < trxids.Length; i++)
                {
                    var txId = trxids[i];

                    var tx = this.pendingBlocksCache.Values.SelectMany(block => block.Block.Transactions)
                        .FirstOrDefault(x => x.GetHash() == txId);

                    if (tx != null)
                    {
                        this.logger.LogDebug("Transaction '{0}' was found in the pending blocks cache.", txId);
                        txes[i] = tx;
                    }
                }
            }

            var notFoundIds = new List<uint256>();

            for (var i = 0; i < trxids.Length; i++)
                if (txes[i] == null)
                    notFoundIds.Add(trxids[i]);

            var fetchedTxes = this.blockRepository.GetTransactionsByIds(notFoundIds.ToArray(), cancellation);

            if (fetchedTxes == null)
            {
                this.logger.LogTrace("(-)[NOT_FOUND_IN_REPOSITORY]:null");
                return null;
            }

            var fetchedIndex = 0;

            for (var i = 0; i < txes.Length; i++)
                if (txes[i] == null)
                {
                    txes[i] = fetchedTxes[fetchedIndex];
                    fetchedIndex++;
                }

            return txes;
        }

        /// <inheritdoc />
        public uint256 GetBlockIdByTransactionId(uint256 trxid)
        {
            lock (this.blocksCacheLock)
            {
                foreach (var chainedHeaderBlock in this.pendingBlocksCache.Values)
                {
                    var exists = chainedHeaderBlock.Block.Transactions.Any(x => x.GetHash() == trxid);

                    if (exists)
                    {
                        var blockId = chainedHeaderBlock.Block.GetHash();

                        this.logger.LogDebug("Block Id '{0}' with tx '{1}' was found in the pending blocks cache.",
                            blockId, trxid);
                        return blockId;
                    }
                }
            }

            return this.blockRepository.GetBlockIdByTransactionId(trxid);
        }

        /// <inheritdoc />
        public Block GetBlock(uint256 blockHash)
        {
            lock (this.blocksCacheLock)
            {
                if (this.pendingBlocksCache.TryGetValue(blockHash, out var chainedHeaderBlock))
                {
                    this.logger.LogTrace("(-)[FOUND_IN_DICTIONARY]");
                    return chainedHeaderBlock.Block;
                }
            }

            var block = this.blockRepository.GetBlock(blockHash);

            this.logger.LogDebug("Block '{0}' was{1} found in the repository.", blockHash, block == null ? " not" : "");

            return block;
        }

        public List<Block> GetBlocks(List<uint256> blockHashes)
        {
            lock (this.blocksCacheLock)
            {
                var res = new Dictionary<uint256, Block>();

                foreach (var key in blockHashes.Intersect(this.pendingBlocksCache.Keys))
                    res[key] = this.pendingBlocksCache[key].Block;

                var cacheCount = res.Count;

                var storeHashes = blockHashes.Except(this.pendingBlocksCache.Keys).ToList();
                foreach (var (block, hashIndex) in this.blockRepository.GetBlocks(storeHashes).Select((x, n) => (x, n)))
                    res[storeHashes[hashIndex]] = block;

                this.logger.LogTrace("{0} blocks were found in the cache of a total of {1} blocks.", cacheCount,
                    res.Count);

                return blockHashes.Select(bh => res[bh]).ToList();
            }
        }

        /// <inheritdoc />
        public void AddToPending(ChainedHeaderBlock chainedHeaderBlock)
        {
            // Throw any error encountered by the asynchronous loop.
            if (this.saveAsyncLoopException != null)
                throw this.saveAsyncLoopException;

            lock (this.blocksCacheLock)
            {
                if (this.pendingBlocksCache.TryAdd(chainedHeaderBlock.ChainedHeader.HashBlock, chainedHeaderBlock))
                {
                    this.logger.LogDebug("Block '{0}' was added to pending.", chainedHeaderBlock.ChainedHeader);
                }
                else
                {
                    // If the chained header block already exists, we need to remove it and add to the back of the collection.
                    this.pendingBlocksCache.Remove(chainedHeaderBlock.ChainedHeader.HashBlock);
                    this.pendingBlocksCache.Add(chainedHeaderBlock.ChainedHeader.HashBlock, chainedHeaderBlock);
                    this.logger.LogDebug("Block '{0}' was re-added to pending.", chainedHeaderBlock.ChainedHeader);
                }

                this.BlockStoreCacheTip = chainedHeaderBlock.ChainedHeader;
            }

            this.blocksQueue.Enqueue(chainedHeaderBlock);
            this.blocksQueueSizeBytes += chainedHeaderBlock.Block.BlockSize.Value;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Let current batch saving task finish.
            this.cancellation.Cancel();
            this.blocksQueue.Dispose();
            this.dequeueLoopTask?.GetAwaiter().GetResult();
            this.blockRepository.Dispose();
        }

        /// <summary>Sets the internal store tip and exposes the store tip to other components through the chain state.</summary>
        void SetStoreTip(ChainedHeader newTip)
        {
            this.storeTip = newTip;
            this.chainState.BlockStoreTip = newTip;
        }

        /// <summary>
        ///     Sets block store tip to the last block that exists both in the repository and in the <see cref="ChainIndexer" />.
        /// </summary>
        void RecoverStoreTip()
        {
            var blockStoreResetList = new List<uint256>();

            var resetBlockHash = this.blockRepository.TipHashAndHeight.Hash;
            var resetBlock = this.blockRepository.GetBlock(resetBlockHash);

            while (this.chainIndexer.GetHeader(resetBlockHash) == null)
            {
                blockStoreResetList.Add(resetBlockHash);

                if (resetBlock.Header.HashPrevBlock == this.chainIndexer.Genesis.HashBlock)
                {
                    resetBlockHash = this.chainIndexer.Genesis.HashBlock;
                    break;
                }

                resetBlock = this.blockRepository.GetBlock(resetBlock.Header.HashPrevBlock);

                if (resetBlock == null)
                    // This can happen only if block store is corrupted.
                    throw new BlockStoreException("Block store failed to recover.");

                resetBlockHash = resetBlock.GetHash();
            }

            var newTip = this.chainIndexer.GetHeader(resetBlockHash);

            if (blockStoreResetList.Count != 0)
                this.blockRepository.Delete(new HashHeightPair(newTip), blockStoreResetList);

            SetStoreTip(newTip);

            // TODO: this will be replaced with tips manager
            // TODO this thing should remove stuff from chain database. Otherwise we are leaving redundant data.
            this.chainIndexer.Initialize(newTip); // we have to set chain store to be same as the store tip.

            this.logger.LogWarning("Block store tip recovered to block '{0}'.", newTip);
        }


        void AddComponentStats(StringBuilder log)
        {
            if (this.storeTip != null)
            {
                log.AppendLine();
                log.AppendLine("======BlockStore======");
                log.AppendLine(
                    $"Batch Size: {this.currentBatchSizeBytes.BytesToMegaBytes()} MB / {this.BatchThresholdSizeBytes.BytesToMegaBytes()} MB ({this.batch.Count} batched blocks)");
                log.AppendLine(
                    $"Queue Size: {this.blocksQueueSizeBytes.BytesToMegaBytes()} MB ({this.blocksQueue.Count} queued blocks)");
            }
        }

        /// <summary>
        ///     Dequeues the blocks continuously and saves them to the database when max batch size is reached or timer ran out.
        /// </summary>
        /// <remarks>Batch is always saved on shutdown.</remarks>
        async Task DequeueBlocksContinuouslyAsync()
        {
            Task<ChainedHeaderBlock> dequeueTask = null;
            Task timerTask = null;

            while (!this.cancellation.IsCancellationRequested)
            {
                // Start new dequeue task if not started already.
                dequeueTask = dequeueTask ?? this.blocksQueue.DequeueAsync();

                // Wait for one of the tasks: dequeue or timer (if available) to finish.
                var task = timerTask == null
                    ? dequeueTask
                    : await Task.WhenAny(dequeueTask, timerTask).ConfigureAwait(false);

                var saveBatch = false;

                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Can happen if Dispose() was called.
                    // We want to save whatever is in the batch before exiting the loop.
                    saveBatch = true;

                    this.logger.LogDebug("Node is shutting down. Save batch.");
                }

                // Save batch if timer ran out or we've dequeued a new block and reached the consensus tip
                // or the max batch size is reached or the node is shutting down.
                if (dequeueTask.Status == TaskStatus.RanToCompletion)
                {
                    var item = dequeueTask.Result;

                    // Set the dequeue task to null so it can be assigned on the next iteration.
                    dequeueTask = null;

                    lock (this.blocksCacheLock)
                    {
                        this.batch.Add(item);
                    }

                    this.blocksQueueSizeBytes -= item.Block.BlockSize.Value;
                    this.currentBatchSizeBytes += item.Block.BlockSize.Value;

                    saveBatch = saveBatch || this.currentBatchSizeBytes >= this.BatchThresholdSizeBytes ||
                                this.blockStoreQueueFlushCondition.ShouldFlush;
                }
                else
                {
                    // Will be executed in case timer ran out or node is being shut down.
                    saveBatch = true;
                }

                if (saveBatch)
                {
                    if (this.batch.Count != 0)
                        try
                        {
                            SaveBatch();

                            // If an error occurred during SaveBatchAsync then this code
                            // which clears the batch will not execute.
                            lock (this.blocksCacheLock)
                            {
                                foreach (var chainedHeaderBlock in this.batch)
                                    this.pendingBlocksCache.Remove(chainedHeaderBlock.ChainedHeader.HashBlock);

                                this.batch.Clear();
                            }

                            this.currentBatchSizeBytes = 0;
                        }
                        catch (Exception err)
                        {
                            this.logger.LogError("Could not save blocks to the block repository. Exiting due to '{0}'.",
                                err.Message);
                            this.saveAsyncLoopException = err;
                            throw;
                        }

                    timerTask = null;
                }
                else
                {
                    // Start timer if it is not started already.
                    timerTask = timerTask ?? Task.Delay(BatchMaxSaveIntervalSeconds * 1000, this.cancellation.Token);
                }
            }

            FlushAllCollections();
        }

        /// <summary>
        ///     Ensures that any blocks queued in <see cref="blocksQueue" /> gets added to <see cref="batch" />
        ///     so that it can be persisted on dispose.
        /// </summary>
        void FlushAllCollections()
        {
            ChainedHeaderBlock chainedHeaderBlock = null;
            while (this.blocksQueue.TryDequeue(out chainedHeaderBlock)) this.batch.Add(chainedHeaderBlock);

            if (this.batch.Count != 0)
                SaveBatch();
        }

        /// <summary>
        ///     Checks if repository contains reorged blocks and deletes them; saves batch on top.
        ///     The last block in the list is considered to be on the current main chain and will be used to determine if a
        ///     database reorg is required.
        /// </summary>
        /// <exception cref="DBreeze.Exceptions.DBreezeException">Thrown if an error occurs during database operations.</exception>
        void SaveBatch()
        {
            var clearedBatch = GetBatchWithoutReorgedBlocks();

            var expectedStoreTip = clearedBatch.First().ChainedHeader.Previous;

            // Check if block repository contains reorged blocks. If it does - delete them.
            if (expectedStoreTip.HashBlock != this.storeTip.HashBlock)
                RemoveReorgedBlocksFromStore(expectedStoreTip);

            // Save the batch.
            var newTip = clearedBatch.Last().ChainedHeader;

            this.logger.LogDebug("Saving batch of {0} blocks, total size: {1} bytes.", clearedBatch.Count,
                this.currentBatchSizeBytes);

            this.blockRepository.PutBlocks(new HashHeightPair(newTip), clearedBatch.Select(b => b.Block).ToList());

            SetStoreTip(newTip);
            this.logger.LogDebug("Store tip set to '{0}'.", this.storeTip);
        }

        /// <summary>
        ///     Cleans the batch in a way that all headers from the latest one are consecutive.
        ///     Those that violate consecutiveness are removed.
        /// </summary>
        /// <returns>List of consecutive blocks.</returns>
        List<ChainedHeaderBlock> GetBatchWithoutReorgedBlocks()
        {
            // Initialize current with highest block from the batch.
            var current = this.batch.Last();

            // List of consecutive blocks. It's a cleaned out version of batch that doesn't have blocks that were reorged.
            var batchCleared = new List<ChainedHeaderBlock>(this.batch.Count) {current};

            // Select only those blocks that were not reorged away.
            for (var i = this.batch.Count - 2; i >= 0; i--)
            {
                if (this.batch[i].ChainedHeader.HashBlock != current.ChainedHeader.Previous.HashBlock)
                {
                    this.logger.LogDebug("Block '{0}' removed from the batch because it was reorged.",
                        this.batch[i].ChainedHeader);
                    continue;
                }

                batchCleared.Add(this.batch[i]);
                current = this.batch[i];
            }

            batchCleared.Reverse();

            return batchCleared;
        }

        /// <summary>Removes reorged blocks from the database.</summary>
        /// <param name="expectedStoreTip">Highest block that should be in the store.</param>
        /// <exception cref="DBreeze.Exceptions.DBreezeException">Thrown if an error occurs during database operations.</exception>
        void RemoveReorgedBlocksFromStore(ChainedHeader expectedStoreTip)
        {
            var blocksToDelete = new List<uint256>();
            var currentHeader = this.storeTip;

            while (currentHeader.HashBlock != expectedStoreTip.HashBlock)
            {
                blocksToDelete.Add(currentHeader.HashBlock);

                if (currentHeader.Previous == null)
                    break;

                currentHeader = currentHeader.Previous;
            }

            this.logger.LogDebug("Block store reorg detected. Removing {0} blocks from the database.",
                blocksToDelete.Count);

            this.blockRepository.Delete(new HashHeightPair(currentHeader), blocksToDelete);

            SetStoreTip(expectedStoreTip);
            this.logger.LogDebug("Store tip rewound to '{0}'.", this.storeTip);
        }
    }
}