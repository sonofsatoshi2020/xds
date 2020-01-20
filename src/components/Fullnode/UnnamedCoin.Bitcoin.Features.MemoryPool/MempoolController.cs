using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Controllers;
using UnnamedCoin.Bitcoin.Utilities;
using UnnamedCoin.Bitcoin.Utilities.JsonErrors;

namespace UnnamedCoin.Bitcoin.Features.MemoryPool
{
    /// <summary>
    ///     Controller providing operations on the Mempool.
    /// </summary>
    [ApiVersion("1")]
    public class MempoolController : FeatureController
    {
        readonly ILogger logger;

        public MempoolController(ILoggerFactory loggerFactory, MempoolManager mempoolManager)
        {
            Guard.NotNull(mempoolManager, nameof(mempoolManager));

            this.MempoolManager = mempoolManager;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
        }

        public MempoolManager MempoolManager { get; }

        [ActionName("getrawmempool")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [ActionDescription("Lists the contents of the memory pool.")]
        public Task<List<uint256>> GetRawMempool()
        {
            return this.MempoolManager.GetMempoolAsync();
        }

        /// <summary>
        ///     Gets a hash of each transaction in the memory pool. In other words, a list of the TX IDs for all the transactions
        ///     in the mempool are retrieved.
        /// </summary>
        /// <returns>
        ///     Json formatted <see cref="List{T}<see cref="uint256" />"/> containing the memory pool contents. Returns
        ///     <see cref="IActionResult" /> formatted error if fails.
        /// </returns>
        [Route("api/[controller]/getrawmempool")]
        [HttpGet]
        public async Task<IActionResult> GetRawMempoolAsync()
        {
            try
            {
                return Json(await GetRawMempool().ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}