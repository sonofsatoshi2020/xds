using NBitcoin;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    public class BitcoinSerializablePayload<T> : Payload where T : IBitcoinSerializable, new()
    {
        T obj;

        public BitcoinSerializablePayload()
        {
        }

        public BitcoinSerializablePayload(T obj)
        {
            this.obj = obj;
        }

        public T Obj
        {
            get => this.obj;
            set => this.obj = value;
        }


        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.obj);
        }
    }
}