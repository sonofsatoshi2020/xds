using System;

namespace NBitcoin.BouncyCastle.security
{
    class KeyException : GeneralSecurityException
    {
        public KeyException()
        {
        }

        public KeyException(string message) : base(message)
        {
        }

        public KeyException(string message, Exception exception) : base(message, exception)
        {
        }
    }
}