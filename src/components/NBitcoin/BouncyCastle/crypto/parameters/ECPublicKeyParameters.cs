using System;
using NBitcoin.BouncyCastle.math.ec;

namespace NBitcoin.BouncyCastle.crypto.parameters
{
    class ECPublicKeyParameters
        : ECKeyParameters
    {
        public ECPublicKeyParameters(
            ECPoint q,
            ECDomainParameters parameters)
            : this("EC", q, parameters)
        {
        }

        public ECPublicKeyParameters(
            string algorithm,
            ECPoint q,
            ECDomainParameters parameters)
            : base(algorithm, false, parameters)
        {
            if (q == null)
                throw new ArgumentNullException("q");

            this.Q = q.Normalize();
        }

        public ECPoint Q { get; }

        public override bool Equals(object obj)
        {
            if (obj == this)
                return true;

            var other = obj as ECPublicKeyParameters;

            if (other == null)
                return false;

            return Equals(other);
        }

        protected bool Equals(
            ECPublicKeyParameters other)
        {
            return this.Q.Equals(other.Q) && base.Equals(other);
        }

        public override int GetHashCode()
        {
            return this.Q.GetHashCode() ^ base.GetHashCode();
        }
    }
}