using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.Controllers.Models;
using UnnamedCoin.Bitcoin.Utilities.Extensions;
using UnnamedCoin.Bitcoin.Utilities.JsonErrors;

namespace UnnamedCoin.Bitcoin.Controllers
{
    /// <summary>
    ///     Provides methods that interact with the network elements of the full node.
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public sealed class NetworkController : Controller
    {
        /// <summary>The connection manager.</summary>
        readonly IConnectionManager connectionManager;

        /// <summary>Instance logger.</summary>
        readonly ILogger logger;

        /// <summary>The network the node is running on.</summary>
        readonly Network network;

        /// <summary>Network peer banning behaviour.</summary>
        readonly IPeerBanning peerBanning;

        public NetworkController(
            IConnectionManager connectionManager,
            ILoggerFactory loggerFactory,
            Network network,
            IPeerBanning peerBanning)
        {
            this.connectionManager = connectionManager;
            this.network = network;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.peerBanning = peerBanning;
        }

        /// <summary>
        ///     Disconnects a connected peer.
        /// </summary>
        /// <param name="viewModel">The model that represents the peer to disconnect.</param>
        /// <returns>
        ///     <see cref="OkResult" />
        /// </returns>
        [Route("disconnect")]
        [HttpPost]
        public IActionResult DisconnectPeer([FromBody] DisconnectPeerViewModel viewModel)
        {
            try
            {
                var endpoint = viewModel.PeerAddress.ToIPEndPoint(this.network.DefaultPort);
                var peer = this.connectionManager.ConnectedPeers.FindByEndpoint(endpoint);
                if (peer != null)
                {
                    var peerBehavior = peer.Behavior<IConnectionManagerBehavior>();
                    peer.Disconnect($"{endpoint} was disconnected via the API.");
                }

                return Ok();
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        ///     Adds or remove a peer from the node's banned peers list.
        /// </summary>
        /// <param name="viewModel">The model that represents the peer to add or remove from the banned list.</param>
        /// <returns>
        ///     <see cref="OkResult" />
        /// </returns>
        [Route("setban")]
        [HttpPost]
        public IActionResult SetBan([FromBody] SetBanPeerViewModel viewModel)
        {
            try
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(viewModel.PeerAddress), this.network.DefaultPort);
                switch (viewModel.BanCommand.ToLowerInvariant())
                {
                    case "add":
                    {
                        var banDuration = this.connectionManager.ConnectionSettings.BanTimeSeconds;
                        if (viewModel.BanDurationSeconds != null && viewModel.BanDurationSeconds.Value > 0)
                            banDuration = viewModel.BanDurationSeconds.Value;

                        this.peerBanning.BanAndDisconnectPeer(endpoint, banDuration, "Banned via the API.");

                        break;
                    }

                    case "remove":
                    {
                        this.peerBanning.UnBanPeer(endpoint);
                        break;
                    }

                    default:
                        throw new Exception("Only 'add' or 'remove' are valid 'setban' commands.");
                }

                return Ok();
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("getbans")]
        [HttpGet]
        public IActionResult GetBans()
        {
            try
            {
                var allBannedPeers = this.peerBanning.GetAllBanned();

                return Json(allBannedPeers.Select(p => new BannedPeerModel
                    {EndPoint = p.Endpoint.ToString(), BanUntil = p.BanUntil, BanReason = p.BanReason}));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        ///     Clears the node of all banned peers.
        /// </summary>
        /// <param name="corsProtection">This body parameter is here to prevent a CORS call from triggering method execution.</param>
        /// <remarks>
        ///     <seealso cref="https://developer.mozilla.org/en-US/docs/Web/HTTP/CORS#Simple_requests" />
        /// </remarks>
        /// <returns>
        ///     <see cref="OkResult" />
        /// </returns>
        [Route("clearbanned")]
        [HttpPost]
        public IActionResult ClearBannedPeers([FromBody] bool corsProtection = true)
        {
            try
            {
                this.peerBanning.ClearBannedPeers();

                return Ok();
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}