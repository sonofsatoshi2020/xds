using Microsoft.Extensions.DependencyInjection;
using UnnamedCoin.Bitcoin.Builder;
using UnnamedCoin.Bitcoin.Configuration.Logging;
using UnnamedCoin.Bitcoin.Features.Consensus;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Fee;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Interfaces;
using UnnamedCoin.Bitcoin.Interfaces;

namespace ChainParams.Configuration
{
    public static class MemoryPoolConfiguration
    {
        /// <summary>
        /// Include the memory pool feature and related services in the full node.
        /// </summary>
        /// <param name="fullNodeBuilder">Full node builder.</param>
        /// <returns>Full node builder.</returns>
        public static IFullNodeBuilder UseObsidianXMempool(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<MempoolFeature>("mempool");
            LoggingConfiguration.RegisterFeatureNamespace<BlockPolicyEstimator>("estimatefee");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                .AddFeature<MempoolFeature>()
                .DependOn<ConsensusFeature>()
                .FeatureServices(services =>
                {
                    services.AddSingleton<MempoolSchedulerLock>();
                    services.AddSingleton<ITxMempool, TxMempool>();
                    services.AddSingleton<BlockPolicyEstimator>();
                    services.AddSingleton<IMempoolValidator, MainNetMempoolValidator>();
                    services.AddSingleton<MempoolOrphans>();
                    services.AddSingleton<MempoolManager>()
                        .AddSingleton<IPooledTransaction, MempoolManager>(provider => provider.GetService<MempoolManager>())
                        .AddSingleton<IPooledGetUnspentTransaction, MempoolManager>(provider => provider.GetService<MempoolManager>());
                    services.AddSingleton<MempoolBehavior>();
                    services.AddSingleton<MempoolSignaled>();
                    services.AddSingleton<BlocksDisconnectedSignaled>();
                    services.AddSingleton<IMempoolPersistence, MempoolPersistence>();
                    services.AddSingleton<MempoolSettings>();

                    foreach (var ruleType in fullNodeBuilder.Network.Consensus.MempoolRules)
                        services.AddSingleton(typeof(IMempoolRule), ruleType);
                });
            });

            return fullNodeBuilder;
        }
    }
    
}
