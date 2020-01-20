using System;
using NBitcoin.BouncyCastle.math;
using NBitcoin.BouncyCastle.math.ec;
using NBitcoin.BouncyCastle.util;

namespace NBitcoin.BouncyCastle.crypto.parameters
{
    class ECDomainParameters
    {
        internal ECCurve curve;
        internal ECPoint g;
        internal BigInteger h;
        internal BigInteger n;
        internal byte[] seed;

        public ECDomainParameters(
            ECCurve curve,
            ECPoint g,
            BigInteger n)
            : this(curve, g, n, BigInteger.One)
        {
        }

        public ECDomainParameters(
            ECCurve curve,
            ECPoint g,
            BigInteger n,
            BigInteger h)
            : this(curve, g, n, h, null)
        {
        }

        public ECDomainParameters(
            ECCurve curve,
            ECPoint g,
            BigInteger n,
            BigInteger h,
            byte[] seed)
        {
            if (curve == null)
                throw new ArgumentNullException("curve");
            if (g == null)
                throw new ArgumentNullException("g");
            if (n == null)
                throw new ArgumentNullException("n");
            if (h == null)
                throw new ArgumentNullException("h");

            this.curve = curve;
            this.g = g.Normalize();
            this.n = n;
            this.h = h;
            this.seed = Arrays.Clone(seed);
        }

        public ECCurve Curve => this.curve;

        public ECPoint G => this.g;

        public BigInteger N => this.n;

        public BigInteger H => this.h;

        public byte[] GetSeed()
        {
            return Arrays.Clone(this.seed);
        }

        public override bool Equals(
            object obj)
        {
            if (obj == this)
                return true;

            var other = obj as ECDomainParameters;

            if (other == null)
                return false;

            return Equals(other);
        }

        protected bool Equals(
            ECDomainParameters other)
        {
            return this.curve.Equals(other.curve)
                   && this.g.Equals(other.g)
                   && this.n.Equals(other.n)
                   && this.h.Equals(other.h)
                   && Arrays.AreEqual(this.seed, other.seed);
        }

        public override int GetHashCode()
        {
            return this.curve.GetHashCode()
                   ^ this.g.GetHashCode()
                   ^ this.n.GetHashCode()
                   ^ this.h.GetHashCode()
                   ^ Arrays.GetHashCode(this.seed);
        }
    }
}