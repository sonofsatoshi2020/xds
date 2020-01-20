using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
using UnnamedCoin.Bitcoin.Utilities.JsonConverters;

namespace UnnamedCoin.Bitcoin.Features.Wallet.Models
{
    public class ListSinceBlockModel
    {
        [JsonProperty("transactions")]
        public IList<ListSinceBlockTransactionModel> Transactions { get; } = new List<ListSinceBlockTransactionModel>();

        [JsonProperty("lastblock", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 LastBlock { get; set; }
    }
}