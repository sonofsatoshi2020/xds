using System;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Miner
{
    /// <summary>
    ///     Configuration related to the miner interface.
    /// </summary>
    public class MinerSettings
    {
        const ulong MinimumSplitCoinValueDefaultValue = 100 * Money.COIN;

        const ulong MinimumStakingCoinValueDefaultValue = 10 * Money.CENT;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>
        ///     Initializes an instance of the object from the node configuration.
        /// </summary>
        /// <param name="nodeSettings">The node configuration.</param>
        public MinerSettings(NodeSettings nodeSettings)
        {
            Guard.NotNull(nodeSettings, nameof(nodeSettings));

            this.logger = nodeSettings.LoggerFactory.CreateLogger(typeof(MinerSettings).FullName);

            var config = nodeSettings.ConfigReader;

            this.Mine = config.GetOrDefault("mine", false, this.logger);
            if (this.Mine)
                this.MineAddress = config.GetOrDefault<string>("mineaddress", null, this.logger);
            this.MineThreadCount = config.GetOrDefault<int>("minethreads", 1, this.logger);

            this.Stake = config.GetOrDefault("stake", false, this.logger);
            if (this.Stake)
            {
                this.WalletName = config.GetOrDefault<string>("walletname", null, this.logger);
                this.WalletPassword = config.GetOrDefault<string>("walletpassword", null); // No logging!
            }

            var blockMaxSize = (uint) config.GetOrDefault("blockmaxsize",
                (int) nodeSettings.Network.Consensus.Options.MaxBlockSerializedSize, this.logger);
            var blockMaxWeight = (uint) config.GetOrDefault("blockmaxweight",
                (int) nodeSettings.Network.Consensus.Options.MaxBlockWeight, this.logger);

            this.BlockDefinitionOptions =
                new BlockDefinitionOptions(blockMaxWeight, blockMaxSize).RestrictForNetwork(nodeSettings.Network);

            this.EnableCoinStakeSplitting = config.GetOrDefault("enablecoinstakesplitting", true, this.logger);
            this.MinimumSplitCoinValue =
                config.GetOrDefault("minimumsplitcoinvalue", MinimumSplitCoinValueDefaultValue, this.logger);
            this.MinimumStakingCoinValue = config.GetOrDefault("minimumstakingcoinvalue",
                MinimumStakingCoinValueDefaultValue, this.logger);
            this.MinimumStakingCoinValue = this.MinimumStakingCoinValue == 0 ? 1 : this.MinimumStakingCoinValue;

            this.EnforceStakingFlag = config.GetOrDefault("enforceStakingFlag", false, this.logger);
        }

        /// <summary>
        ///     Enable the node to stake.
        /// </summary>
        public bool Stake { get; }

        /// <summary>
        ///     Enable splitting coins when staking.
        /// </summary>
        public bool EnableCoinStakeSplitting { get; }

        /// <summary>
        ///     Minimum value a coin has to be in order to be considered for staking.
        /// </summary>
        /// <remarks>
        ///     This can be used to save on CPU consumption by excluding small coins that would not significantly impact a wallet's
        ///     staking power.
        /// </remarks>
        public ulong MinimumStakingCoinValue { get; }

        /// <summary>
        ///     Targeted minimum value of staking coins after splitting.
        /// </summary>
        public ulong MinimumSplitCoinValue { get; }

        /// <summary>
        ///     Enable the node to mine.
        /// </summary>
        public bool Mine { get; }

        public int MineThreadCount { get; }

        /// <summary>
        ///     If true this will only allow staking coins that have been flaged.
        /// </summary>
        public bool EnforceStakingFlag { get; }

        /// <summary>
        ///     An address to use when mining, if not specified and address from the wallet will be used.
        /// </summary>
        public string MineAddress { get; set; }

        /// <summary>
        ///     The wallet password needed when staking to sign blocks.
        /// </summary>
        public string WalletPassword { get; set; }

        /// <summary>
        ///     The wallet name to select outputs to stake.
        /// </summary>
        public string WalletName { get; set; }

        /// <summary>
        ///     Settings for <see cref="BlockDefinition" />.
        /// </summary>
        public BlockDefinitionOptions BlockDefinitionOptions { get; }

        /// <summary>
        ///     Displays mining help information on the console.
        /// </summary>
        /// <param name="network">Not used.</param>
        public static void PrintHelp(Network network)
        {
            var defaults = NodeSettings.Default(network);
            var builder = new StringBuilder();

            builder.AppendLine("-mine=<0 or 1>                      Enable POW mining.");
            builder.AppendLine("-stake=<0 or 1>                     Enable POS.");
            builder.AppendLine("-mineaddress=<string>               The address to use for mining (empty string to select an address from the wallet).");
            builder.AppendLine("-minethreads=1                      Total threads to mine on (default 1).");

            builder.AppendLine("-mine=<0 or 1>                      Enable POW mining.");

            builder.AppendLine("-walletname=<string>                The wallet name to use when staking.");
            builder.AppendLine("-walletpassword=<string>            Password to unlock the wallet.");
            builder.AppendLine(
                "-blockmaxsize=<number>              Maximum block size (in bytes) for the miner to generate.");
            builder.AppendLine(
                "-blockmaxweight=<number>            Maximum block weight (in weight units) for the miner to generate.");
            builder.AppendLine(
                "-enablecoinstakesplitting=<0 or 1>  Enable splitting coins when staking. This is true by default.");
            builder.AppendLine(
                $"-minimumstakingcoinvalue=<number>   Minimum size of the coins considered for staking, in satoshis. Default value is {MinimumStakingCoinValueDefaultValue:N0} satoshis (= {MinimumStakingCoinValueDefaultValue / (decimal) Money.COIN:N1} Coin).");
            builder.AppendLine(
                $"-minimumsplitcoinvalue=<number>     Targeted minimum value of staking coins after splitting, in satoshis. Default value is {MinimumSplitCoinValueDefaultValue:N0} satoshis (= {MinimumSplitCoinValueDefaultValue / Money.COIN} Coin).");

            builder.AppendLine(
                "-enforceStakingFlag=<0 or 1>        If true staking will require whitelisting addresses in order to stake. Defult is false");

            defaults.Logger.LogInformation(builder.ToString());
        }

        /// <summary>
        ///     Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            builder.AppendLine("####Miner Settings####");
            builder.AppendLine("#Enable POW mining.");
            builder.AppendLine("#mine=0");
            builder.AppendLine("#Enable POS.");
            builder.AppendLine("#stake=0");
            builder.AppendLine("#The address to use for mining (empty string to select an address from the wallet).");
            builder.AppendLine("#mineaddress=<string>");
            builder.AppendLine("#Total threads to mine on (default 1)..");
            builder.AppendLine("#minethreads=1");
            builder.AppendLine("#The wallet name to use when staking.");
            builder.AppendLine("#walletname=<string>");
            builder.AppendLine("#Password to unlock the wallet.");
            builder.AppendLine("#walletpassword=<string>");
            builder.AppendLine("#Maximum block size (in bytes) for the miner to generate.");
            builder.AppendLine($"#blockmaxsize={network.Consensus.Options.MaxBlockSerializedSize}");
            builder.AppendLine("#Maximum block weight (in weight units) for the miner to generate.");
            builder.AppendLine($"#blockmaxweight={network.Consensus.Options.MaxBlockWeight}");
            builder.AppendLine("#Enable splitting coins when staking.");
            builder.AppendLine("#enablecoinstakesplitting=1");
            builder.AppendLine("#Minimum size of the coins considered for staking, in satoshis.");
            builder.AppendLine($"#minimumstakingcoinvalue={MinimumStakingCoinValueDefaultValue}");
            builder.AppendLine("#Targeted minimum value of staking coins after splitting, in satoshis.");
            builder.AppendLine($"#minimumsplitcoinvalue={MinimumSplitCoinValueDefaultValue}");
            builder.AppendLine("#If staking will require whitelisting addresses in order to stake. Defult is false.");
            builder.AppendLine("#enforceStakingFlag=0");
        }
    }
}