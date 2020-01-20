using System;

namespace NBitcoin.BouncyCastle.math.ec.multiplier
{
    class FixedPointCombMultiplier
        : AbstractECMultiplier
    {
        protected override ECPoint MultiplyPositive(ECPoint p, BigInteger k)
        {
            var c = p.Curve;
            var size = FixedPointUtilities.GetCombSize(c);

            if (k.BitLength > size)
                /*
                     * TODO The comb works best when the scalars are less than the (possibly unknown) order.
                     * Still, if we want to handle larger scalars, we could allow customization of the comb
                     * size, or alternatively we could deal with the 'extra' bits either by running the comb
                     * multiple times as necessary, or by using an alternative multiplier as prelude.
                     */
                throw new InvalidOperationException(
                    "fixed-point comb doesn't support scalars larger than the curve order");

            var minWidth = GetWidthForCombSize(size);

            var info = FixedPointUtilities.Precompute(p, minWidth);
            var lookupTable = info.PreComp;
            var width = info.Width;

            var d = (size + width - 1) / width;

            var R = c.Infinity;

            var top = d * width - 1;
            for (var i = 0; i < d; ++i)
            {
                var index = 0;

                for (var j = top - i; j >= 0; j -= d)
                {
                    index <<= 1;
                    if (k.TestBit(j)) index |= 1;
                }

                R = R.TwicePlus(lookupTable[index]);
            }

            return R;
        }

        protected virtual int GetWidthForCombSize(int combSize)
        {
            return combSize > 257 ? 6 : 5;
        }
    }
}