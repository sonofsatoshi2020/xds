using System.IO;

namespace NBitcoin.BouncyCastle.asn1
{
    abstract class Asn1Generator
    {
        protected Asn1Generator(
            Stream outStream)
        {
            this.Out = outStream;
        }

        protected Stream Out { get; }

        public abstract void AddObject(Asn1Encodable obj);

        public abstract Stream GetRawOutputStream();

        public abstract void Close();
    }
}