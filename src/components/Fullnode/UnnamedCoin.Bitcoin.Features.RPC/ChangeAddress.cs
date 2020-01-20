using NBitcoin;

namespace UnnamedCoin.Bitcoin.Features.RPC
{
    public class ChangeAddress
    {
        public Money Amount { get; set; }
        public BitcoinAddress Address { get; set; }
    }
}