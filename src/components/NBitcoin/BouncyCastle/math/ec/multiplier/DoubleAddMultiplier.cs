namespace NBitcoin.BouncyCastle.math.ec.multiplier
{
    class DoubleAddMultiplier
        : AbstractECMultiplier
    {
        /**
         * Joye's double-add algorithm.
         */
        protected override ECPoint MultiplyPositive(ECPoint p, BigInteger k)
        {
            var R = new[] {p.Curve.Infinity, p};

            var n = k.BitLength;
            for (var i = 0; i < n; ++i)
            {
                var b = k.TestBit(i) ? 1 : 0;
                var bp = 1 - b;
                R[bp] = R[bp].TwicePlus(R[b]);
            }

            return R[0];
        }
    }
}