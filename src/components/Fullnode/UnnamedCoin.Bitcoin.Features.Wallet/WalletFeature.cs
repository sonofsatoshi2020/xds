using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBitcoin.Policy;
using UnnamedCoin.Bitcoin.Builder;
using UnnamedCoin.Bitcoin.Builder.Feature;
using UnnamedCoin.Bitcoin.Configuration.Logging;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Features.BlockStore;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.RPC;
using UnnamedCoin.Bitcoin.Features.Wallet.Broadcasting;
using UnnamedCoin.Bitcoin.Features.Wallet.Interfaces;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.Signals;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Wallet
{
    /// <summary>
    ///     Common base class for any feature replacing the <see cref="WalletFeature" />.
    /// </summary>
    public abstract class BaseWalletFeature : FullNodeFeature
    {
    }

    /// <summary>
    ///     Wallet feature for the full node.
    /// </summary>
    /// <seealso cref="FullNodeFeature" />
    public class WalletFeature : BaseWalletFeature
    {
        readonly IAddressBookManager addressBookManager;

        readonly BroadcasterBehavior broadcasterBehavior;

        readonly IConnectionManager connectionManager;

        readonly ISignals signals;

        readonly IWalletManager walletManager;
        readonly IWalletSyncManager walletSyncManager;

        /// <summary>
        ///     Initializes a new instance of the <see cref="WalletFeature" /> class.
        /// </summary>
        /// <param name="walletSyncManager">
        ///     The synchronization manager for the wallet, tasked with keeping the wallet synced with
        ///     the network.
        /// </param>
        /// <param name="walletManager">The wallet manager.</param>
        /// <param name="addressBookManager">The address book manager.</param>
        /// <param name="signals">The signals responsible for receiving blocks and transactions from the network.</param>
        /// <param name="connectionManager">The connection manager.</param>
        /// <param name="broadcasterBehavior">The broadcaster behavior.</param>
        public WalletFeature(
            IWalletSyncManager walletSyncManager,
            IWalletManager walletManager,
            IAddressBookManager addressBookManager,
            ISignals signals,
            IConnectionManager connectionManager,
            BroadcasterBehavior broadcasterBehavior,
            INodeStats nodeStats)
        {
            this.walletSyncManager = walletSyncManager;
            this.walletManager = walletManager;
            this.addressBookManager = addressBookManager;
            this.signals = signals;
            this.connectionManager = connectionManager;
            this.broadcasterBehavior = broadcasterBehavior;

            nodeStats.RegisterStats(AddComponentStats, StatsType.Component, GetType().Name);
            nodeStats.RegisterStats(AddInlineStats, StatsType.Inline, GetType().Name, 800);
        }

        /// <summary>
        ///     Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            WalletSettings.PrintHelp(network);
        }

        /// <summary>
        ///     Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            WalletSettings.BuildDefaultConfigurationFile(builder, network);
        }

        void AddInlineStats(StringBuilder log)
        {
            var walletManager = this.walletManager as WalletManager;

            if (walletManager != null)
            {
                var hashHeightPair = walletManager.LastReceivedBlockInfo();

                log.AppendLine("Wallet.Height: ".PadRight(LoggingConfiguration.ColumnLength + 1) +
                               (walletManager.ContainsWallets
                                   ? hashHeightPair.Height.ToString().PadRight(8)
                                   : "No Wallet".PadRight(8)) +
                               (walletManager.ContainsWallets
                                   ? " Wallet.Hash: ".PadRight(LoggingConfiguration.ColumnLength - 1) +
                                     hashHeightPair.Hash
                                   : string.Empty));
            }
        }

        void AddComponentStats(StringBuilder log)
        {
            var walletNames = this.walletManager.GetWalletsNames();

            if (walletNames.Any())
            {
                log.AppendLine();
                log.AppendLine("======Wallets======");

                foreach (var walletName in walletNames)
                foreach (var account in this.walletManager.GetAccounts(walletName))
                {
                    var accountBalance = this.walletManager.GetBalances(walletName, account.Name).Single();
                    log.AppendLine(
                        ($"{walletName}/{account.Name}" + ",").PadRight(LoggingConfiguration.ColumnLength + 10)
                        + (" Confirmed balance: " + accountBalance.AmountConfirmed.ToString()).PadRight(
                            LoggingConfiguration.ColumnLength + 20)
                        + " Unconfirmed balance: " + accountBalance.AmountUnconfirmed.ToString());
                }
            }
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            this.walletManager.Start();
            this.walletSyncManager.Start();
            this.addressBookManager.Initialize();

            this.connectionManager.Parameters.TemplateBehaviors.Add(this.broadcasterBehavior);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            this.walletManager.Stop();
            this.walletSyncManager.Stop();
        }
    }

    /// <summary>
    ///     A class providing extension methods for <see cref="IFullNodeBuilder" />.
    /// </summary>
    public static class FullNodeBuilderWalletExtension
    {
        public static IFullNodeBuilder UseWallet(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<WalletFeature>("wallet");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<WalletFeature>()
                    .DependOn<MempoolFeature>()
                    .DependOn<BlockStoreFeature>()
                    .DependOn<RPCFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<IWalletSyncManager, WalletSyncManager>();
                        services.AddSingleton<IWalletTransactionHandler, WalletTransactionHandler>();
                        services.AddSingleton<IWalletManager, WalletManager>();
                        services.AddSingleton<IWalletFeePolicy, WalletFeePolicy>();
                        services.AddSingleton<IBroadcasterManager, FullNodeBroadcasterManager>();
                        services.AddSingleton<BroadcasterBehavior>();
                        services.AddSingleton<WalletSettings>();
                        services.AddSingleton<IScriptAddressReader>(new ScriptAddressReader());
                        services.AddSingleton<StandardTransactionPolicy>();
                        services.AddSingleton<IAddressBookManager, AddressBookManager>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}