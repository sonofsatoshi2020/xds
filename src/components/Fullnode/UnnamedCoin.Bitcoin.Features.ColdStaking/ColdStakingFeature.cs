using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Builder;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Configuration.Logging;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.Features.BlockStore;
using UnnamedCoin.Bitcoin.Features.MemoryPool;
using UnnamedCoin.Bitcoin.Features.RPC;
using UnnamedCoin.Bitcoin.Features.Wallet;
using UnnamedCoin.Bitcoin.Features.Wallet.Broadcasting;
using UnnamedCoin.Bitcoin.Features.Wallet.Interfaces;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.ColdStaking
{
    /// <summary>
    ///     Feature for cold staking which eliminates the need to keep the coins in the hot wallet.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In order to produce blocks , a miner has to be online with running
    ///         node and have its wallet open. This is necessary because at each time slot, the miner is
    ///         supposed to check whether one of its UTXOs is eligible to be used as so-called coinstake kernel
    ///         input and if so, it needs to use the private key associated with this UTXO in order to produce
    ///         the coinstake transaction.
    ///     </para>
    ///     <para>
    ///         The chance of a UTXO being eligible for producing a coinstake transaction grows linearly
    ///         with the number of coins that the UTXO presents. This implies that the biggest miners on the
    ///         network are required to keep the coins in a hot wallet. This is dangerous in case the machine
    ///         where the hot wallet runs is compromised.
    ///     </para>
    ///     <para>
    ///         Cold staking is a mechanism that eliminates the need to keep the coins in the hot wallet.
    ///         With cold staking implemented, the miner still needs to be online and running a node with an open
    ///         wallet, but the coins that are used for staking can be safely stored in cold storage. Therefore
    ///         the open hot wallet does not need to hold any significant amount of coins, or it can even be
    ///         completely empty.
    ///     </para>
    /// </remarks>
    /// <seealso cref="ColdStakingManager.GetColdStakingScript(NBitcoin.ScriptId, NBitcoin.ScriptId)" />
    /// <seealso cref="FullNodeFeature" />
    public class ColdStakingFeature : BaseWalletFeature
    {
        readonly IAddressBookManager addressBookManager;

        /// <summary>The broadcaster behavior.</summary>
        readonly BroadcasterBehavior broadcasterBehavior;

        /// <summary>The cold staking manager.</summary>
        readonly ColdStakingManager coldStakingManager;

        /// <summary>The connection manager.</summary>
        readonly IConnectionManager connectionManager;

        /// <summary>The instance logger.</summary>
        readonly ILogger logger;

        /// <summary>The logger factory used to create instance loggers.</summary>
        readonly ILoggerFactory loggerFactory;

        /// <summary>The settings for the node.</summary>
        readonly NodeSettings nodeSettings;

        /// <summary>The settings for the wallet.</summary>
        readonly WalletSettings walletSettings;

        /// <summary>The synchronization manager for the wallet, tasked with keeping the wallet synced with the network.</summary>
        readonly IWalletSyncManager walletSyncManager;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ColdStakingFeature" /> class.
        /// </summary>
        /// <param name="walletSyncManager">
        ///     The synchronization manager for the wallet, tasked with keeping the wallet synced with
        ///     the network.
        /// </param>
        /// <param name="walletManager">The wallet manager.</param>
        /// <param name="addressBookManager">The address book manager.</param>
        /// <param name="signals">The signals responsible for receiving blocks and transactions from the network.</param>
        /// <param name="chain">The chain of blocks.</param>
        /// <param name="connectionManager">The connection manager.</param>
        /// <param name="broadcasterBehavior">The broadcaster behavior.</param>
        /// <param name="nodeSettings">The settings for the node.</param>
        /// <param name="walletSettings">The settings for the wallet.</param>
        /// <param name="loggerFactory">The factory used to create instance loggers.</param>
        /// <param name="nodeStats">The node stats object used to register node stats.</param>
        public ColdStakingFeature(
            IWalletSyncManager walletSyncManager,
            IWalletManager walletManager,
            IAddressBookManager addressBookManager,
            IConnectionManager connectionManager,
            BroadcasterBehavior broadcasterBehavior,
            NodeSettings nodeSettings,
            WalletSettings walletSettings,
            ILoggerFactory loggerFactory,
            INodeStats nodeStats)
        {
            Guard.NotNull(walletManager, nameof(walletManager));
            Guard.NotNull(loggerFactory, nameof(loggerFactory));

            this.coldStakingManager = walletManager as ColdStakingManager;
            Guard.NotNull(this.coldStakingManager, nameof(this.coldStakingManager));

            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.loggerFactory = loggerFactory;

            this.walletSyncManager = walletSyncManager;
            this.addressBookManager = addressBookManager;
            this.connectionManager = connectionManager;
            this.broadcasterBehavior = broadcasterBehavior;
            this.nodeSettings = nodeSettings;
            this.walletSettings = walletSettings;

            nodeStats.RemoveStats(StatsType.Component, typeof(WalletFeature).Name);
            nodeStats.RemoveStats(StatsType.Inline, typeof(WalletFeature).Name);

            nodeStats.RegisterStats(AddComponentStats, StatsType.Component, GetType().Name);
            nodeStats.RegisterStats(AddInlineStats, StatsType.Inline, GetType().Name, 800);
        }

        /// <summary>
        ///     Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            // The wallet feature will print the help.
        }

        /// <summary>
        ///     Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            // The wallet feature will add its own settings to the config.
        }

        void AddInlineStats(StringBuilder benchLogs)
        {
            var walletManager = this.coldStakingManager;

            if (walletManager != null)
            {
                var hashHeightPair = walletManager.LastReceivedBlockInfo();

                benchLogs.AppendLine("Wallet.Height: ".PadRight(LoggingConfiguration.ColumnLength + 1) +
                                     (walletManager.ContainsWallets
                                         ? hashHeightPair.Height.ToString().PadRight(8)
                                         : "No Wallet".PadRight(8)) +
                                     (walletManager.ContainsWallets
                                         ? " Wallet.Hash: ".PadRight(LoggingConfiguration.ColumnLength - 1) +
                                           hashHeightPair.Hash
                                         : string.Empty));
            }
        }

        void AddComponentStats(StringBuilder benchLog)
        {
            var walletNames = this.coldStakingManager.GetWalletsNames();

            if (walletNames.Any())
            {
                benchLog.AppendLine();
                benchLog.AppendLine("======Wallets======");

                foreach (var walletName in walletNames)
                {
                    // Get all the accounts, including the ones used for cold staking.
                    // TODO: change GetAccounts to accept a filter.
                    foreach (var account in this.coldStakingManager.GetAccounts(walletName))
                    {
                        var accountBalance = this.coldStakingManager.GetBalances(walletName, account.Name).Single();
                        benchLog.AppendLine(
                            ($"{walletName}/{account.Name}" + ",").PadRight(LoggingConfiguration.ColumnLength + 20)
                            + (" Confirmed balance: " + accountBalance.AmountConfirmed.ToString()).PadRight(
                                LoggingConfiguration.ColumnLength + 20)
                            + " Unconfirmed balance: " + accountBalance.AmountUnconfirmed.ToString());
                    }

                    var coldStakingAccount =
                        this.coldStakingManager.GetColdStakingAccount(this.coldStakingManager.GetWallet(walletName),
                            true);
                    if (coldStakingAccount != null)
                    {
                        var accountBalance = this.coldStakingManager.GetBalances(walletName, coldStakingAccount.Name)
                            .Single();
                        benchLog.AppendLine(
                            ($"{walletName}/{coldStakingAccount.Name}" + ",").PadRight(
                                LoggingConfiguration.ColumnLength + 20)
                            + (" Confirmed balance: " + accountBalance.AmountConfirmed.ToString()).PadRight(
                                LoggingConfiguration.ColumnLength + 20)
                            + " Unconfirmed balance: " + accountBalance.AmountUnconfirmed.ToString());
                    }

                    var hotStakingAccount =
                        this.coldStakingManager.GetColdStakingAccount(this.coldStakingManager.GetWallet(walletName),
                            false);
                    if (hotStakingAccount != null)
                    {
                        var accountBalance = this.coldStakingManager.GetBalances(walletName, hotStakingAccount.Name)
                            .Single();
                        benchLog.AppendLine(
                            ($"{walletName}/{hotStakingAccount.Name}" + ",").PadRight(
                                LoggingConfiguration.ColumnLength + 20)
                            + (" Confirmed balance: " + accountBalance.AmountConfirmed.ToString()).PadRight(
                                LoggingConfiguration.ColumnLength + 20)
                            + " Unconfirmed balance: " + accountBalance.AmountUnconfirmed.ToString());
                    }
                }
            }
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }
    }

    /// <summary>
    ///     A class providing extension methods for <see cref="IFullNodeBuilder" />.
    /// </summary>
    public static class FullNodeBuilderColdStakingExtension
    {
        // TODO: Move to IServiceCollection helper class.
        public static bool RemoveSingleton<T>(this IServiceCollection services)
        {
            // Remove the service if it exists.
            return services.Remove(services.Where(sd => sd.ServiceType == typeof(T)).FirstOrDefault());
        }

        public static IFullNodeBuilder UseColdStakingWallet(this IFullNodeBuilder fullNodeBuilder)
        {
            if (!fullNodeBuilder.Network.Consensus.IsProofOfStake)
                throw new InvalidOperationException("Cold staking can only be used on a PoS network.");

            // Register the cold staking script template.
            fullNodeBuilder.Network.StandardScriptsRegistry.RegisterStandardScriptTemplate(ColdStakingScriptTemplate
                .Instance);

            LoggingConfiguration.RegisterFeatureNamespace<ColdStakingFeature>("wallet");

            fullNodeBuilder.UseWallet();

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<ColdStakingFeature>()
                    .DependOn<MempoolFeature>()
                    .DependOn<BlockStoreFeature>()
                    .DependOn<RPCFeature>()
                    .FeatureServices(services =>
                    {
                        services.RemoveSingleton<IWalletManager>();
                        services.AddSingleton<IWalletManager, ColdStakingManager>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}