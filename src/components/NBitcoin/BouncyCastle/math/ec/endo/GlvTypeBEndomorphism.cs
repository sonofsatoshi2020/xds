namespace NBitcoin.BouncyCastle.math.ec.endo
{
    class GlvTypeBEndomorphism
        : GlvEndomorphism
    {
        protected readonly ECCurve m_curve;
        protected readonly GlvTypeBParameters m_parameters;
        protected readonly ECPointMap m_pointMap;

        public GlvTypeBEndomorphism(ECCurve curve, GlvTypeBParameters parameters)
        {
            this.m_curve = curve;
            this.m_parameters = parameters;
            this.m_pointMap = new ScaleXPointMap(curve.FromBigInteger(parameters.Beta));
        }

        public virtual BigInteger[] DecomposeScalar(BigInteger k)
        {
            var bits = this.m_parameters.Bits;
            var b1 = CalculateB(k, this.m_parameters.G1, bits);
            var b2 = CalculateB(k, this.m_parameters.G2, bits);

            BigInteger[] v1 = this.m_parameters.V1, v2 = this.m_parameters.V2;
            var a = k.Subtract(b1.Multiply(v1[0]).Add(b2.Multiply(v2[0])));
            var b = b1.Multiply(v1[1]).Add(b2.Multiply(v2[1])).Negate();

            return new[] {a, b};
        }

        public virtual ECPointMap PointMap => this.m_pointMap;

        public virtual bool HasEfficientPointMap => true;

        protected virtual BigInteger CalculateB(BigInteger k, BigInteger g, int t)
        {
            var negative = g.SignValue < 0;
            var b = k.Multiply(g.Abs());
            var extra = b.TestBit(t - 1);
            b = b.ShiftRight(t);
            if (extra) b = b.Add(BigInteger.One);
            return negative ? b.Negate() : b;
        }
    }
}