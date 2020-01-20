using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Controllers;
using UnnamedCoin.Bitcoin.Features.Miner.Interfaces;
using UnnamedCoin.Bitcoin.Features.RPC;
using UnnamedCoin.Bitcoin.Features.RPC.Exceptions;
using UnnamedCoin.Bitcoin.Features.Wallet;
using UnnamedCoin.Bitcoin.Features.Wallet.Interfaces;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Miner.Controllers
{
    /// <summary>
    ///     RPC controller for calls related to PoW mining and PoS minting.
    /// </summary>
    [ApiVersion("1")]
    [Controller]
    public class MiningRpcController : FeatureController
    {
        /// <summary>Full node.</summary>
        readonly IFullNode fullNode;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>PoW miner.</summary>
        readonly IPowMining powMining;

        /// <summary>Wallet manager.</summary>
        readonly IWalletManager walletManager;

        /// <summary>
        ///     Initializes a new instance of the object.
        /// </summary>
        /// <param name="powMining">PoW miner.</param>
        /// <param name="fullNode">Full node to offer mining RPC.</param>
        /// <param name="loggerFactory">Factory to be used to create logger for the node.</param>
        /// <param name="walletManager">The wallet manager.</param>
        public MiningRpcController(IPowMining powMining, IFullNode fullNode, ILoggerFactory loggerFactory,
            IWalletManager walletManager) : base(fullNode)
        {
            Guard.NotNull(powMining, nameof(powMining));
            Guard.NotNull(fullNode, nameof(fullNode));
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(walletManager, nameof(walletManager));

            this.fullNode = fullNode;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.walletManager = walletManager;
            this.powMining = powMining;
        }

        /// <summary>
        ///     Tries to mine one or more blocks.
        /// </summary>
        /// <param name="blockCount">Number of blocks to mine.</param>
        /// <returns>List of block header hashes of newly mined blocks.</returns>
        /// <remarks>
        ///     It is possible that less than the required number of blocks will be mined because the generating function only
        ///     tries all possible header nonces values.
        /// </remarks>
        [ActionName("generate")]
        [ActionDescription("Tries to mine a given number of blocks and returns a list of block header hashes.")]
        public List<uint256> Generate(int blockCount)
        {
            if (blockCount <= 0)
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_REQUEST,
                    "The number of blocks to mine must be higher than zero.");

            var accountReference = GetAccount();
            var address = this.walletManager.GetUnusedAddress(accountReference);

            var res = this.powMining.GenerateBlocks(new ReserveScript(address.Pubkey), (ulong) blockCount,
                int.MaxValue);
            return res;
        }

        /// <summary>
        ///     Finds first available wallet and its account.
        /// </summary>
        /// <returns>Reference to wallet account.</returns>
        WalletAccountReference GetAccount()
        {
            var walletName = this.walletManager.GetWalletsNames().FirstOrDefault();
            if (walletName == null)
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_REQUEST, "No wallet found");

            var account = this.walletManager.GetAccounts(walletName).FirstOrDefault();
            if (account == null)
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_REQUEST, "No account found on wallet");

            var res = new WalletAccountReference(walletName, account.Name);

            return res;
        }
    }
}