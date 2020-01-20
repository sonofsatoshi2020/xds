using System;
using System.Threading.Tasks;
using ChainParams;
using ChainParams.Configuration;
using NBitcoin.Protocol;
using UnnamedCoin.Bitcoin.Builder;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Features.Api;
using UnnamedCoin.Bitcoin.Features.BlockStore;
using UnnamedCoin.Bitcoin.Features.ColdStaking;
using UnnamedCoin.Bitcoin.Features.Consensus;
using UnnamedCoin.Bitcoin.Features.Miner;
using UnnamedCoin.Bitcoin.Features.RPC;
using UnnamedCoin.Bitcoin.Utilities;

namespace Daemon
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var nodeSettings = new NodeSettings(new MainNet(),
                    protocolVersion: ProtocolVersion.PROVEN_HEADER_VERSION, 
                    args: args);

                var builder = new FullNodeBuilder()
                    .UseNodeSettings(nodeSettings)
                    .UseBlockStore()
                    .UsePosConsensus()
                    .UseObsidianXMempool()
                    .UseColdStakingWallet()
                    .AddPowPosMining()
                    .AddRPC()
                    .UseApi();

                await builder.Build().RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(@"There was a problem initializing the node. Details: '{0}'", ex.Message);
            }
        }
    }
}
