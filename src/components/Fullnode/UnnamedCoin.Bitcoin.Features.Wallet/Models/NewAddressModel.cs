using Newtonsoft.Json;
using UnnamedCoin.Bitcoin.Controllers.Converters;

namespace UnnamedCoin.Bitcoin.Features.Wallet.Models
{
    [JsonConverter(typeof(ToStringJsonConverter))]
    public class NewAddressModel
    {
        public NewAddressModel(string address)
        {
            this.Address = address;
        }

        public string Address { get; set; }

        public override string ToString()
        {
            return this.Address;
        }
    }
}