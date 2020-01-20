using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.RPC
{
    public interface IRPCRouteHandler : IRouter
    {
    }

    public class RPCRouteHandler : IRPCRouteHandler
    {
        readonly IActionDescriptorCollectionProvider actionDescriptor;
        readonly IRouter inner;

        public RPCRouteHandler(IRouter inner, IActionDescriptorCollectionProvider actionDescriptor)
        {
            Guard.NotNull(inner, nameof(inner));
            Guard.NotNull(actionDescriptor, nameof(actionDescriptor));

            this.inner = inner;
            this.actionDescriptor = actionDescriptor;
        }

        public VirtualPathData GetVirtualPath(VirtualPathContext context)
        {
            Guard.NotNull(context, nameof(context));

            return this.inner.GetVirtualPath(context);
        }

        public async Task RouteAsync(RouteContext context)
        {
            Guard.NotNull(context, nameof(context));

            JToken request;
            using (var streamReader = new StreamReader(context.HttpContext.Request.Body))
            using (var textReader = new JsonTextReader(streamReader))
            {
                // Ensure floats are parsed as decimals and not as doubles.
                textReader.FloatParseHandling = FloatParseHandling.Decimal;
                request = await JObject.LoadAsync(textReader);
            }

            var method = (string) request["method"];
            var controllerName = this.actionDescriptor.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
                                     .FirstOrDefault(w => w.ActionName == method)?.ControllerName ?? string.Empty;

            context.RouteData.Values.Add("action", method);
            //TODO: Need to be extensible
            context.RouteData.Values.Add("controller", controllerName);
            context.RouteData.Values.Add("req", request);
            await this.inner.RouteAsync(context);
        }
    }
}