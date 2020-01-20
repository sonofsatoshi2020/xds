using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.BIP32;
using NBitcoin.BIP39;
using NBitcoin.BuilderExtensions;
using UnnamedCoin.Bitcoin.AsyncWork;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Features.Wallet.Broadcasting;
using UnnamedCoin.Bitcoin.Features.Wallet.Interfaces;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.Extensions;

[assembly: InternalsVisibleTo("Stratis.Bitcoin.Features.Wallet.Tests")]

namespace UnnamedCoin.Bitcoin.Features.Wallet
{
    /// <summary>
    ///     A manager providing operations on wallets.
    /// </summary>
    public class WalletManager : IWalletManager
    {
        /// <summary>Used to get the first account.</summary>
        public const string DefaultAccount = "account 0";

        // <summary>As per RPC method definition this should be the max allowable expiry duration.</summary>
        const int MaxWalletUnlockDurationInSeconds = 1073741824;

        /// <summary>Quantity of accounts created in a wallet file when a wallet is restored.</summary>
        const int WalletRecoveryAccountsCount = 1;

        /// <summary>Quantity of accounts created in a wallet file when a wallet is created.</summary>
        const int WalletCreationAccountsCount = 1;

        /// <summary>File extension for wallet files.</summary>
        const string WalletFileExtension = "wallet.json";

        /// <summary>Timer for saving wallet files to the file system.</summary>
        const int WalletSavetimeIntervalInMinutes = 5;

        const string DownloadChainLoop = "WalletManager.DownloadChain";

        /// <summary>Factory for creating background async loop tasks.</summary>
        readonly IAsyncProvider asyncProvider;

        /// <summary>The broadcast manager.</summary>
        readonly IBroadcasterManager broadcasterManager;

        /// <summary>The chain of headers.</summary>
        protected readonly ChainIndexer ChainIndexer;

        /// <summary>The type of coin used in this manager.</summary>
        protected readonly int coinType;

        /// <summary>Provider of time functions.</summary>
        readonly IDateTimeProvider dateTimeProvider;

        /// <summary>An object capable of storing <see cref="Wallet" />s to the file system.</summary>
        readonly FileStorage<Wallet> fileStorage;

        /// <summary>
        ///     A lock object that protects access to the <see cref="Wallet" />.
        ///     Any of the collections inside Wallet must be synchronized using this lock.
        /// </summary>
        protected readonly object lockObject;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        protected readonly Network network;

        /// <summary>Global application life cycle control - triggers when application shuts down.</summary>
        readonly INodeLifetime nodeLifetime;

        /// <summary>The private key cache for unlocked wallets.</summary>
        readonly MemoryCache privateKeyCache;

        /// <summary>The settings for the wallet feature.</summary>
        readonly IScriptAddressReader scriptAddressReader;

        /// <summary>The settings for the wallet feature.</summary>
        readonly WalletSettings walletSettings;

        /// <summary>The async loop we need to wait upon before we can shut down this manager.</summary>
        IAsyncLoop asyncLoop;

        readonly Dictionary<OutPoint, TransactionData> inputLookup;

        // In order to allow faster look-ups of transactions affecting the wallets' addresses,
        // we keep a couple of objects in memory:
        // 1. the list of unspent outputs for checking whether inputs from a transaction are being spent by our wallet and
        // 2. the list of addresses contained in our wallet for checking whether a transaction is being paid to the wallet.
        // 3. a mapping of all inputs with their corresponding transactions, to facilitate rapid lookup
        Dictionary<OutPoint, TransactionData> outpointLookup;
        protected internal ScriptToAddressLookup scriptToAddressLookup;

        public WalletManager(
            ILoggerFactory loggerFactory,
            Network network,
            ChainIndexer chainIndexer,
            WalletSettings walletSettings,
            DataFolder dataFolder,
            IWalletFeePolicy walletFeePolicy,
            IAsyncProvider asyncProvider,
            INodeLifetime nodeLifetime,
            IDateTimeProvider dateTimeProvider,
            IScriptAddressReader scriptAddressReader,
            IBroadcasterManager broadcasterManager =
                null) // no need to know about transactions the node will broadcast to.
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(chainIndexer, nameof(chainIndexer));
            Guard.NotNull(walletSettings, nameof(walletSettings));
            Guard.NotNull(dataFolder, nameof(dataFolder));
            Guard.NotNull(walletFeePolicy, nameof(walletFeePolicy));
            Guard.NotNull(asyncProvider, nameof(asyncProvider));
            Guard.NotNull(nodeLifetime, nameof(nodeLifetime));
            Guard.NotNull(scriptAddressReader, nameof(scriptAddressReader));

            this.walletSettings = walletSettings;
            this.lockObject = new object();

            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.Wallets = new ConcurrentBag<Wallet>();

            this.network = network;
            this.coinType = network.Consensus.CoinType;
            this.ChainIndexer = chainIndexer;
            this.asyncProvider = asyncProvider;
            this.nodeLifetime = nodeLifetime;
            this.fileStorage = new FileStorage<Wallet>(dataFolder.WalletPath);
            this.broadcasterManager = broadcasterManager;
            this.scriptAddressReader = scriptAddressReader;
            this.dateTimeProvider = dateTimeProvider;

            // register events
            if (this.broadcasterManager != null)
                this.broadcasterManager.TransactionStateChanged += BroadcasterManager_TransactionStateChanged;

            this.scriptToAddressLookup = CreateAddressFromScriptLookup();
            this.outpointLookup = new Dictionary<OutPoint, TransactionData>();
            this.inputLookup = new Dictionary<OutPoint, TransactionData>();

            this.privateKeyCache = new MemoryCache(new MemoryCacheOptions
                {ExpirationScanFrequency = new TimeSpan(0, 1, 0)});
        }

        /// <summary>Gets the list of wallets.</summary>
        public ConcurrentBag<Wallet> Wallets { get; }

        public uint256 WalletTipHash { get; set; }
        public int WalletTipHeight { get; set; }

        /// <inheritdoc />
        public virtual Dictionary<string, ScriptTemplate> GetValidStakingTemplates()
        {
            return new Dictionary<string, ScriptTemplate>
            {
                {"P2PK", PayToPubkeyTemplate.Instance},
                {"P2PKH", PayToPubkeyHashTemplate.Instance},
                {"P2SH", PayToScriptHashTemplate.Instance},
                {"P2WPKH", PayToWitPubKeyHashTemplate.Instance},
                {"P2WSH", PayToWitScriptHashTemplate.Instance}
            };
        }

        // <inheritdoc />
        public virtual IEnumerable<BuilderExtension> GetTransactionBuilderExtensionsForStaking()
        {
            return new List<BuilderExtension>();
        }

        public void Start()
        {
            // Find wallets and load them in memory.
            var wallets = this.fileStorage.LoadByFileExtension(WalletFileExtension);

            foreach (var wallet in wallets)
            {
                Load(wallet);
                foreach (var account in wallet.GetAccounts())
                {
                    AddAddressesToMaintainBuffer(account, false);
                    AddAddressesToMaintainBuffer(account, true);
                }
            }

            if (this.walletSettings.IsDefaultWalletEnabled())
            {
                // Check if it already exists, if not, create one.
                if (!wallets.Any(w => w.Name == this.walletSettings.DefaultWalletName))
                {
                    var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
                    CreateWallet(this.walletSettings.DefaultWalletPassword, this.walletSettings.DefaultWalletName,
                        string.Empty, mnemonic);
                }

                // Make sure both unlock is specified, and that we actually have a default wallet name specified.
                if (this.walletSettings.UnlockDefaultWallet)
                    UnlockWallet(this.walletSettings.DefaultWalletPassword, this.walletSettings.DefaultWalletName,
                        MaxWalletUnlockDurationInSeconds);
            }

            // Load data in memory for faster lookups.
            LoadKeysLookupLock();

            // Find the last chain block received by the wallet manager.
            var hashHeightPair = LastReceivedBlockInfo();
            this.WalletTipHash = hashHeightPair.Hash;
            this.WalletTipHeight = hashHeightPair.Height;

            // Save the wallets file every 5 minutes to help against crashes.
            this.asyncLoop = this.asyncProvider.CreateAndRunAsyncLoop("Wallet persist job", token =>
                {
                    SaveWallets();
                    this.logger.LogInformation("Wallets saved to file at {0}.", this.dateTimeProvider.GetUtcNow());

                    this.logger.LogTrace("(-)[IN_ASYNC_LOOP]");
                    return Task.CompletedTask;
                },
                this.nodeLifetime.ApplicationStopping,
                TimeSpan.FromMinutes(WalletSavetimeIntervalInMinutes),
                TimeSpan.FromMinutes(WalletSavetimeIntervalInMinutes));
        }

        /// <inheritdoc />
        public void Stop()
        {
            if (this.broadcasterManager != null)
                this.broadcasterManager.TransactionStateChanged -= BroadcasterManager_TransactionStateChanged;

            this.asyncLoop?.Dispose();
            SaveWallets();
        }

        /// <inheritdoc />
        public Mnemonic CreateWallet(string password, string name, string passphrase, Mnemonic mnemonic = null,
            int? coinType = null)
        {
            Guard.NotEmpty(password, nameof(password));
            Guard.NotEmpty(name, nameof(name));
            Guard.NotNull(passphrase, nameof(passphrase));

            // Generate the root seed used to generate keys from a mnemonic picked at random
            // and a passphrase optionally provided by the user.
            mnemonic = mnemonic ?? new Mnemonic(Wordlist.English, WordCount.Twelve);

            var extendedKey = HdOperations.GetExtendedKey(mnemonic, passphrase);

            // Create a wallet file.
            var encryptedSeed = extendedKey.PrivateKey.GetEncryptedBitcoinSecret(password, this.network).ToWif();
            var wallet = GenerateWalletFile(name, encryptedSeed, extendedKey.ChainCode, coinType: coinType);

            // Generate multiple accounts and addresses from the get-go.
            for (var i = 0; i < WalletCreationAccountsCount; i++)
            {
                var account = wallet.AddNewAccount(password, this.dateTimeProvider.GetTimeOffset());
                var newReceivingAddresses =
                    account.CreateAddresses(this.network, this.walletSettings.UnusedAddressesBuffer);
                var newChangeAddresses =
                    account.CreateAddresses(this.network, this.walletSettings.UnusedAddressesBuffer, true);
                UpdateKeysLookupLocked(newReceivingAddresses.Concat(newChangeAddresses));
            }

            // If the chain is downloaded, we set the height of the newly created wallet to it.
            // However, if the chain is still downloading when the user creates a wallet,
            // we wait until it is downloaded in order to set it. Otherwise, the height of the wallet will be the height of the chain at that moment.
            if (this.ChainIndexer.IsDownloaded())
                UpdateLastBlockSyncedHeight(wallet, this.ChainIndexer.Tip);
            else
                UpdateWhenChainDownloaded(new[] {wallet}, this.dateTimeProvider.GetUtcNow());

            // Save the changes to the file and add addresses to be tracked.
            SaveWallet(wallet);
            Load(wallet);

            return mnemonic;
        }

        /// <inheritdoc />
        public string SignMessage(string password, string walletName, string externalAddress, string message)
        {
            Guard.NotEmpty(password, nameof(password));
            Guard.NotEmpty(walletName, nameof(walletName));
            Guard.NotEmpty(message, nameof(message));
            Guard.NotEmpty(externalAddress, nameof(externalAddress));

            // Get wallet
            var wallet = GetWalletByName(walletName);

            // Sign the message.
            var hdAddress = wallet.GetAddress(externalAddress);
            var privateKey = wallet.GetExtendedPrivateKeyForAddress(password, hdAddress).PrivateKey;
            return privateKey.SignMessage(message);
        }

        /// <inheritdoc />
        public bool VerifySignedMessage(string externalAddress, string message, string signature)
        {
            Guard.NotEmpty(message, nameof(message));
            Guard.NotEmpty(externalAddress, nameof(externalAddress));
            Guard.NotEmpty(signature, nameof(signature));

            var result = false;

            try
            {
                var bitcoinPubKeyAddress = new BitcoinPubKeyAddress(externalAddress, this.network);
                result = bitcoinPubKeyAddress.VerifyMessage(message, signature);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug("Failed to verify message: {0}", ex.ToString());
                this.logger.LogTrace("(-)[EXCEPTION]");
            }

            return result;
        }

        /// <inheritdoc />
        public Wallet LoadWallet(string password, string name)
        {
            Guard.NotEmpty(password, nameof(password));
            Guard.NotEmpty(name, nameof(name));

            // Load the file from the local system.
            var wallet = this.fileStorage.LoadByFileName($"{name}.{WalletFileExtension}");

            // Check the password.
            try
            {
                if (!wallet.IsExtPubKeyWallet)
                    Key.Parse(wallet.EncryptedSeed, password, wallet.Network);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug("Exception occurred: {0}", ex.ToString());
                this.logger.LogTrace("(-)[EXCEPTION]");
                throw new SecurityException(ex.Message);
            }

            Load(wallet);

            return wallet;
        }

        /// <inheritdoc />
        public void UnlockWallet(string password, string name, int timeout)
        {
            Guard.NotEmpty(password, nameof(password));
            Guard.NotEmpty(name, nameof(name));

            // Length of expiry of the unlocking, restricted to max duration.
            var duration = new TimeSpan(0, 0, Math.Min(timeout, MaxWalletUnlockDurationInSeconds));

            CacheSecret(name, password, duration);
        }

        /// <inheritdoc />
        public void LockWallet(string name)
        {
            Guard.NotNull(name, nameof(name));

            var wallet = GetWalletByName(name);
            var cacheKey = wallet.EncryptedSeed;
            this.privateKeyCache.Remove(cacheKey);
        }

        /// <inheritdoc />
        public virtual Wallet RecoverWallet(string password, string name, string mnemonic, DateTime creationTime,
            string passphrase, int? coinType = null)
        {
            Guard.NotEmpty(password, nameof(password));
            Guard.NotEmpty(name, nameof(name));
            Guard.NotEmpty(mnemonic, nameof(mnemonic));
            Guard.NotNull(passphrase, nameof(passphrase));

            // Generate the root seed used to generate keys.
            ExtKey extendedKey;
            try
            {
                extendedKey = HdOperations.GetExtendedKey(mnemonic, passphrase);
            }
            catch (NotSupportedException ex)
            {
                this.logger.LogDebug("Exception occurred: {0}", ex.ToString());
                this.logger.LogTrace("(-)[EXCEPTION]");

                if (ex.Message == "Unknown")
                    throw new WalletException("Please make sure you enter valid mnemonic words.");

                throw;
            }

            // Create a wallet file.
            var encryptedSeed = extendedKey.PrivateKey.GetEncryptedBitcoinSecret(password, this.network).ToWif();
            var wallet = GenerateWalletFile(name, encryptedSeed, extendedKey.ChainCode, creationTime, coinType);

            // Generate multiple accounts and addresses from the get-go.
            for (var i = 0; i < WalletRecoveryAccountsCount; i++)
            {
                HdAccount account;
                lock (this.lockObject)
                {
                    account = wallet.AddNewAccount(password, this.dateTimeProvider.GetTimeOffset());
                }

                var newReceivingAddresses =
                    account.CreateAddresses(this.network, this.walletSettings.UnusedAddressesBuffer);
                var newChangeAddresses =
                    account.CreateAddresses(this.network, this.walletSettings.UnusedAddressesBuffer, true);
                UpdateKeysLookupLocked(newReceivingAddresses.Concat(newChangeAddresses));
            }

            // If the chain is downloaded, we set the height of the recovered wallet to that of the recovery date.
            // However, if the chain is still downloading when the user restores a wallet,
            // we wait until it is downloaded in order to set it. Otherwise, the height of the wallet may not be known.
            if (this.ChainIndexer.IsDownloaded())
            {
                var blockSyncStart = this.ChainIndexer.GetHeightAtTime(creationTime);
                UpdateLastBlockSyncedHeight(wallet, this.ChainIndexer.GetHeader(blockSyncStart));
            }
            else
            {
                UpdateWhenChainDownloaded(new[] {wallet}, creationTime);
            }

            SaveWallet(wallet);
            Load(wallet);

            return wallet;
        }

        /// <inheritdoc />
        public Wallet RecoverWallet(string name, ExtPubKey extPubKey, int accountIndex, DateTime creationTime)
        {
            Guard.NotEmpty(name, nameof(name));
            Guard.NotNull(extPubKey, nameof(extPubKey));
            this.logger.LogDebug("({0}:'{1}',{2}:'{3}',{4}:'{5}')", nameof(name), name, nameof(extPubKey), extPubKey,
                nameof(accountIndex), accountIndex);

            // Create a wallet file.
            var wallet = GenerateExtPubKeyOnlyWalletFile(name, creationTime);

            // Generate account
            HdAccount account;
            lock (this.lockObject)
            {
                account = wallet.AddNewAccount(extPubKey, accountIndex, this.dateTimeProvider.GetTimeOffset());
            }

            var newReceivingAddresses =
                account.CreateAddresses(this.network, this.walletSettings.UnusedAddressesBuffer);
            var newChangeAddresses =
                account.CreateAddresses(this.network, this.walletSettings.UnusedAddressesBuffer, true);
            UpdateKeysLookupLocked(newReceivingAddresses.Concat(newChangeAddresses));

            // If the chain is downloaded, we set the height of the recovered wallet to that of the recovery date.
            // However, if the chain is still downloading when the user restores a wallet,
            // we wait until it is downloaded in order to set it. Otherwise, the height of the wallet may not be known.
            if (this.ChainIndexer.IsDownloaded())
            {
                var blockSyncStart = this.ChainIndexer.GetHeightAtTime(creationTime);
                UpdateLastBlockSyncedHeight(wallet, this.ChainIndexer.GetHeader(blockSyncStart));
            }
            else
            {
                UpdateWhenChainDownloaded(new[] {wallet}, creationTime);
            }

            // Save the changes to the file and add addresses to be tracked.
            SaveWallet(wallet);
            Load(wallet);
            return wallet;
        }

        /// <inheritdoc />
        public HdAccount GetUnusedAccount(string walletName, string password)
        {
            Guard.NotEmpty(walletName, nameof(walletName));
            Guard.NotEmpty(password, nameof(password));

            var wallet = GetWalletByName(walletName);

            if (wallet.IsExtPubKeyWallet)
            {
                this.logger.LogTrace("(-)[CANNOT_ADD_ACCOUNT_TO_EXTPUBKEY_WALLET]");
                throw new CannotAddAccountToXpubKeyWalletException("Use recover-via-extpubkey instead.");
            }

            var res = GetUnusedAccount(wallet, password);
            return res;
        }

        /// <inheritdoc />
        public HdAccount GetUnusedAccount(Wallet wallet, string password)
        {
            Guard.NotNull(wallet, nameof(wallet));
            Guard.NotEmpty(password, nameof(password));

            HdAccount account;

            lock (this.lockObject)
            {
                account = wallet.GetFirstUnusedAccount();

                if (account != null)
                {
                    this.logger.LogTrace("(-)[ACCOUNT_FOUND]");
                    return account;
                }

                // No unused account was found, create a new one.
                account = wallet.AddNewAccount(password, this.dateTimeProvider.GetTimeOffset());
                var newReceivingAddresses =
                    account.CreateAddresses(this.network, this.walletSettings.UnusedAddressesBuffer);
                var newChangeAddresses =
                    account.CreateAddresses(this.network, this.walletSettings.UnusedAddressesBuffer, true);
                UpdateKeysLookupLocked(newReceivingAddresses.Concat(newChangeAddresses));
            }

            // Save the changes to the file.
            SaveWallet(wallet);

            return account;
        }

        public string GetExtPubKey(WalletAccountReference accountReference)
        {
            Guard.NotNull(accountReference, nameof(accountReference));

            var wallet = GetWalletByName(accountReference.WalletName);

            string extPubKey;
            lock (this.lockObject)
            {
                // Get the account.
                var account = wallet.GetAccount(accountReference.AccountName);
                if (account == null)
                    throw new WalletException(
                        $"No account with the name '{accountReference.AccountName}' could be found.");
                extPubKey = account.ExtendedPubKey;
            }

            return extPubKey;
        }

        /// <inheritdoc />
        public HdAddress GetUnusedAddress(WalletAccountReference accountReference)
        {
            var res = GetUnusedAddresses(accountReference, 1).Single();

            return res;
        }

        /// <inheritdoc />
        public HdAddress GetUnusedChangeAddress(WalletAccountReference accountReference)
        {
            var res = GetUnusedAddresses(accountReference, 1, true).Single();

            return res;
        }

        /// <inheritdoc />
        public IEnumerable<HdAddress> GetUnusedAddresses(WalletAccountReference accountReference, int count,
            bool isChange = false, bool alwaysnew = false)
        {
            Guard.NotNull(accountReference, nameof(accountReference));
            Guard.Assert(count > 0);

            var wallet = GetWalletByName(accountReference.WalletName);

            var generated = false;
            IEnumerable<HdAddress> addresses;

            var newAddresses = new List<HdAddress>();

            lock (this.lockObject)
            {
                // Get the account.
                var account = wallet.GetAccount(accountReference.AccountName);
                if (account == null)
                    throw new WalletException(
                        $"No account with the name '{accountReference.AccountName}' could be found.");

                var unusedAddresses = isChange
                    ? account.InternalAddresses.Where(acc => !acc.Transactions.Any()).ToList()
                    : account.ExternalAddresses.Where(acc => !acc.Transactions.Any()).ToList();

                var diff = alwaysnew ? -1 : unusedAddresses.Count - count;

                if (diff < 0)
                {
                    newAddresses = account.CreateAddresses(this.network, Math.Abs(diff), isChange).ToList();
                    UpdateKeysLookupLocked(newAddresses);
                    generated = true;
                }

                addresses = unusedAddresses.Concat(newAddresses).OrderBy(x => x.Index).Take(count);
            }

            if (generated)
            {
                // Save the changes to the file.
                SaveWallet(wallet);

                return alwaysnew ? newAddresses : addresses;
            }

            return addresses;
        }

        /// <inheritdoc />
        public (string folderPath, IEnumerable<string>) GetWalletsFiles()
        {
            return (this.fileStorage.FolderPath, this.fileStorage.GetFilesNames(GetWalletFileExtension()));
        }

        /// <inheritdoc />
        public IEnumerable<AccountHistory> GetHistory(string walletName, string accountName = null)
        {
            Guard.NotEmpty(walletName, nameof(walletName));

            // In order to calculate the fee properly we need to retrieve all the transactions with spending details.
            var wallet = GetWalletByName(walletName);

            var accountsHistory = new List<AccountHistory>();

            lock (this.lockObject)
            {
                var accounts = new List<HdAccount>();
                if (!string.IsNullOrEmpty(accountName))
                {
                    var account = wallet.GetAccount(accountName);
                    if (account == null)
                        throw new WalletException($"No account with the name '{accountName}' could be found.");

                    accounts.Add(account);
                }
                else
                {
                    accounts.AddRange(wallet.GetAccounts());
                }

                foreach (var account in accounts) accountsHistory.Add(GetHistory(account));
            }

            return accountsHistory;
        }

        /// <inheritdoc />
        public AccountHistory GetHistory(HdAccount account)
        {
            Guard.NotNull(account, nameof(account));
            FlatHistory[] items;
            lock (this.lockObject)
            {
                // Get transactions contained in the account.
                items = account.GetCombinedAddresses()
                    .Where(a => a.Transactions.Any())
                    .SelectMany(s => s.Transactions.Select(t => new FlatHistory {Address = s, Transaction = t}))
                    .ToArray();
            }

            return new AccountHistory {Account = account, History = items};
        }

        /// <inheritdoc />
        public IEnumerable<AccountBalance> GetBalances(string walletName, string accountName = null)
        {
            var balances = new List<AccountBalance>();

            lock (this.lockObject)
            {
                var wallet = GetWalletByName(walletName);

                var accounts = new List<HdAccount>();
                if (!string.IsNullOrEmpty(accountName))
                {
                    var account = wallet.GetAccount(accountName);
                    if (account == null)
                        throw new WalletException($"No account with the name '{accountName}' could be found.");

                    accounts.Add(account);
                }
                else
                {
                    accounts.AddRange(wallet.GetAccounts());
                }

                foreach (var account in accounts)
                {
                    // Calculates the amount of spendable coins.
                    var spendableBalance = account.GetSpendableTransactions(this.ChainIndexer.Tip.Height,
                        this.network.Consensus.CoinbaseMaturity).ToArray();
                    var spendableAmount = Money.Zero;
                    foreach (var bal in spendableBalance) spendableAmount += bal.Transaction.Amount;

                    // Get the total balances.
                    (Money amountConfirmed, Money amountUnconfirmed) result = account.GetBalances();

                    balances.Add(new AccountBalance
                    {
                        Account = account,
                        AmountConfirmed = result.amountConfirmed,
                        AmountUnconfirmed = result.amountUnconfirmed,
                        SpendableAmount = spendableAmount
                    });
                }
            }

            return balances;
        }

        /// <inheritdoc />
        public AddressBalance GetAddressBalance(string address)
        {
            Guard.NotEmpty(address, nameof(address));

            var balance = new AddressBalance
            {
                Address = address,
                CoinType = this.coinType
            };

            lock (this.lockObject)
            {
                HdAddress hdAddress = null;

                foreach (var wallet in this.Wallets)
                {
                    hdAddress = wallet.GetAllAddresses().FirstOrDefault(a => a.Address == address);
                    if (hdAddress == null) continue;

                    (Money amountConfirmed, Money amountUnconfirmed) result = hdAddress.GetBalances();

                    Money spendableAmount = wallet
                        .GetAllSpendableTransactions(this.ChainIndexer.Tip.Height)
                        .Where(s => s.Address.Address == hdAddress.Address)
                        .Sum(s => s.Transaction?.Amount ?? 0);

                    balance.AmountConfirmed = result.amountConfirmed;
                    balance.AmountUnconfirmed = result.amountUnconfirmed;
                    balance.SpendableAmount = spendableAmount;

                    break;
                }

                if (hdAddress == null)
                {
                    this.logger.LogTrace("(-)[ADDRESS_NOT_FOUND]");
                    throw new WalletException($"Address '{address}' not found in wallets.");
                }
            }

            return balance;
        }

        /// <inheritdoc />
        public Wallet GetWallet(string walletName)
        {
            Guard.NotEmpty(walletName, nameof(walletName));

            var wallet = GetWalletByName(walletName);

            return wallet;
        }

        /// <inheritdoc />
        public IEnumerable<HdAccount> GetAccounts(string walletName)
        {
            Guard.NotEmpty(walletName, nameof(walletName));

            var wallet = GetWalletByName(walletName);

            HdAccount[] res = null;
            lock (this.lockObject)
            {
                res = wallet.GetAccounts().ToArray();
            }

            return res;
        }

        /// <inheritdoc />
        public int LastBlockHeight()
        {
            if (!this.Wallets.Any())
            {
                var height = this.ChainIndexer.Tip.Height;
                this.logger.LogTrace("(-)[NO_WALLET]:{0}", height);
                return height;
            }

            int res;
            lock (this.lockObject)
            {
                res = this.Wallets.Min(w => w.AccountsRoot.Single().LastBlockSyncedHeight) ?? 0;
            }

            return res;
        }

        /// <inheritdoc />
        public bool ContainsWallets => this.Wallets.Any();

        /// <inheritdoc />
        public IEnumerable<UnspentOutputReference> GetSpendableTransactionsInWallet(string walletName,
            int confirmations = 0)
        {
            return GetSpendableTransactionsInWallet(walletName, confirmations, Wallet.NormalAccounts);
        }

        public virtual IEnumerable<UnspentOutputReference> GetSpendableTransactionsInWalletForStaking(string walletName,
            int confirmations = 0)
        {
            return GetUnspentTransactionsInWallet(walletName, confirmations, Wallet.NormalAccounts);
        }

        /// <inheritdoc />
        public IEnumerable<UnspentOutputReference> GetUnspentTransactionsInWallet(string walletName, int confirmations,
            Func<HdAccount, bool> accountFilter)
        {
            Guard.NotEmpty(walletName, nameof(walletName));

            var wallet = GetWalletByName(walletName);
            UnspentOutputReference[] res = null;
            lock (this.lockObject)
            {
                res = wallet.GetAllUnspentTransactions(this.ChainIndexer.Tip.Height, confirmations, accountFilter)
                    .ToArray();
            }

            return res;
        }

        /// <inheritdoc />
        public IEnumerable<UnspentOutputReference> GetSpendableTransactionsInAccount(
            WalletAccountReference walletAccountReference, int confirmations = 0)
        {
            Guard.NotNull(walletAccountReference, nameof(walletAccountReference));

            var wallet = GetWalletByName(walletAccountReference.WalletName);
            UnspentOutputReference[] res = null;
            lock (this.lockObject)
            {
                var account = wallet.GetAccount(walletAccountReference.AccountName);

                if (account == null)
                {
                    this.logger.LogTrace("(-)[ACT_NOT_FOUND]");
                    throw new WalletException(
                        $"Account '{walletAccountReference.AccountName}' in wallet '{walletAccountReference.WalletName}' not found.");
                }

                res = account.GetSpendableTransactions(this.ChainIndexer.Tip.Height,
                    this.network.Consensus.CoinbaseMaturity, confirmations).ToArray();
            }

            return res;
        }

        /// <inheritdoc />
        public void RemoveBlocks(ChainedHeader fork)
        {
            Guard.NotNull(fork, nameof(fork));

            lock (this.lockObject)
            {
                var allAddresses = this.scriptToAddressLookup.Values;
                foreach (var address in allAddresses)
                {
                    // Remove all the UTXO that have been reorged.
                    IEnumerable<TransactionData> makeUnspendable =
                        address.Transactions.Where(w => w.BlockHeight > fork.Height).ToList();
                    foreach (var transactionData in makeUnspendable)
                        address.Transactions.Remove(transactionData);

                    // Bring back all the UTXO that are now spendable after the reorg.
                    var makeSpendable = address.Transactions.Where(w =>
                        w.SpendingDetails != null && w.SpendingDetails.BlockHeight > fork.Height);
                    foreach (var transactionData in makeSpendable)
                        transactionData.SpendingDetails = null;
                }

                UpdateLastBlockSyncedHeight(fork);

                // Reload the lookup dictionaries.
                RefreshInputKeysLookupLock();
            }
        }

        /// <inheritdoc />
        public void ProcessBlock(Block block, ChainedHeader chainedHeader)
        {
            Guard.NotNull(block, nameof(block));
            Guard.NotNull(chainedHeader, nameof(chainedHeader));

            // If there is no wallet yet, update the wallet tip hash and do nothing else.
            if (!this.Wallets.Any())
            {
                this.WalletTipHash = chainedHeader.HashBlock;
                this.WalletTipHeight = chainedHeader.Height;
                this.logger.LogTrace("(-)[NO_WALLET]");
                return;
            }

            // Is this the next block.
            if (chainedHeader.Header.HashPrevBlock != this.WalletTipHash)
            {
                this.logger.LogDebug("New block's previous hash '{0}' does not match current wallet's tip hash '{1}'.",
                    chainedHeader.Header.HashPrevBlock, this.WalletTipHash);

                // The block coming in to the wallet should never be ahead of the wallet.
                // If the block is behind, let it pass.
                if (chainedHeader.Height > this.WalletTipHeight)
                {
                    this.logger.LogTrace("(-)[BLOCK_TOO_FAR]");
                    throw new WalletException("block too far in the future has arrived to the wallet");
                }
            }

            lock (this.lockObject)
            {
                var trxFoundInBlock = false;
                foreach (var transaction in block.Transactions)
                {
                    var trxFound = ProcessTransaction(transaction, chainedHeader.Height, block);
                    if (trxFound) trxFoundInBlock = true;
                }

                // Update the wallets with the last processed block height.
                // It's important that updating the height happens after the block processing is complete,
                // as if the node is stopped, on re-opening it will start updating from the previous height.
                UpdateLastBlockSyncedHeight(chainedHeader);

                if (trxFoundInBlock)
                    this.logger.LogDebug("Block {0} contains at least one transaction affecting the user's wallet(s).",
                        chainedHeader);
            }
        }

        /// <inheritdoc />
        public virtual bool ProcessTransaction(Transaction transaction, int? blockHeight = null, Block block = null,
            bool isPropagated = true)
        {
            Guard.NotNull(transaction, nameof(transaction));
            var hash = transaction.GetHash();

            bool foundReceivingTrx = false, foundSendingTrx = false;

            lock (this.lockObject)
            {
                if (block != null)
                    // Do a pre-scan of the incoming transaction's inputs to see if they're used in other wallet transactions already.
                    foreach (var input in transaction.Inputs)
                        // See if this input is being used by another wallet transaction present in the index.
                        // The inputs themselves may not belong to the wallet, but the transaction data in the index has to be for a wallet transaction.
                        if (this.inputLookup.TryGetValue(input.PrevOut, out var indexData))
                        {
                            // It's the same transaction, which can occur if the transaction had been added to the wallet previously. Ignore.
                            if (indexData.Id == hash)
                                continue;

                            if (indexData.BlockHash != null)
                                // This should not happen as pre checks are done in mempool and consensus.
                                throw new WalletException(
                                    "The same inputs were found in two different confirmed transactions");

                            // This is a double spend we remove the unconfirmed trx
                            RemoveTransactionsByIds(new[] {indexData.Id});
                            this.inputLookup.Remove(input.PrevOut);
                        }

                // Check the outputs, ignoring the ones with a 0 amount.
                foreach (var utxo in transaction.Outputs.Where(o => o.Value != Money.Zero))
                    // Check if the outputs contain one of our addresses.
                    if (this.scriptToAddressLookup.TryGetValue(utxo.ScriptPubKey, out _))
                    {
                        AddTransactionToWallet(transaction, utxo, blockHeight, block, isPropagated);
                        foundReceivingTrx = true;
                        this.logger.LogDebug("Transaction '{0}' contained funds received by the user's wallet(s).",
                            hash);
                    }

                // Check the inputs - include those that have a reference to a transaction containing one of our scripts and the same index.
                foreach (var input in transaction.Inputs)
                {
                    if (!this.outpointLookup.TryGetValue(input.PrevOut, out var tTx)) continue;

                    // Get the details of the outputs paid out.
                    var paidOutTo = transaction.Outputs.Where(o =>
                    {
                        // If script is empty ignore it.
                        if (o.IsEmpty)
                            return false;

                        // Check if the destination script is one of the wallet's.
                        var found = this.scriptToAddressLookup.TryGetValue(o.ScriptPubKey, out var addr);

                        // Include the keys not included in our wallets (external payees).
                        if (!found)
                            return true;

                        // Include the keys that are in the wallet but that are for receiving
                        // addresses (which would mean the user paid itself).
                        // We also exclude the keys involved in a staking transaction.
                        return !addr.IsChangeAddress() && !transaction.IsCoinStake;
                    });

                    AddSpendingTransactionToWallet(transaction, paidOutTo, tTx.Id, tTx.Index, blockHeight, block);
                    foundSendingTrx = true;
                    this.logger.LogDebug("Transaction '{0}' contained funds sent by the user's wallet(s).", hash);
                }
            }

            return foundSendingTrx || foundReceivingTrx;
        }

        /// <inheritdoc />
        public void DeleteWallet()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void SaveWallets()
        {
            foreach (var wallet in this.Wallets) SaveWallet(wallet);
        }

        /// <inheritdoc />
        public void SaveWallet(Wallet wallet)
        {
            Guard.NotNull(wallet, nameof(wallet));

            lock (this.lockObject)
            {
                this.fileStorage.SaveToFile(wallet, $"{wallet.Name}.{WalletFileExtension}",
                    new FileStorageOption {SerializeNullValues = false});
            }
        }

        /// <inheritdoc />
        public string GetWalletFileExtension()
        {
            return WalletFileExtension;
        }

        /// <inheritdoc />
        public void UpdateLastBlockSyncedHeight(ChainedHeader chainedHeader)
        {
            Guard.NotNull(chainedHeader, nameof(chainedHeader));

            // Update the wallets with the last processed block height.
            foreach (var wallet in this.Wallets) UpdateLastBlockSyncedHeight(wallet, chainedHeader);

            this.WalletTipHash = chainedHeader.HashBlock;
            this.WalletTipHeight = chainedHeader.Height;
        }

        /// <inheritdoc />
        public void UpdateLastBlockSyncedHeight(Wallet wallet, ChainedHeader chainedHeader)
        {
            Guard.NotNull(wallet, nameof(wallet));
            Guard.NotNull(chainedHeader, nameof(chainedHeader));

            // The block locator will help when the wallet
            // needs to rewind this will be used to find the fork.
            wallet.BlockLocator = chainedHeader.GetLocator().Blocks;

            lock (this.lockObject)
            {
                wallet.SetLastBlockDetails(chainedHeader);
            }
        }

        /// <inheritdoc />
        public IEnumerable<string> GetWalletsNames()
        {
            return this.Wallets.Select(w => w.Name);
        }

        /// <inheritdoc />
        public Wallet GetWalletByName(string walletName)
        {
            var wallet = this.Wallets.SingleOrDefault(w => w.Name == walletName);
            if (wallet == null)
            {
                this.logger.LogTrace("(-)[WALLET_NOT_FOUND]");
                throw new WalletException($"No wallet with name '{walletName}' could be found.");
            }

            return wallet;
        }

        /// <inheritdoc />
        public ICollection<uint256> GetFirstWalletBlockLocator()
        {
            return this.Wallets.First().BlockLocator;
        }

        /// <inheritdoc />
        public int? GetEarliestWalletHeight()
        {
            return this.Wallets.Min(w => w.AccountsRoot.Single().LastBlockSyncedHeight);
        }

        /// <inheritdoc />
        public DateTimeOffset GetOldestWalletCreationTime()
        {
            return this.Wallets.Min(w => w.CreationTime);
        }

        /// <inheritdoc />
        public HashSet<(uint256, DateTimeOffset)> RemoveTransactionsByIds(string walletName,
            IEnumerable<uint256> transactionsIds)
        {
            Guard.NotNull(transactionsIds, nameof(transactionsIds));
            Guard.NotEmpty(walletName, nameof(walletName));

            var idsToRemove = transactionsIds.ToList();
            var wallet = GetWallet(walletName);

            var result = new HashSet<(uint256, DateTimeOffset)>();

            lock (this.lockObject)
            {
                var accounts = wallet.GetAccounts(a => true);
                foreach (var account in accounts)
                foreach (var address in account.GetCombinedAddresses())
                    for (var i = 0; i < address.Transactions.Count; i++)
                    {
                        var transaction = address.Transactions.ElementAt(i);

                        // Remove the transaction from the list of transactions affecting an address.
                        // Only transactions that haven't been confirmed in a block can be removed.
                        if (!transaction.IsConfirmed() && idsToRemove.Contains(transaction.Id))
                        {
                            result.Add((transaction.Id, transaction.CreationTime));
                            address.Transactions = address.Transactions.Except(new[] {transaction}).ToList();
                            i--;
                        }

                        // Remove the spending transaction object containing this transaction id.
                        if (transaction.SpendingDetails != null && !transaction.SpendingDetails.IsSpentConfirmed() &&
                            idsToRemove.Contains(transaction.SpendingDetails.TransactionId))
                        {
                            result.Add((transaction.SpendingDetails.TransactionId,
                                transaction.SpendingDetails.CreationTime));
                            address.Transactions.ElementAt(i).SpendingDetails = null;
                        }
                    }
            }

            if (result.Any())
            {
                // Reload the lookup dictionaries.
                RefreshInputKeysLookupLock();

                SaveWallet(wallet);
            }

            return result;
        }

        /// <inheritdoc />
        public HashSet<(uint256, DateTimeOffset)> RemoveAllTransactions(string walletName)
        {
            Guard.NotEmpty(walletName, nameof(walletName));
            var wallet = GetWallet(walletName);

            var removedTransactions = new HashSet<(uint256, DateTimeOffset)>();

            lock (this.lockObject)
            {
                var accounts = wallet.GetAccounts();
                foreach (var account in accounts)
                foreach (var address in account.GetCombinedAddresses())
                {
                    removedTransactions.UnionWith(address.Transactions.Select(t => (t.Id, t.CreationTime)));
                    address.Transactions.Clear();
                }

                // Reload the lookup dictionaries.
                RefreshInputKeysLookupLock();
            }

            if (removedTransactions.Any()) SaveWallet(wallet);

            return removedTransactions;
        }

        /// <inheritdoc />
        public HashSet<(uint256, DateTimeOffset)> RemoveTransactionsFromDate(string walletName, DateTimeOffset fromDate)
        {
            Guard.NotEmpty(walletName, nameof(walletName));
            var wallet = GetWallet(walletName);

            var removedTransactions = new HashSet<(uint256, DateTimeOffset)>();

            lock (this.lockObject)
            {
                var accounts = wallet.GetAccounts();
                foreach (var account in accounts)
                foreach (var address in account.GetCombinedAddresses())
                {
                    var toRemove = address.Transactions.Where(t => t.CreationTime > fromDate).ToList();
                    foreach (var trx in toRemove)
                    {
                        removedTransactions.Add((trx.Id, trx.CreationTime));
                        address.Transactions.Remove(trx);
                    }
                }

                // Reload the lookup dictionaries.
                RefreshInputKeysLookupLock();
            }

            if (removedTransactions.Any()) SaveWallet(wallet);

            return removedTransactions;
        }

        /// <inheritdoc />
        public ExtKey GetExtKey(WalletAccountReference accountReference, string password = "", bool cache = false)
        {
            var wallet = GetWalletByName(accountReference.WalletName);
            var cacheKey = wallet.EncryptedSeed;
            Key privateKey;

            if (this.privateKeyCache.TryGetValue(cacheKey, out SecureString secretValue))
                privateKey = wallet.Network.CreateBitcoinSecret(secretValue.FromSecureString()).PrivateKey;
            else
                privateKey = Key.Parse(wallet.EncryptedSeed, password, wallet.Network);

            if (cache)
            {
                // The default duration the secret is cached is 5 minutes.
                var timeOutDuration = new TimeSpan(0, 5, 0);
                UnlockWallet(password, accountReference.WalletName, (int) timeOutDuration.TotalSeconds);
            }

            return new ExtKey(privateKey, wallet.ChainCode);
        }

        /// <summary>
        ///     Creates the <see cref="ScriptToAddressLookup" /> object to use.
        /// </summary>
        /// <remarks>
        ///     Override this method and the <see cref="ScriptToAddressLookup" /> object to provide a custom keys lookup.
        /// </remarks>
        /// <returns>A new <see cref="ScriptToAddressLookup" /> object for use by this class.</returns>
        protected virtual ScriptToAddressLookup CreateAddressFromScriptLookup()
        {
            return new ScriptToAddressLookup();
        }

        void BroadcasterManager_TransactionStateChanged(object sender, TransactionBroadcastEntry transactionEntry)
        {
            if (string.IsNullOrEmpty(transactionEntry.ErrorMessage))
            {
                ProcessTransaction(transactionEntry.Transaction, null, null,
                    transactionEntry.State == State.Propagated);
            }
            else
            {
                this.logger.LogDebug("Exception occurred: {0}", transactionEntry.ErrorMessage);
                this.logger.LogTrace("(-)[EXCEPTION]");
            }
        }


        SecureString CacheSecret(string name, string walletPassword, TimeSpan duration)
        {
            var wallet = GetWalletByName(name);
            var cacheKey = wallet.EncryptedSeed;

            if (!this.privateKeyCache.TryGetValue(cacheKey, out SecureString secretValue))
            {
                var privateKey = Key.Parse(wallet.EncryptedSeed, walletPassword, wallet.Network);
                secretValue = privateKey.ToString(wallet.Network).ToSecureString();
            }

            this.privateKeyCache.Set(cacheKey, secretValue, duration);

            return secretValue;
        }

        /// <summary>
        ///     Gets the hash of the last block received by the wallets.
        /// </summary>
        /// <returns>Hash of the last block received by the wallets.</returns>
        public HashHeightPair LastReceivedBlockInfo()
        {
            if (!this.Wallets.Any())
            {
                var chainedHeader = this.ChainIndexer.Tip;
                this.logger.LogTrace("(-)[NO_WALLET]:'{0}'", chainedHeader);
                return new HashHeightPair(chainedHeader);
            }

            AccountRoot accountRoot;
            lock (this.lockObject)
            {
                accountRoot = this.Wallets
                    .Select(w => w.AccountsRoot.Single())
                    .Where(w => w != null)
                    .OrderBy(o => o.LastBlockSyncedHeight)
                    .FirstOrDefault();

                // If details about the last block synced are not present in the wallet,
                // find out which is the oldest wallet and set the last block synced to be the one at this date.
                if (accountRoot == null || accountRoot.LastBlockSyncedHash == null)
                {
                    this.logger.LogWarning("There were no details about the last block synced in the wallets.");
                    var earliestWalletDate = this.Wallets.Min(c => c.CreationTime);
                    UpdateWhenChainDownloaded(this.Wallets, earliestWalletDate.DateTime);
                    return new HashHeightPair(this.ChainIndexer.Tip);
                }
            }

            return new HashHeightPair(accountRoot.LastBlockSyncedHash, accountRoot.LastBlockSyncedHeight.Value);
        }

        public IEnumerable<UnspentOutputReference> GetSpendableTransactionsInWallet(string walletName,
            int confirmations, Func<HdAccount, bool> accountFilter)
        {
            Guard.NotEmpty(walletName, nameof(walletName));

            var wallet = GetWalletByName(walletName);
            UnspentOutputReference[] res = null;
            lock (this.lockObject)
            {
                res = wallet.GetAllSpendableTransactions(this.ChainIndexer.Tip.Height, confirmations, accountFilter)
                    .ToArray();
            }

            return res;
        }

        /// <summary>
        ///     Adds a transaction that credits the wallet with new coins.
        ///     This method is can be called many times for the same transaction (idempotent).
        /// </summary>
        /// <param name="transaction">The transaction from which details are added.</param>
        /// <param name="utxo">The unspent output to add to the wallet.</param>
        /// <param name="blockHeight">Height of the block.</param>
        /// <param name="block">The block containing the transaction to add.</param>
        /// <param name="isPropagated">Propagation state of the transaction.</param>
        void AddTransactionToWallet(Transaction transaction, TxOut utxo, int? blockHeight = null, Block block = null,
            bool isPropagated = true)
        {
            Guard.NotNull(transaction, nameof(transaction));
            Guard.NotNull(utxo, nameof(utxo));

            var transactionHash = transaction.GetHash();

            // Get the collection of transactions to add to.
            var script = utxo.ScriptPubKey;
            this.scriptToAddressLookup.TryGetValue(script, out var address);
            var addressTransactions = address.Transactions;

            // Check if a similar UTXO exists or not (same transaction ID and same index).
            // New UTXOs are added, existing ones are updated.
            var index = transaction.Outputs.IndexOf(utxo);
            var amount = utxo.Value;
            var foundTransaction = addressTransactions.FirstOrDefault(t => t.Id == transactionHash && t.Index == index);
            if (foundTransaction == null)
            {
                this.logger.LogDebug("UTXO '{0}-{1}' not found, creating.", transactionHash, index);
                var newTransaction = new TransactionData
                {
                    Amount = amount,
                    IsCoinBase = transaction.IsCoinBase == false ? (bool?) null : true,
                    IsCoinStake = transaction.IsCoinStake == false ? (bool?) null : true,
                    BlockHeight = blockHeight,
                    BlockHash = block?.GetHash(),
                    BlockIndex = block?.Transactions.FindIndex(t => t.GetHash() == transactionHash),
                    Id = transactionHash,
                    CreationTime =
                        DateTimeOffset.FromUnixTimeSeconds(block?.Header.Time ?? this.dateTimeProvider.GetTime()),
                    Index = index,
                    ScriptPubKey = script,
                    Hex = this.walletSettings.SaveTransactionHex ? transaction.ToHex() : null,
                    IsPropagated = isPropagated
                };

                // Add the Merkle proof to the (non-spending) transaction.
                if (block != null)
                    newTransaction.MerkleProof = new MerkleBlock(block, new[] {transactionHash}).PartialMerkleTree;

                addressTransactions.Add(newTransaction);
                AddInputKeysLookupLocked(newTransaction);

                if (block == null)
                    // Unconfirmed inputs track for double spends.
                    AddTxLookupLocked(newTransaction, transaction);
            }
            else
            {
                this.logger.LogDebug("Transaction ID '{0}' found, updating.", transactionHash);

                // Update the block height and block hash.
                if (foundTransaction.BlockHeight == null && blockHeight != null)
                {
                    foundTransaction.BlockHeight = blockHeight;
                    foundTransaction.BlockHash = block?.GetHash();
                    foundTransaction.BlockIndex = block?.Transactions.FindIndex(t => t.GetHash() == transactionHash);
                }

                // Update the block time.
                if (block != null)
                    foundTransaction.CreationTime = DateTimeOffset.FromUnixTimeSeconds(block.Header.Time);

                // Add the Merkle proof now that the transaction is confirmed in a block.
                if (block != null && foundTransaction.MerkleProof == null)
                    foundTransaction.MerkleProof = new MerkleBlock(block, new[] {transactionHash}).PartialMerkleTree;

                if (isPropagated)
                    foundTransaction.IsPropagated = true;

                if (block != null)
                    // Inputs are in a block no need to track them anymore.
                    RemoveTxLookupLocked(transaction);
            }

            TransactionFoundInternal(script);
        }

        /// <summary>
        ///     Mark an output as spent, the credit of the output will not be used to calculate the balance.
        ///     The output will remain in the wallet for history (and reorg).
        /// </summary>
        /// <param name="transaction">The transaction from which details are added.</param>
        /// <param name="paidToOutputs">A list of payments made out</param>
        /// <param name="spendingTransactionId">
        ///     The id of the transaction containing the output being spent, if this is a spending
        ///     transaction.
        /// </param>
        /// <param name="spendingTransactionIndex">
        ///     The index of the output in the transaction being referenced, if this is a
        ///     spending transaction.
        /// </param>
        /// <param name="blockHeight">Height of the block.</param>
        /// <param name="block">The block containing the transaction to add.</param>
        void AddSpendingTransactionToWallet(Transaction transaction, IEnumerable<TxOut> paidToOutputs,
            uint256 spendingTransactionId, int? spendingTransactionIndex, int? blockHeight = null, Block block = null)
        {
            Guard.NotNull(transaction, nameof(transaction));
            Guard.NotNull(paidToOutputs, nameof(paidToOutputs));

            var transactionHash = transaction.GetHash();

            // Get the transaction being spent.
            var spentTransaction = this.scriptToAddressLookup.Values.Distinct().SelectMany(v => v.Transactions)
                .SingleOrDefault(t => t.Id == spendingTransactionId && t.Index == spendingTransactionIndex);
            if (spentTransaction == null)
            {
                // Strange, why would it be null?
                this.logger.LogTrace("(-)[TX_NULL]");
                return;
            }

            // If the details of this spending transaction are seen for the first time.
            if (spentTransaction.SpendingDetails == null)
            {
                this.logger.LogDebug("Spending UTXO '{0}-{1}' is new.", spendingTransactionId,
                    spendingTransactionIndex);

                var payments = new List<PaymentDetails>();
                foreach (var paidToOutput in paidToOutputs)
                {
                    // Figure out how to retrieve the destination address.
                    var destinationAddress =
                        this.scriptAddressReader.GetAddressFromScriptPubKey(this.network, paidToOutput.ScriptPubKey);
                    if (string.IsNullOrEmpty(destinationAddress))
                        if (this.scriptToAddressLookup.TryGetValue(paidToOutput.ScriptPubKey, out var destination))
                            destinationAddress = destination.Address;

                    payments.Add(new PaymentDetails
                    {
                        DestinationScriptPubKey = paidToOutput.ScriptPubKey,
                        DestinationAddress = destinationAddress,
                        Amount = paidToOutput.Value,
                        OutputIndex = transaction.Outputs.IndexOf(paidToOutput)
                    });
                }

                var spendingDetails = new SpendingDetails
                {
                    TransactionId = transactionHash,
                    Payments = payments,
                    CreationTime =
                        DateTimeOffset.FromUnixTimeSeconds(block?.Header.Time ?? this.dateTimeProvider.GetTime()),
                    BlockHeight = blockHeight,
                    BlockIndex = block?.Transactions.FindIndex(t => t.GetHash() == transactionHash),
                    Hex = this.walletSettings.SaveTransactionHex ? transaction.ToHex() : null,
                    IsCoinStake = transaction.IsCoinStake == false ? (bool?) null : true
                };

                spentTransaction.SpendingDetails = spendingDetails;
                spentTransaction.MerkleProof = null;
            }
            else // If this spending transaction is being confirmed in a block.
            {
                this.logger.LogDebug("Spending transaction ID '{0}' is being confirmed, updating.",
                    spendingTransactionId);

                // Update the block height.
                if (spentTransaction.SpendingDetails.BlockHeight == null && blockHeight != null)
                    spentTransaction.SpendingDetails.BlockHeight = blockHeight;

                // Update the block time to be that of the block in which the transaction is confirmed.
                if (block != null)
                {
                    spentTransaction.SpendingDetails.CreationTime =
                        DateTimeOffset.FromUnixTimeSeconds(block.Header.Time);
                    spentTransaction.BlockIndex = block?.Transactions.FindIndex(t => t.GetHash() == transactionHash);
                }
            }

            // If the transaction is spent and confirmed, we remove the UTXO from the lookup dictionary.
            if (spentTransaction.SpendingDetails.BlockHeight != null) RemoveInputKeysLookupLock(spentTransaction);
        }

        public virtual void TransactionFoundInternal(Script script, Func<HdAccount, bool> accountFilter = null)
        {
            foreach (var wallet in this.Wallets)
            foreach (var account in wallet.GetAccounts(accountFilter ?? Wallet.NormalAccounts))
            {
                bool isChange;
                if (account.ExternalAddresses.Any(address => address.ScriptPubKey == script))
                    isChange = false;
                else if (account.InternalAddresses.Any(address => address.ScriptPubKey == script))
                    isChange = true;
                else
                    continue;

                var newAddresses = AddAddressesToMaintainBuffer(account, isChange);

                UpdateKeysLookupLocked(newAddresses);
            }
        }

        IEnumerable<HdAddress> AddAddressesToMaintainBuffer(HdAccount account, bool isChange)
        {
            var lastUsedAddress = account.GetLastUsedAddress(isChange);
            var lastUsedAddressIndex = lastUsedAddress?.Index ?? -1;
            var addressesCount = isChange ? account.InternalAddresses.Count() : account.ExternalAddresses.Count();
            var emptyAddressesCount = addressesCount - lastUsedAddressIndex - 1;
            var addressesToAdd = this.walletSettings.UnusedAddressesBuffer - emptyAddressesCount;

            return addressesToAdd > 0
                ? account.CreateAddresses(this.network, addressesToAdd, isChange)
                : new List<HdAddress>();
        }

        /// <summary>
        ///     Generates the wallet file.
        /// </summary>
        /// <param name="name">The name of the wallet.</param>
        /// <param name="encryptedSeed">The seed for this wallet, password encrypted.</param>
        /// <param name="chainCode">The chain code.</param>
        /// <param name="creationTime">The time this wallet was created.</param>
        /// <param name="coinType">A BIP44 coin type, this will allow to overwrite the default network coin type.</param>
        /// <returns>The wallet object that was saved into the file system.</returns>
        /// <exception cref="WalletException">Thrown if wallet cannot be created.</exception>
        Wallet GenerateWalletFile(string name, string encryptedSeed, byte[] chainCode,
            DateTimeOffset? creationTime = null, int? coinType = null)
        {
            Guard.NotEmpty(name, nameof(name));
            Guard.NotEmpty(encryptedSeed, nameof(encryptedSeed));
            Guard.NotNull(chainCode, nameof(chainCode));

            // Check if any wallet file already exists, with case insensitive comparison.
            if (this.Wallets.Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                this.logger.LogTrace("(-)[WALLET_ALREADY_EXISTS]");
                throw new WalletException($"Wallet with name '{name}' already exists.");
            }

            var similarWallets = this.Wallets.Where(w => w.EncryptedSeed == encryptedSeed).ToList();
            if (similarWallets.Any())
            {
                this.logger.LogTrace("(-)[SAME_PK_ALREADY_EXISTS]");
                throw new WalletException(
                    "Cannot create this wallet as a wallet with the same private key already exists. If you want to restore your wallet from scratch, " +
                    $"please remove the file {string.Join(", ", similarWallets.Select(w => w.Name))}.{WalletFileExtension} from '{this.fileStorage.FolderPath}' and try restoring the wallet again. " +
                    "Make sure you have your mnemonic and your password handy!");
            }

            var walletFile = new Wallet
            {
                Name = name,
                EncryptedSeed = encryptedSeed,
                ChainCode = chainCode,
                CreationTime = creationTime ?? this.dateTimeProvider.GetTimeOffset(),
                Network = this.network,
                AccountsRoot = new List<AccountRoot>
                    {new AccountRoot {Accounts = new List<HdAccount>(), CoinType = coinType ?? this.coinType}}
            };

            // Create a folder if none exists and persist the file.
            SaveWallet(walletFile);

            return walletFile;
        }

        /// <summary>
        ///     Generates the wallet file without private key and chain code.
        ///     For use with only the extended public key.
        /// </summary>
        /// <param name="name">The name of the wallet.</param>
        /// <param name="creationTime">The time this wallet was created.</param>
        /// <returns>The wallet object that was saved into the file system.</returns>
        /// <exception cref="WalletException">Thrown if wallet cannot be created.</exception>
        Wallet GenerateExtPubKeyOnlyWalletFile(string name, DateTimeOffset? creationTime = null)
        {
            Guard.NotEmpty(name, nameof(name));

            // Check if any wallet file already exists, with case insensitive comparison.
            if (this.Wallets.Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                this.logger.LogTrace("(-)[WALLET_ALREADY_EXISTS]");
                throw new WalletException($"Wallet with name '{name}' already exists.");
            }

            var walletFile = new Wallet
            {
                Name = name,
                IsExtPubKeyWallet = true,
                CreationTime = creationTime ?? this.dateTimeProvider.GetTimeOffset(),
                Network = this.network,
                AccountsRoot = new List<AccountRoot>
                    {new AccountRoot {Accounts = new List<HdAccount>(), CoinType = this.coinType}}
            };

            // Create a folder if none exists and persist the file.
            SaveWallet(walletFile);

            return walletFile;
        }

        /// <summary>
        ///     Loads the wallet to be used by the manager if a wallet with this name has not already been loaded.
        /// </summary>
        /// <param name="wallet">The wallet to load.</param>
        void Load(Wallet wallet)
        {
            Guard.NotNull(wallet, nameof(wallet));

            if (this.Wallets.Any(w => w.Name == wallet.Name))
            {
                this.logger.LogTrace("(-)[NOT_FOUND]");
                return;
            }

            this.Wallets.Add(wallet);
        }

        /// <summary>
        ///     Loads the keys and transactions we're tracking in memory for faster lookups.
        /// </summary>
        public void LoadKeysLookupLock()
        {
            lock (this.lockObject)
            {
                foreach (var wallet in this.Wallets)
                foreach (var account in wallet.GetAccounts(a => true))
                foreach (var address in account.GetCombinedAddresses())
                {
                    AddAddressToIndex(address);

                    foreach (var transaction in address.Transactions)
                        // Get the UTXOs that are unspent or spent but not confirmed.
                        // We only exclude from the list the confirmed spent UTXOs.
                        if (transaction.SpendingDetails?.BlockHeight == null)
                            this.outpointLookup[new OutPoint(transaction.Id, transaction.Index)] = transaction;
                }
            }
        }

        protected virtual void AddAddressToIndex(HdAddress address)
        {
            // Track the P2PKH of this pubic key
            this.scriptToAddressLookup[address.ScriptPubKey] = address;

            // Track the P2PK of this public key
            if (address.Pubkey != null)
                this.scriptToAddressLookup[address.Pubkey] = address;

            // Track the P2WPKH of this pubic key
            if (address.Bech32Address != null)
                this.scriptToAddressLookup[
                    new BitcoinWitPubKeyAddress(address.Bech32Address, this.network).ScriptPubKey] = address;
        }

        /// <summary>
        ///     Update the keys and transactions we're tracking in memory for faster lookups.
        /// </summary>
        public void UpdateKeysLookupLocked(IEnumerable<HdAddress> addresses)
        {
            if (addresses == null || !addresses.Any()) return;

            lock (this.lockObject)
            {
                foreach (var address in addresses) AddAddressToIndex(address);
            }
        }

        /// <summary>
        ///     Add to the list of unspent outputs kept in memory for faster lookups.
        /// </summary>
        void AddInputKeysLookupLocked(TransactionData transactionData)
        {
            Guard.NotNull(transactionData, nameof(transactionData));

            lock (this.lockObject)
            {
                this.outpointLookup[new OutPoint(transactionData.Id, transactionData.Index)] = transactionData;
            }
        }

        /// <summary>
        ///     Remove from the list of unspent outputs kept in memory.
        /// </summary>
        void RemoveInputKeysLookupLock(TransactionData transactionData)
        {
            Guard.NotNull(transactionData, nameof(transactionData));
            Guard.NotNull(transactionData.SpendingDetails, nameof(transactionData.SpendingDetails));

            lock (this.lockObject)
            {
                this.outpointLookup.Remove(new OutPoint(transactionData.Id, transactionData.Index));
            }
        }

        /// <summary>
        ///     Reloads the UTXOs we're tracking in memory for faster lookups.
        /// </summary>
        public void RefreshInputKeysLookupLock()
        {
            lock (this.lockObject)
            {
                this.outpointLookup = new Dictionary<OutPoint, TransactionData>();

                foreach (var wallet in this.Wallets)
                foreach (var address in wallet.GetAllAddresses(a => true))
                    // Get the UTXOs that are unspent or spent but not confirmed.
                    // We only exclude from the list the confirmed spent UTXOs.
                foreach (var transaction in address.Transactions.Where(t => t.SpendingDetails?.BlockHeight == null))
                    this.outpointLookup[new OutPoint(transaction.Id, transaction.Index)] = transaction;
            }
        }

        /// <summary>
        ///     Add to the mapping of transactions kept in memory for faster lookups.
        /// </summary>
        void AddTxLookupLocked(TransactionData transactionData, Transaction transaction)
        {
            Guard.NotNull(transaction, nameof(transaction));
            Guard.NotNull(transactionData, nameof(transactionData));

            lock (this.lockObject)
            {
                foreach (var input in transaction.Inputs.Select(s => s.PrevOut))
                    this.inputLookup[input] = transactionData;
            }
        }

        void RemoveTxLookupLocked(Transaction transaction)
        {
            Guard.NotNull(transaction, nameof(transaction));

            lock (this.lockObject)
            {
                foreach (var input in transaction.Inputs.Select(s => s.PrevOut)) this.inputLookup.Remove(input);
            }
        }

        /// <summary>
        ///     Search all wallets and removes the specified transactions from the wallet and persist it.
        /// </summary>
        void RemoveTransactionsByIds(IEnumerable<uint256> transactionsIds)
        {
            Guard.NotNull(transactionsIds, nameof(transactionsIds));

            foreach (var wallet in this.Wallets) RemoveTransactionsByIds(wallet.Name, transactionsIds);
        }

        /// <summary>
        ///     Updates details of the last block synced in a wallet when the chain of headers finishes downloading.
        /// </summary>
        /// <param name="wallets">The wallets to update when the chain has downloaded.</param>
        /// <param name="date">The creation date of the block with which to update the wallet.</param>
        void UpdateWhenChainDownloaded(IEnumerable<Wallet> wallets, DateTime date)
        {
            if (this.asyncProvider.IsAsyncLoopRunning(DownloadChainLoop)) return;

            this.asyncProvider.CreateAndRunAsyncLoopUntil(DownloadChainLoop, this.nodeLifetime.ApplicationStopping,
                () => this.ChainIndexer.IsDownloaded(),
                () =>
                {
                    var heightAtDate = this.ChainIndexer.GetHeightAtTime(date);

                    foreach (var wallet in wallets)
                    {
                        this.logger.LogDebug(
                            "The chain of headers has finished downloading, updating wallet '{0}' with height {1}",
                            wallet.Name, heightAtDate);
                        UpdateLastBlockSyncedHeight(wallet, this.ChainIndexer.GetHeader(heightAtDate));
                        SaveWallet(wallet);
                    }
                },
                ex =>
                {
                    // In case of an exception while waiting for the chain to be at a certain height, we just cut our losses and
                    // sync from the current height.
                    this.logger.LogError($"Exception occurred while waiting for chain to download: {ex.Message}");

                    foreach (var wallet in wallets) UpdateLastBlockSyncedHeight(wallet, this.ChainIndexer.Tip);
                },
                TimeSpans.FiveSeconds);
        }
    }
}