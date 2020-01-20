using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.AsyncWork;
using UnnamedCoin.Bitcoin.Base.Deployments;
using UnnamedCoin.Bitcoin.Features.Consensus.CoinViews;

namespace UnnamedCoin.Bitcoin.Features.Consensus
{
    /// <summary>
    ///     Pre-fetches coins (UTXOs) for blocks that are likely to be validated in the near future.
    ///     <para>
    ///         This speeds up full block validation.
    ///     </para>
    /// </summary>
    public class CoinviewPrefetcher : IDisposable
    {
        /// <summary>
        ///     How many blocks ahead pre-fetching will look.
        ///     When header at height X is dequeued block with header at height <c>X + Lookahead</c> will be pre-fetching in case
        ///     block data is downloaded.
        /// </summary>
        /// <remarks>
        ///     TODO maybe make it dynamic so the value is increased when we are too close to the tip after pre-fetching was
        ///     completed
        ///     and decreased if we are too far from the tip.
        /// </remarks>
        const int Lookahead = 20;

        readonly IAsyncProvider asyncProvider;

        readonly ChainIndexer chainIndexer;

        readonly ICoinView coinview;

        readonly CoinviewHelper coinviewHelper;

        /// <summary>Queue of headers that were added when block associated with such header was fully validated.</summary>
        readonly IAsyncDelegateDequeuer<ChainedHeader> headersQueue;

        readonly ILogger logger;

        public CoinviewPrefetcher(ICoinView coinview, ChainIndexer chainIndexer, ILoggerFactory loggerFactory,
            IAsyncProvider asyncProvider)
        {
            this.coinview = coinview;
            this.chainIndexer = chainIndexer;
            this.asyncProvider = asyncProvider;

            this.headersQueue =
                asyncProvider.CreateAndRunAsyncDelegateDequeuer<ChainedHeader>(
                    $"{nameof(CoinviewPrefetcher)}-{nameof(this.headersQueue)}", OnHeaderEnqueued);
            this.coinviewHelper = new CoinviewHelper();
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
        }

        public void Dispose()
        {
            this.headersQueue.Dispose();
        }

        /// <summary>
        ///     Pre-fetch UTXOs for a block and some blocks with higher height.
        ///     <para>
        ///         This task runs in the background.
        ///     </para>
        /// </summary>
        /// <param name="header">Header of a block that is about to be partially validated and that requires pre-fetching.</param>
        Task OnHeaderEnqueued(ChainedHeader header, CancellationToken cancellation)
        {
            var currentHeader = header;

            // Go Lookahead blocks ahead of current header and get block for pre-fetching.
            // There might be several blocks at height of header.Height + Lookahead but
            // only first one will be pre-fetched since pre-fetching is in place mostly to
            // speed up IBD.
            for (var i = 0; i < Lookahead; i++)
            {
                if (currentHeader.Next.Count == 0)
                {
                    this.logger.LogTrace("(-)[NO_HEADERS]");
                    return Task.CompletedTask;
                }

                currentHeader = currentHeader.Next.FirstOrDefault();

                if (currentHeader == null)
                {
                    this.logger.LogTrace("(-)[NO_NEXT_HEADER]");
                    return Task.CompletedTask;
                }
            }

            var block = currentHeader.Block;

            if (block == null)
            {
                this.logger.LogTrace("(-)[NO_BLOCK_DATA]");
                return Task.CompletedTask;
            }

            var farFromTip = currentHeader.Height > this.chainIndexer.Tip.Height + Lookahead / 2;

            if (!farFromTip)
            {
                this.logger.LogDebug("Skipping pre-fetch, the block selected is too close to the tip.");
                this.logger.LogTrace("(-)[TOO_CLOSE_TO_PREFETCH_HEIGHT]");
                return Task.CompletedTask;
            }

            var enforceBIP30 = DeploymentFlags.EnforceBIP30ForBlock(currentHeader);
            var idsToFetch = this.coinviewHelper.GetIdsToFetch(block, enforceBIP30);

            if (idsToFetch.Length != 0)
            {
                this.coinview.FetchCoins(idsToFetch, cancellation);

                this.logger.LogDebug("{0} ids were pre-fetched.", idsToFetch.Length);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Pre-fetches UTXOs for a block and some blocks with higher height.
        ///     <para>
        ///         Pre-fetching is done in the background.
        ///     </para>
        /// </summary>
        /// <param name="header">Header of a block that was fully validated and requires pre-fetching.</param>
        public void Prefetch(ChainedHeader header)
        {
            this.headersQueue.Enqueue(header);
        }
    }
}