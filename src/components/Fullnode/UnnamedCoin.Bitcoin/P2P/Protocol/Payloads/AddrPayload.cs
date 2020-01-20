using System.Linq;
using NBitcoin;
using NBitcoin.Protocol;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    /// <summary>
    ///     An available peer address in the bitcoin network is announce (unsollicited or after a getaddr).
    /// </summary>
    [Payload("addr")]
    public class AddrPayload : Payload, IBitcoinSerializable
    {
        NetworkAddress[] addresses = new NetworkAddress[0];

        public AddrPayload()
        {
        }

        public AddrPayload(NetworkAddress address)
        {
            this.addresses = new[] {address};
        }

        public AddrPayload(NetworkAddress[] addresses)
        {
            this.addresses = addresses.ToArray();
        }

        public NetworkAddress[] Addresses => this.addresses;

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.addresses);
        }

        public override string ToString()
        {
            return this.Addresses.Length + " address(es)";
        }
    }
}