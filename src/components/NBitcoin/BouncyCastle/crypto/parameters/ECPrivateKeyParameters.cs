using System;
using NBitcoin.BouncyCastle.math;

namespace NBitcoin.BouncyCastle.crypto.parameters
{
    class ECPrivateKeyParameters
        : ECKeyParameters
    {
        public ECPrivateKeyParameters(
            BigInteger d,
            ECDomainParameters parameters)
            : this("EC", d, parameters)
        {
        }

        public ECPrivateKeyParameters(
            string algorithm,
            BigInteger d,
            ECDomainParameters parameters)
            : base(algorithm, true, parameters)
        {
            if (d == null)
                throw new ArgumentNullException("d");

            this.D = d;
        }

        public BigInteger D { get; }

        public override bool Equals(
            object obj)
        {
            if (obj == this)
                return true;

            var other = obj as ECPrivateKeyParameters;

            if (other == null)
                return false;

            return Equals(other);
        }

        protected bool Equals(
            ECPrivateKeyParameters other)
        {
            return this.D.Equals(other.D) && base.Equals(other);
        }

        public override int GetHashCode()
        {
            return this.D.GetHashCode() ^ base.GetHashCode();
        }
    }
}