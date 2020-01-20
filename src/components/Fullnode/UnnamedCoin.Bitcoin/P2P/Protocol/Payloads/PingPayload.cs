using NBitcoin;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    [Payload("ping")]
    public class PingPayload : Payload
    {
        ulong nonce;

        public PingPayload()
        {
            this.nonce = RandomUtils.GetUInt64();
        }

        public ulong Nonce
        {
            get => this.nonce;
            set => this.nonce = value;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.nonce);
        }

        public PongPayload CreatePong()
        {
            return new PongPayload
            {
                Nonce = this.Nonce
            };
        }

        public override string ToString()
        {
            return base.ToString() + " : " + this.Nonce;
        }
    }
}