using NBitcoin;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    [Payload("pong")]
    public class PongPayload : Payload
    {
        ulong nonce;

        public ulong Nonce
        {
            get => this.nonce;
            set => this.nonce = value;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.nonce);
        }

        public override string ToString()
        {
            return base.ToString() + " : " + this.Nonce;
        }
    }
}