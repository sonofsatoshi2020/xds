using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Utilities;
using Transaction = DBreeze.Transactions.Transaction;

namespace UnnamedCoin.Bitcoin.Features.BlockStore.Pruning
{
    /// <inheritdoc />
    public class PrunedBlockRepository : IPrunedBlockRepository
    {
        static readonly byte[] prunedTipKey = new byte[2];
        readonly IBlockRepository blockRepository;
        readonly DBreezeSerializer dBreezeSerializer;
        readonly ILogger logger;
        readonly StoreSettings storeSettings;

        public PrunedBlockRepository(IBlockRepository blockRepository, DBreezeSerializer dBreezeSerializer,
            ILoggerFactory loggerFactory, StoreSettings storeSettings)
        {
            this.blockRepository = blockRepository;
            this.dBreezeSerializer = dBreezeSerializer;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.storeSettings = storeSettings;
        }

        /// <inheritdoc />
        public HashHeightPair PrunedTip { get; private set; }

        /// <inheritdoc />
        public void Initialize()
        {
            using (var transaction = this.blockRepository.DBreeze.GetTransaction())
            {
                LoadPrunedTip(transaction);
            }
        }

        /// <inheritdoc />
        public void PruneAndCompactDatabase(ChainedHeader blockRepositoryTip, Network network, bool nodeInitializing)
        {
            this.logger.LogInformation("Pruning started...");

            if (this.PrunedTip == null)
            {
                var genesis = network.GetGenesis();

                this.PrunedTip = new HashHeightPair(genesis.GetHash(), 0);

                using (var transaction = this.blockRepository.DBreeze.GetTransaction())
                {
                    transaction.Insert(BlockRepository.CommonTableName, prunedTipKey,
                        this.dBreezeSerializer.Serialize(this.PrunedTip));
                    transaction.Commit();
                }
            }

            if (nodeInitializing)
            {
                if (IsDatabasePruned())
                    return;

                PrepareDatabaseForCompacting(blockRepositoryTip);
            }

            CompactDataBase();

            this.logger.LogInformation("Pruning complete.");
        }

        /// <inheritdoc />
        public void UpdatePrunedTip(ChainedHeader tip)
        {
            this.PrunedTip = new HashHeightPair(tip);
        }

        bool IsDatabasePruned()
        {
            if (this.blockRepository.TipHashAndHeight.Height <=
                this.PrunedTip.Height + this.storeSettings.AmountOfBlocksToKeep)
            {
                this.logger.LogDebug("(-):true");
                return true;
            }

            this.logger.LogDebug("(-):false");
            return false;
        }

        /// <summary>
        ///     Compacts the block and transaction database by recreating the tables without the deleted references.
        /// </summary>
        /// <param name="blockRepositoryTip">The last fully validated block of the node.</param>
        void PrepareDatabaseForCompacting(ChainedHeader blockRepositoryTip)
        {
            var upperHeight = this.blockRepository.TipHashAndHeight.Height - this.storeSettings.AmountOfBlocksToKeep;

            var toDelete = new List<ChainedHeader>();

            var startFromHeader = blockRepositoryTip.GetAncestor(upperHeight);
            var endAtHeader = blockRepositoryTip.FindAncestorOrSelf(this.PrunedTip.Hash);

            this.logger.LogInformation($"Pruning blocks from height {upperHeight} to {endAtHeader.Height}.");

            while (startFromHeader.Previous != null && startFromHeader != endAtHeader)
            {
                toDelete.Add(startFromHeader);
                startFromHeader = startFromHeader.Previous;
            }

            this.blockRepository.DeleteBlocks(toDelete.Select(cb => cb.HashBlock).ToList());

            UpdatePrunedTip(blockRepositoryTip.GetAncestor(upperHeight));
        }

        void LoadPrunedTip(Transaction dbreezeTransaction)
        {
            if (this.PrunedTip == null)
            {
                dbreezeTransaction.ValuesLazyLoadingIsOn = false;

                var row = dbreezeTransaction.Select<byte[], byte[]>(BlockRepository.CommonTableName, prunedTipKey);
                if (row.Exists)
                    this.PrunedTip = this.dBreezeSerializer.Deserialize<HashHeightPair>(row.Value);

                dbreezeTransaction.ValuesLazyLoadingIsOn = true;
            }
        }

        /// <summary>
        ///     Compacts the block and transaction database by recreating the tables without the deleted references.
        /// </summary>
        void CompactDataBase()
        {
            var task = Task.Run(() =>
            {
                using (var dbreezeTransaction = this.blockRepository.DBreeze.GetTransaction())
                {
                    dbreezeTransaction.SynchronizeTables(BlockRepository.BlockTableName,
                        BlockRepository.TransactionTableName);

                    var tempBlocks =
                        dbreezeTransaction.SelectDictionary<byte[], byte[]>(BlockRepository.BlockTableName);

                    if (tempBlocks.Count != 0)
                    {
                        this.logger.LogInformation($"{tempBlocks.Count} blocks will be copied to the pruned table.");

                        dbreezeTransaction.RemoveAllKeys(BlockRepository.BlockTableName, true);
                        dbreezeTransaction.InsertDictionary(BlockRepository.BlockTableName, tempBlocks, false);

                        var tempTransactions =
                            dbreezeTransaction.SelectDictionary<byte[], byte[]>(BlockRepository.TransactionTableName);
                        if (tempTransactions.Count != 0)
                        {
                            this.logger.LogInformation(
                                $"{tempTransactions.Count} transactions will be copied to the pruned table.");
                            dbreezeTransaction.RemoveAllKeys(BlockRepository.TransactionTableName, true);
                            dbreezeTransaction.InsertDictionary(BlockRepository.TransactionTableName, tempTransactions,
                                false);
                        }

                        // Save the hash and height of where the node was pruned up to.
                        dbreezeTransaction.Insert(BlockRepository.CommonTableName, prunedTipKey,
                            this.dBreezeSerializer.Serialize(this.PrunedTip));
                    }

                    dbreezeTransaction.Commit();
                }

                return Task.CompletedTask;
            });
        }
    }
}