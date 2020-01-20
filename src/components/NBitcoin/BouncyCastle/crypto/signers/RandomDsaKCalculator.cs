using System;
using NBitcoin.BouncyCastle.math;
using NBitcoin.BouncyCastle.security;

namespace NBitcoin.BouncyCastle.crypto.signers
{
    class RandomDsaKCalculator
        : IDsaKCalculator
    {
        BigInteger q;
        SecureRandom random;

        public virtual bool IsDeterministic => false;

        public virtual void Init(BigInteger n, SecureRandom random)
        {
            this.q = n;
            this.random = random;
        }

        public virtual void Init(BigInteger n, BigInteger d, byte[] message)
        {
            throw new InvalidOperationException("Operation not supported");
        }

        public virtual BigInteger NextK()
        {
            var qBitLength = this.q.BitLength;

            BigInteger k;
            do
            {
                k = new BigInteger(qBitLength, this.random);
            } while (k.SignValue < 1 || k.CompareTo(this.q) >= 0);

            return k;
        }
    }
}