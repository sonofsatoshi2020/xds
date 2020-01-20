using System;
using System.IO;

namespace NBitcoin.BouncyCastle.util.io
{
    class StreamOverflowException
        : IOException
    {
        public StreamOverflowException()
        {
        }

        public StreamOverflowException(
            string message)
            : base(message)
        {
        }

        public StreamOverflowException(
            string message,
            Exception exception)
            : base(message, exception)
        {
        }
    }
}