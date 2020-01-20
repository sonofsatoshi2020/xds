using NBitcoin;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    public class UnknowPayload : Payload
    {
        string command;

        byte[] data = new byte[0];

        public UnknowPayload()
        {
        }

        public UnknowPayload(string command)
        {
            this.command = command;
        }

        public override string Command => this.command;

        public byte[] Data
        {
            get => this.data;
            set => this.data = value;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.data);
        }

        internal void UpdateCommand(string command)
        {
            this.command = command;
        }
    }
}