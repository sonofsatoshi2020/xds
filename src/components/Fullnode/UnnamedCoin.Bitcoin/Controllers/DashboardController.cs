using Microsoft.AspNetCore.Mvc;
using UnnamedCoin.Bitcoin.AsyncWork;

namespace UnnamedCoin.Bitcoin.Controllers
{
    /// <summary>
    ///     Controller providing HTML Dashboard
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class DashboardController : Controller
    {
        readonly IAsyncProvider asyncProvider;
        readonly IFullNode fullNode;

        public DashboardController(IFullNode fullNode, IAsyncProvider asyncProvider)
        {
            this.fullNode = fullNode;
            this.asyncProvider = asyncProvider;
        }

        /// <summary>
        ///     Gets a web page containing the last log output for this node.
        /// </summary>
        /// <returns>text/html content</returns>
        [HttpGet]
        [Route("Stats")]
        public IActionResult Stats()
        {
            var content = (this.fullNode as FullNode).LastLogOutput;
            return Content(content);
        }

        /// <summary>
        ///     Returns a web page with Async Loops statistics
        /// </summary>
        /// <returns>text/html content</returns>
        [HttpGet]
        [Route("AsyncLoopsStats")]
        public IActionResult AsyncLoopsStats()
        {
            return Content(this.asyncProvider.GetStatistics(false));
        }
    }
}