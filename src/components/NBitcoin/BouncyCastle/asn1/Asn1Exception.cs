using System;
using System.IO;

namespace NBitcoin.BouncyCastle.asn1
{
    class Asn1Exception
        : IOException
    {
        public Asn1Exception()
        {
        }

        public Asn1Exception(
            string message)
            : base(message)
        {
        }

        public Asn1Exception(
            string message,
            Exception exception)
            : base(message, exception)
        {
        }
    }
}