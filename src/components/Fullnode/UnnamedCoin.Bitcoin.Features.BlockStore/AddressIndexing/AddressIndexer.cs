using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.AsyncWork;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Configuration.Logging;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Controllers.Models;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.Primitives;
using UnnamedCoin.Bitcoin.Utilities;
using FileMode = LiteDB.FileMode;
using Script = NBitcoin.Script;

namespace UnnamedCoin.Bitcoin.Features.BlockStore.AddressIndexing
{
    /// <summary>Component that builds an index of all addresses and deposits\withdrawals that happened to\from them.</summary>
    public interface IAddressIndexer : IDisposable
    {
        ChainedHeader IndexerTip { get; }

        void Initialize();

        /// <summary>
        ///     Returns balance of the given address confirmed with at least <paramref name="minConfirmations" />
        ///     confirmations.
        /// </summary>
        /// <param name="addresses">The set of addresses that will be queried.</param>
        /// <param name="minConfirmations">Only blocks below consensus tip less this parameter will be considered.</param>
        /// <returns>Balance of a given address or <c>null</c> if address wasn't indexed or doesn't exists.</returns>
        AddressBalancesResult GetAddressBalances(string[] addresses, int minConfirmations = 0);

        /// <summary>Returns verbose balances data.</summary>
        /// <param name="addresses">The set of addresses that will be queried.</param>
        VerboseAddressBalancesResult GetAddressIndexerState(string[] addresses);
    }

    public class AddressIndexer : IAddressIndexer
    {
        const string DbTipDataKey = "AddrTipData";

        const string AddressIndexerDatabaseFilename = "addressindex.litedb";

        /// <summary>Max supported reorganization length for networks without max reorg property.</summary>
        public const int FallBackMaxReorg = 200;

        /// <summary>
        ///     Time to wait before attempting to index the next block.
        ///     Waiting happens after a failure to get next block to index.
        /// </summary>
        const int DelayTimeMs = 2000;

        const int CompactingThreshold = 50;

        /// <summary>Max distance between consensus and indexer tip to consider indexer synced.</summary>
        const int ConsiderSyncedMaxDistance = 10;

        const int PurgeIntervalSeconds = 60;

        /// <summary>
        ///     This is a window of some blocks that is needed to reduce the consequences of nodes having different view of
        ///     consensus chain.
        ///     We assume that nodes usually don't have view that is different from other nodes by that constant of blocks.
        /// </summary>
        public const int SyncBuffer = 50;

        readonly IAsyncProvider asyncProvider;

        readonly AverageCalculator averageTimePerBlock;

        readonly CancellationTokenSource cancellation;

        readonly ChainIndexer chainIndexer;

        /// <summary>Distance in blocks from consensus tip at which compaction should start.</summary>
        /// <remarks>
        ///     It can't be lower than maxReorg since compacted data can't be converted back to uncompacted state for partial
        ///     reversion.
        /// </remarks>
        readonly int compactionTriggerDistance;

        readonly IConsensusManager consensusManager;

        readonly DataFolder dataFolder;

        readonly IDateTimeProvider dateTimeProvider;

        readonly TimeSpan flushChangesInterval;

        /// <summary>Protects access to <see cref="addressIndexRepository" /> and <see cref="outpointsRepository" />.</summary>
        readonly object lockObject;

        readonly ILogger logger;

        readonly ILoggerFactory loggerFactory;

        readonly Network network;

        readonly INodeStats nodeStats;

        readonly IScriptAddressReader scriptAddressReader;

        readonly StoreSettings storeSettings;

        /// <summary>A mapping between addresses and their balance changes.</summary>
        /// <remarks>All access should be protected by <see cref="lockObject" />.</remarks>
        AddressIndexRepository addressIndexRepository;

        LiteDatabase db;

        Task indexingTask;

        DateTime lastFlushTime;

        /// <summary>Last time rewind data was purged.</summary>
        DateTime lastPurgeTime;

        /// <summary>Indexer height at the last save.</summary>
        /// <remarks>Should be protected by <see cref="lockObject" />.</remarks>
        int lastSavedHeight;

        /// <summary>Script pub keys and amounts mapped by outpoints.</summary>
        /// <remarks>All access should be protected by <see cref="lockObject" />.</remarks>
        AddressIndexerOutpointsRepository outpointsRepository;

        Task<ChainedHeaderBlock> prefetchingTask;

        LiteCollection<AddressIndexerTipData> tipDataStore;

        public AddressIndexer(StoreSettings storeSettings, DataFolder dataFolder, ILoggerFactory loggerFactory,
            Network network, INodeStats nodeStats,
            IConsensusManager consensusManager, IAsyncProvider asyncProvider, ChainIndexer chainIndexer,
            IDateTimeProvider dateTimeProvider)
        {
            this.storeSettings = storeSettings;
            this.network = network;
            this.nodeStats = nodeStats;
            this.dataFolder = dataFolder;
            this.consensusManager = consensusManager;
            this.asyncProvider = asyncProvider;
            this.dateTimeProvider = dateTimeProvider;
            this.loggerFactory = loggerFactory;
            this.scriptAddressReader = new ScriptAddressReader();

            this.lockObject = new object();
            this.flushChangesInterval = TimeSpan.FromMinutes(2);
            this.lastFlushTime = this.dateTimeProvider.GetUtcNow();
            this.cancellation = new CancellationTokenSource();
            this.chainIndexer = chainIndexer;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);

            this.averageTimePerBlock = new AverageCalculator(200);
            var maxReorgLength = GetMaxReorgOrFallbackMaxReorg(this.network);

            this.compactionTriggerDistance = maxReorgLength * 2 + SyncBuffer + 1000;
        }

        public ChainedHeader IndexerTip { get; private set; }

        public void Initialize()
        {
            // The transaction index is needed in the event of a reorg.
            if (!this.storeSettings.AddressIndex)
            {
                this.logger.LogTrace("(-)[DISABLED]");
                return;
            }

            var dbPath = Path.Combine(this.dataFolder.RootPath, AddressIndexerDatabaseFilename);

            var fileMode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? FileMode.Exclusive : FileMode.Shared;
            this.db = new LiteDatabase(new ConnectionString {Filename = dbPath, Mode = fileMode});

            this.addressIndexRepository = new AddressIndexRepository(this.db, this.loggerFactory);

            this.logger.LogDebug("Address indexing is enabled.");

            this.tipDataStore = this.db.GetCollection<AddressIndexerTipData>(DbTipDataKey);

            lock (this.lockObject)
            {
                var tipData = this.tipDataStore.FindAll().FirstOrDefault();

                this.logger.LogDebug("Tip data: '{0}'.", tipData == null ? "null" : tipData.ToString());

                this.IndexerTip = tipData == null
                    ? this.chainIndexer.Genesis
                    : this.consensusManager.Tip.FindAncestorOrSelf(new uint256(tipData.TipHashBytes));

                if (this.IndexerTip == null)
                {
                    // This can happen if block hash from tip data is no longer a part of the consensus chain and node was killed in the middle of a reorg.
                    var rewindAmount = this.compactionTriggerDistance / 2;

                    if (rewindAmount > this.consensusManager.Tip.Height)
                        this.IndexerTip = this.chainIndexer.Genesis;
                    else
                        this.IndexerTip =
                            this.consensusManager.Tip.GetAncestor(this.consensusManager.Tip.Height - rewindAmount);
                }
            }

            this.outpointsRepository = new AddressIndexerOutpointsRepository(this.db, this.loggerFactory);

            RewindAndSave(this.IndexerTip);

            this.logger.LogDebug("Indexer initialized at '{0}'.", this.IndexerTip);

            this.indexingTask = Task.Run(async () => await IndexAddressesContinuouslyAsync().ConfigureAwait(false));

            this.asyncProvider.RegisterTask($"{nameof(AddressIndexer)}.{nameof(this.indexingTask)}", this.indexingTask);

            this.nodeStats.RegisterStats(AddInlineStats, StatsType.Inline, GetType().Name, 400);
        }

        /// <inheritdoc />
        /// <remarks>This is currently not in use but will be required for exchange integration.</remarks>
        public AddressBalancesResult GetAddressBalances(string[] addresses, int minConfirmations = 1)
        {
            var (isQueryable, reason) = IsQueryable();

            if (!isQueryable)
                return AddressBalancesResult.RequestFailed(reason);

            var result = new AddressBalancesResult();

            lock (this.lockObject)
            {
                foreach (var address in addresses)
                {
                    var indexData = this.addressIndexRepository.GetOrCreateAddress(address);

                    var maxAllowedHeight = this.consensusManager.Tip.Height - minConfirmations + 1;

                    var balance = indexData.BalanceChanges.Where(x => x.BalanceChangedHeight <= maxAllowedHeight)
                        .CalculateBalance();

                    this.logger.LogDebug("Address: {0}, balance: {1}.", address, balance);
                    result.Balances.Add(new AddressBalanceResult(address, new Money(balance)));
                }

                return result;
            }
        }

        /// <inheritdoc />
        public VerboseAddressBalancesResult GetAddressIndexerState(string[] addresses)
        {
            var result = new VerboseAddressBalancesResult(this.consensusManager.Tip.Height);

            if (addresses.Length == 0)
                return result;

            var (isQueryable, reason) = IsQueryable();

            if (!isQueryable)
                return VerboseAddressBalancesResult.RequestFailed(reason);

            lock (this.lockObject)
            {
                foreach (var address in addresses)
                {
                    var indexData = this.addressIndexRepository.GetOrCreateAddress(address);

                    var copy = new AddressIndexerData
                    {
                        Address = indexData.Address,
                        BalanceChanges = new List<AddressBalanceChange>(indexData.BalanceChanges)
                    };

                    result.BalancesData.Add(copy);
                }
            }

            return result;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.cancellation.Cancel();

            this.indexingTask?.GetAwaiter().GetResult();

            this.db?.Dispose();
        }

        /// <summary>Returns maxReorg of <see cref="FallBackMaxReorg" /> in case maxReorg is <c>0</c>.</summary>
        public static int GetMaxReorgOrFallbackMaxReorg(Network network)
        {
            var maxReorgLength = network.Consensus.MaxReorgLength == 0
                ? FallBackMaxReorg
                : (int) network.Consensus.MaxReorgLength;

            return maxReorgLength;
        }

        async Task IndexAddressesContinuouslyAsync()
        {
            var watch = Stopwatch.StartNew();

            while (!this.cancellation.IsCancellationRequested)
            {
                if (this.dateTimeProvider.GetUtcNow() - this.lastFlushTime > this.flushChangesInterval)
                {
                    this.logger.LogDebug("Flushing changes.");

                    SaveAll();

                    this.lastFlushTime = this.dateTimeProvider.GetUtcNow();

                    this.logger.LogDebug("Flush completed.");
                }

                if (this.cancellation.IsCancellationRequested)
                    break;

                var nextHeader = this.consensusManager.Tip.GetAncestor(this.IndexerTip.Height + 1);

                if (nextHeader == null)
                {
                    this.logger.LogDebug("Next header wasn't found. Waiting.");

                    try
                    {
                        await Task.Delay(DelayTimeMs, this.cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    continue;
                }

                if (nextHeader.Previous.HashBlock != this.IndexerTip.HashBlock)
                {
                    var lastCommonHeader = nextHeader.FindFork(this.IndexerTip);

                    this.logger.LogDebug("Reorganization detected. Rewinding till '{0}'.", lastCommonHeader);

                    RewindAndSave(lastCommonHeader);

                    continue;
                }

                // First try to see if it's prefetched.
                var prefetchedBlock = this.prefetchingTask == null
                    ? null
                    : await this.prefetchingTask.ConfigureAwait(false);

                Block blockToProcess;

                if (prefetchedBlock != null && prefetchedBlock.ChainedHeader == nextHeader)
                    blockToProcess = prefetchedBlock.Block;
                else
                    blockToProcess = this.consensusManager.GetBlockData(nextHeader.HashBlock).Block;

                if (blockToProcess == null)
                {
                    this.logger.LogDebug("Next block wasn't found. Waiting.");

                    try
                    {
                        await Task.Delay(DelayTimeMs, this.cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    continue;
                }

                // Schedule prefetching of the next block;
                var headerToPrefetch = this.consensusManager.Tip.GetAncestor(nextHeader.Height + 1);

                if (headerToPrefetch != null)
                    this.prefetchingTask =
                        Task.Run(() => this.consensusManager.GetBlockData(headerToPrefetch.HashBlock));

                watch.Restart();

                var success = ProcessBlock(blockToProcess, nextHeader);

                watch.Stop();
                this.averageTimePerBlock.AddSample(watch.Elapsed.TotalMilliseconds);

                if (!success)
                {
                    this.logger.LogDebug("Failed to process next block. Waiting.");

                    try
                    {
                        await Task.Delay(DelayTimeMs, this.cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    continue;
                }

                this.IndexerTip = nextHeader;
            }

            SaveAll();
        }

        void RewindAndSave(ChainedHeader rewindToHeader)
        {
            lock (this.lockObject)
            {
                // The cache doesn't really lend itself to handling a reorg very well.
                // Therefore, we leverage LiteDb's indexing capabilities to tell us
                // which records are for the affected blocks.

                var affectedAddresses = this.addressIndexRepository.GetAddressesHigherThanHeight(rewindToHeader.Height);

                foreach (var address in affectedAddresses)
                {
                    var indexData = this.addressIndexRepository.GetOrCreateAddress(address);
                    indexData.BalanceChanges.RemoveAll(x => x.BalanceChangedHeight > rewindToHeader.Height);
                }

                this.logger.LogDebug("Rewinding changes for {0} addresses.", affectedAddresses.Count);

                // Rewind all the way back to the fork point.
                this.outpointsRepository.RewindDataAboveHeight(rewindToHeader.Height);

                this.IndexerTip = rewindToHeader;

                SaveAll();
            }
        }

        void SaveAll()
        {
            this.logger.LogDebug("Saving address indexer.");

            lock (this.lockObject)
            {
                this.addressIndexRepository.SaveAllItems();
                this.outpointsRepository.SaveAllItems();

                var tipData = this.tipDataStore.FindAll().FirstOrDefault();

                if (tipData == null)
                    tipData = new AddressIndexerTipData();

                tipData.Height = this.IndexerTip.Height;
                tipData.TipHashBytes = this.IndexerTip.HashBlock.ToBytes();

                this.tipDataStore.Upsert(tipData);
                this.lastSavedHeight = this.IndexerTip.Height;
            }

            this.logger.LogDebug("Address indexer saved.");
        }

        void AddInlineStats(StringBuilder benchLog)
        {
            benchLog.AppendLine("AddressIndexer.Height: ".PadRight(LoggingConfiguration.ColumnLength + 1) +
                                this.IndexerTip.Height.ToString().PadRight(9) +
                                "AddressCache%: " +
                                this.addressIndexRepository.GetLoadPercentage().ToString().PadRight(8) +
                                "OutPointCache%: " +
                                this.outpointsRepository.GetLoadPercentage().ToString().PadRight(8) +
                                $"Ms/block: {Math.Round(this.averageTimePerBlock.Average, 2)}");
        }

        /// <summary>Processes a block that was added or removed from the consensus chain.</summary>
        /// <param name="block">The block to process.</param>
        /// <param name="header">The chained header associated to the block being processed.</param>
        /// <returns><c>true</c> if block was sucessfully processed.</returns>
        bool ProcessBlock(Block block, ChainedHeader header)
        {
            lock (this.lockObject)
            {
                // Record outpoints.
                foreach (var tx in block.Transactions)
                    for (var i = 0; i < tx.Outputs.Count; i++)
                    {
                        // OP_RETURN outputs and empty outputs cannot be spent and therefore do not need to be put into the cache.
                        if (tx.Outputs[i].IsEmpty || tx.Outputs[i].ScriptPubKey.IsUnspendable)
                            continue;

                        var outPoint = new OutPoint(tx, i);

                        var outPointData = new OutPointData
                        {
                            Outpoint = outPoint.ToString(),
                            ScriptPubKeyBytes = tx.Outputs[i].ScriptPubKey.ToBytes(),
                            Money = tx.Outputs[i].Value
                        };

                        // TODO: When the outpoint cache is full, adding outpoints singly causes overhead writing evicted entries out to the repository
                        this.outpointsRepository.AddOutPointData(outPointData);
                    }
            }

            // Process inputs.
            var inputs = new List<TxIn>();

            // Collect all inputs excluding coinbases.
            foreach (var inputsCollection in block.Transactions.Where(x => !x.IsCoinBase).Select(x => x.Inputs))
                inputs.AddRange(inputsCollection);

            lock (this.lockObject)
            {
                var rewindData = new AddressIndexerRewindData
                {
                    BlockHash = header.HashBlock.ToString(), BlockHeight = header.Height,
                    SpentOutputs = new List<OutPointData>()
                };

                foreach (var input in inputs)
                {
                    var consumedOutput = input.PrevOut;

                    if (!this.outpointsRepository.TryGetOutPointData(consumedOutput, out var consumedOutputData))
                    {
                        this.logger.LogError("Missing outpoint data for {0}.", consumedOutput);
                        this.logger.LogTrace("(-)[MISSING_OUTPOINTS_DATA]");
                        throw new Exception($"Missing outpoint data for {consumedOutput}");
                    }

                    Money amountSpent = consumedOutputData.Money;

                    rewindData.SpentOutputs.Add(consumedOutputData);

                    // Transactions that don't actually change the balance just bloat the database.
                    if (amountSpent == 0)
                        continue;

                    var address = this.scriptAddressReader.GetAddressFromScriptPubKey(this.network,
                        new Script(consumedOutputData.ScriptPubKeyBytes));

                    if (string.IsNullOrEmpty(address))
                        // This condition need not be logged, as the address reader should be aware of all possible address formats already.
                        continue;

                    ProcessBalanceChangeLocked(header.Height, address, amountSpent, false);
                }

                // Process outputs.
                foreach (var tx in block.Transactions)
                foreach (var txOut in tx.Outputs)
                {
                    var amountReceived = txOut.Value;

                    // Transactions that don't actually change the balance just bloat the database.
                    if (amountReceived == 0 || txOut.IsEmpty || txOut.ScriptPubKey.IsUnspendable)
                        continue;

                    var address = this.scriptAddressReader.GetAddressFromScriptPubKey(this.network, txOut.ScriptPubKey);

                    if (string.IsNullOrEmpty(address))
                        // This condition need not be logged, as the address reader should be aware of all
                        // possible address formats already.
                        continue;

                    ProcessBalanceChangeLocked(header.Height, address, amountReceived, true);
                }

                this.outpointsRepository.RecordRewindData(rewindData);

                var purgeRewindDataThreshold =
                    Math.Min(this.consensusManager.Tip.Height - this.compactionTriggerDistance, this.lastSavedHeight);

                if ((this.dateTimeProvider.GetUtcNow() - this.lastPurgeTime).TotalSeconds > PurgeIntervalSeconds)
                {
                    this.outpointsRepository.PurgeOldRewindData(purgeRewindDataThreshold);
                    this.lastPurgeTime = this.dateTimeProvider.GetUtcNow();
                }

                // Remove outpoints that were consumed.
                foreach (var consumedOutPoint in inputs.Select(x => x.PrevOut))
                    this.outpointsRepository.RemoveOutPointData(consumedOutPoint);
            }

            return true;
        }

        /// <summary>Adds a new balance change entry to to the <see cref="addressIndexRepository" />.</summary>
        /// <param name="height">The height of the block this being processed.</param>
        /// <param name="address">The address receiving the funds.</param>
        /// <param name="amount">The amount being received.</param>
        /// <param name="deposited"><c>false</c> if this is an output being spent, <c>true</c> otherwise.</param>
        /// <remarks>Should be protected by <see cref="lockObject" />.</remarks>
        void ProcessBalanceChangeLocked(int height, string address, Money amount, bool deposited)
        {
            var indexData = this.addressIndexRepository.GetOrCreateAddress(address);

            // Record new balance change into the address index data.
            indexData.BalanceChanges.Add(new AddressBalanceChange
            {
                BalanceChangedHeight = height,
                Satoshi = amount.Satoshi,
                Deposited = deposited
            });

            // Anything less than that should be compacted.
            var heightThreshold = this.consensusManager.Tip.Height - this.compactionTriggerDistance;

            var compact = indexData.BalanceChanges.Count > CompactingThreshold &&
                          indexData.BalanceChanges[1].BalanceChangedHeight < heightThreshold;

            if (!compact)
            {
                this.logger.LogTrace("(-)[TOO_FEW_CHANGE_RECORDS]");
                return;
            }

            var compacted = new List<AddressBalanceChange>(CompactingThreshold / 2)
            {
                new AddressBalanceChange
                {
                    BalanceChangedHeight = 0,
                    Satoshi = 0,
                    Deposited = true
                }
            };

            foreach (var change in indexData.BalanceChanges)
                if (change.BalanceChangedHeight < heightThreshold)
                {
                    this.logger.LogDebug("Balance change: {0} was selected for compaction. Compacted balance now: {1}.",
                        change, compacted[0].Satoshi);

                    if (change.Deposited)
                        compacted[0].Satoshi += change.Satoshi;
                    else
                        compacted[0].Satoshi -= change.Satoshi;

                    this.logger.LogDebug("New compacted balance: {0}.", compacted[0].Satoshi);
                }
                else
                {
                    compacted.Add(change);
                }

            indexData.BalanceChanges = compacted;
            this.addressIndexRepository.AddOrUpdate(indexData.Address, indexData, indexData.BalanceChanges.Count + 1);
        }

        bool IsSynced()
        {
            lock (this.lockObject)
            {
                return this.consensusManager.Tip.Height - this.IndexerTip.Height <= ConsiderSyncedMaxDistance;
            }
        }

        (bool isQueryable, string reason) IsQueryable()
        {
            if (this.addressIndexRepository == null)
            {
                this.logger.LogTrace("(-)[NOT_INITIALIZED]");
                return (false, "Address indexer is not initialized.");
            }

            if (!IsSynced())
            {
                this.logger.LogTrace("(-)[NOT_SYNCED]");
                return (false, "Address indexer is not synced.");
            }

            return (true, string.Empty);
        }
    }
}