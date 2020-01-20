using System.Reflection;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    public class Payload : IBitcoinSerializable
    {
        public virtual string Command => GetType().GetCustomAttribute<PayloadAttribute>().Name;


        public void ReadWrite(BitcoinStream stream)
        {
            using (stream.SerializationTypeScope(SerializationType.Network))
            {
                ReadWriteCore(stream);
            }
        }


        public virtual void ReadWriteCore(BitcoinStream stream)
        {
        }


        public override string ToString()
        {
            return GetType().Name;
        }
    }
}