using System;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Base;
using UnnamedCoin.Bitcoin.Base.Deployments.Models;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Controllers;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.JsonErrors;

namespace UnnamedCoin.Bitcoin.Features.Consensus
{
    /// <summary>
    ///     A <see cref="FeatureController" /> that provides API and RPC methods from the consensus loop.
    /// </summary>
    [ApiVersion("1")]
    public class ConsensusController : FeatureController
    {
        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        public ConsensusController(
            ILoggerFactory loggerFactory,
            IChainState chainState,
            IConsensusManager consensusManager,
            ChainIndexer chainIndexer)
            : base(chainState: chainState, consensusManager: consensusManager, chainIndexer: chainIndexer)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(chainIndexer, nameof(chainIndexer));
            Guard.NotNull(chainState, nameof(chainState));

            this.logger = loggerFactory.CreateLogger(GetType().FullName);
        }

        /// <summary>
        ///     Implements the getbestblockhash RPC call.
        /// </summary>
        /// <returns>A <see cref="uint256" /> hash of the block at the consensus tip.</returns>
        [ActionName("getbestblockhash")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [ActionDescription("Get the hash of the block at the consensus tip.")]
        public uint256 GetBestBlockHashRPC()
        {
            return this.ChainState.ConsensusTip?.HashBlock;
        }

        /// <summary>
        ///     Get the threshold states of softforks currently being deployed.
        ///     Allowable states are: Defined, Started, LockedIn, Failed, Active.
        /// </summary>
        /// <returns>
        ///     A <see cref="JsonResult" /> object derived from a list of
        ///     <see cref="ThresholdStateModel" /> objects - one per deployment.
        ///     Returns an <see cref="ErrorResult" /> if the method fails.
        /// </returns>
        [Route("api/[controller]/deploymentflags")]
        [HttpGet]
        public IActionResult DeploymentFlags()
        {
            try
            {
                var ruleEngine = this.ConsensusManager.ConsensusRules as ConsensusRuleEngine;

                // Ensure threshold conditions cached.
                var thresholdStates = ruleEngine.NodeDeployments.BIP9.GetStates(this.ChainState.ConsensusTip.Previous);

                var metrics =
                    ruleEngine.NodeDeployments.BIP9.GetThresholdStateMetrics(this.ChainState.ConsensusTip.Previous,
                        thresholdStates);

                return Json(metrics);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        ///     Gets the hash of the block at the consensus tip.
        /// </summary>
        /// <returns>
        ///     Json formatted <see cref="uint256" /> hash of the block at the consensus tip. Returns
        ///     <see cref="IActionResult" /> formatted error if fails.
        /// </returns>
        /// <remarks>This is an API implementation of an RPC call.</remarks>
        [Route("api/[controller]/getbestblockhash")]
        [HttpGet]
        public IActionResult GetBestBlockHashAPI()
        {
            try
            {
                return Json(GetBestBlockHashRPC());
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        ///     Implements the getblockhash RPC call.
        /// </summary>
        /// <param name="height">The requested block height.</param>
        /// <returns>A <see cref="uint256" /> hash of the block at the given height. <c>Null</c> if block not found.</returns>
        [ActionName("getblockhash")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [ActionDescription("Gets the hash of the block at the given height.")]
        public uint256 GetBlockHashRPC(int height)
        {
            this.logger.LogDebug("GetBlockHash {0}", height);

            var bestBlockHash = this.ConsensusManager.Tip?.HashBlock;
            var bestBlock = bestBlockHash == null ? null : this.ChainIndexer.GetHeader(bestBlockHash);
            if (bestBlock == null)
                return null;
            var block = this.ChainIndexer.GetHeader(height);
            var hash = block == null || block.Height > bestBlock.Height ? null : block.HashBlock;

            if (hash == null)
                throw new BlockNotFoundException($"No block found at height {height}");

            return hash;
        }

        /// <summary>
        ///     Gets the hash of the block at a given height.
        /// </summary>
        /// <param name="height">The height of the block to get the hash for.</param>
        /// <returns>
        ///     Json formatted <see cref="uint256" /> hash of the block at the given height. <c>Null</c> if block not found.
        ///     Returns <see cref="IActionResult" /> formatted error if fails.
        /// </returns>
        /// <remarks>This is an API implementation of an RPC call.</remarks>
        [Route("api/[controller]/getblockhash")]
        [HttpGet]
        public IActionResult GetBlockHashAPI([FromQuery] int height)
        {
            try
            {
                return Json(GetBlockHashRPC(height));
            }
            catch (Exception e)
            {
                this.logger.LogTrace("(-)[EXCEPTION]");
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}