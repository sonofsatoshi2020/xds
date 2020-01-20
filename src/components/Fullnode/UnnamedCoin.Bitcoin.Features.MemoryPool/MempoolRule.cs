using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Interfaces;

namespace UnnamedCoin.Bitcoin.Features.MemoryPool
{
    public abstract class MempoolRule : IMempoolRule
    {
        protected readonly ChainIndexer chainIndexer;

        protected readonly ILogger logger;

        protected readonly ITxMempool mempool;
        protected readonly Network network;

        protected readonly MempoolSettings settings;

        protected MempoolRule(Network network,
            ITxMempool mempool,
            MempoolSettings settings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory)
        {
            this.network = network;
            this.mempool = mempool;
            this.settings = settings;
            this.chainIndexer = chainIndexer;

            this.logger = loggerFactory.CreateLogger(GetType().FullName);
        }

        /// <inheritdoc />
        public abstract void CheckTransaction(MempoolValidationContext context);
    }
}