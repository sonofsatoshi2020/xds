using Newtonsoft.Json;

namespace UnnamedCoin.Bitcoin.Features.RPC.Models
{
    /// <summary>
    ///     This model defines a list item returned by the RPC API method "listmethods".
    /// </summary>
    public class RpcCommandModel
    {
        [JsonProperty(PropertyName = "command")]
        public string Command { get; set; }

        [JsonProperty(PropertyName = "description")]
        public string Description { get; set; }
    }
}