using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DBreeze;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Base
{
    public interface IChainRepository : IDisposable
    {
        /// <summary>Loads the chain of headers from the database.</summary>
        /// <returns>Tip of the loaded chain.</returns>
        Task<ChainedHeader> LoadAsync(ChainedHeader genesisHeader);

        /// <summary>Persists chain of headers to the database.</summary>
        Task SaveAsync(ChainIndexer chainIndexer);
    }

    public class ChainRepository : IChainRepository
    {
        /// <summary>Access to DBreeze database.</summary>
        readonly DBreezeEngine dbreeze;

        readonly DBreezeSerializer dBreezeSerializer;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        BlockLocator locator;

        public ChainRepository(string folder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer)
        {
            this.dBreezeSerializer = dBreezeSerializer;
            Guard.NotEmpty(folder, nameof(folder));
            Guard.NotNull(loggerFactory, nameof(loggerFactory));

            this.logger = loggerFactory.CreateLogger(GetType().FullName);

            Directory.CreateDirectory(folder);
            this.dbreeze = new DBreezeEngine(folder);
        }

        public ChainRepository(DataFolder dataFolder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer)
            : this(dataFolder.ChainPath, loggerFactory, dBreezeSerializer)
        {
        }

        /// <inheritdoc />
        public Task<ChainedHeader> LoadAsync(ChainedHeader genesisHeader)
        {
            var task = Task.Run(() =>
            {
                using (var transaction = this.dbreeze.GetTransaction())
                {
                    transaction.ValuesLazyLoadingIsOn = false;
                    ChainedHeader tip = null;
                    var firstRow = transaction.Select<int, byte[]>("Chain", 0);

                    if (!firstRow.Exists)
                        return genesisHeader;

                    var previousHeader = this.dBreezeSerializer.Deserialize<BlockHeader>(firstRow.Value);
                    Guard.Assert(previousHeader.GetHash() == genesisHeader.HashBlock); // can't swap networks

                    foreach (var row in transaction.SelectForwardSkip<int, byte[]>("Chain", 1))
                    {
                        if (tip != null && previousHeader.HashPrevBlock != tip.HashBlock)
                            break;

                        var blockHeader = this.dBreezeSerializer.Deserialize<BlockHeader>(row.Value);
                        tip = new ChainedHeader(previousHeader, blockHeader.HashPrevBlock, tip);
                        previousHeader = blockHeader;
                    }

                    if (previousHeader != null)
                        tip = new ChainedHeader(previousHeader, previousHeader.GetHash(), tip);

                    if (tip == null)
                        tip = genesisHeader;

                    this.locator = tip.GetLocator();
                    return tip;
                }
            });

            return task;
        }

        /// <inheritdoc />
        public Task SaveAsync(ChainIndexer chainIndexer)
        {
            Guard.NotNull(chainIndexer, nameof(chainIndexer));

            var task = Task.Run(() =>
            {
                using (var transaction = this.dbreeze.GetTransaction())
                {
                    var fork = this.locator == null ? null : chainIndexer.FindFork(this.locator);
                    var tip = chainIndexer.Tip;
                    var toSave = tip;

                    var headers = new List<ChainedHeader>();
                    while (toSave != fork)
                    {
                        headers.Add(toSave);
                        toSave = toSave.Previous;
                    }

                    // DBreeze is faster on ordered insert.
                    var orderedChainedHeaders = headers.OrderBy(b => b.Height);
                    foreach (var block in orderedChainedHeaders)
                    {
                        var header = block.Header;
                        if (header is ProvenBlockHeader)
                        {
                            // copy the header parameters, untill we dont make PH a normal header we store it in its own repo.
                            var newHeader = chainIndexer.Network.Consensus.ConsensusFactory.CreateBlockHeader();
                            newHeader.Bits = header.Bits;
                            newHeader.Time = header.Time;
                            newHeader.Nonce = header.Nonce;
                            newHeader.Version = header.Version;
                            newHeader.HashMerkleRoot = header.HashMerkleRoot;
                            newHeader.HashPrevBlock = header.HashPrevBlock;

                            header = newHeader;
                        }

                        transaction.Insert("Chain", block.Height, this.dBreezeSerializer.Serialize(header));
                    }

                    this.locator = tip.GetLocator();
                    transaction.Commit();
                }
            });

            return task;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.dbreeze?.Dispose();
        }
    }
}