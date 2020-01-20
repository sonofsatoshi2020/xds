using NBitcoin;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Base.Deployments
{
    public class NodeDeployments
    {
        /// <summary>Thread safe access to the best chain of block headers (that the node is aware of) from genesis.</summary>
        readonly ChainIndexer chainIndexer;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        readonly Network network;

        public NodeDeployments(Network network, ChainIndexer chainIndexer)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(chainIndexer, nameof(chainIndexer));

            this.network = network;
            this.chainIndexer = chainIndexer;
            this.BIP9 = new ThresholdConditionCache(network.Consensus);
        }

        public ThresholdConditionCache BIP9 { get; }

        public virtual DeploymentFlags GetFlags(ChainedHeader block)
        {
            Guard.NotNull(block, nameof(block));

            lock (this.BIP9)
            {
                var states = this.BIP9.GetStates(block.Previous);
                var flags = new DeploymentFlags(block, states, this.network.Consensus, this.chainIndexer);
                return flags;
            }
        }
    }
}