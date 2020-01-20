using System;
using NBitcoin.BouncyCastle.math.ec.abc;

namespace NBitcoin.BouncyCastle.math.ec.multiplier
{
    /**
     * Class implementing the WTNAF (Window
     * <code>&#964;</code>
     * -adic Non-Adjacent Form) algorithm.
     */
    class WTauNafMultiplier
        : AbstractECMultiplier
    {
        // TODO Create WTauNafUtilities class and move various functionality into it
        internal static readonly string PRECOMP_NAME = "bc_wtnaf";

        /**
         * Multiplies a {@link NBitcoin.BouncyCastle.math.ec.AbstractF2mPoint AbstractF2mPoint}
         * by
         * <code>k</code>
         * using the reduced
         * <code>&#964;</code>
         * -adic NAF (RTNAF)
         * method.
         * @param p The AbstractF2mPoint to multiply.
         * @param k The integer by which to multiply
         * <code>k</code>
         * .
         * @return
         * <code>p</code>
         * multiplied by
         * <code>k</code>
         * .
         */
        protected override ECPoint MultiplyPositive(ECPoint point, BigInteger k)
        {
            if (!(point is AbstractF2mPoint))
                throw new ArgumentException("Only AbstractF2mPoint can be used in WTauNafMultiplier");

            var p = (AbstractF2mPoint) point;
            var curve = (AbstractF2mCurve) p.Curve;
            var m = curve.FieldSize;
            var a = (sbyte) curve.A.ToBigInteger().IntValue;
            var mu = Tnaf.GetMu(a);
            var s = curve.GetSi();

            var rho = Tnaf.PartModReduction(k, m, a, s, mu, 10);

            return MultiplyWTnaf(p, rho, curve.GetPreCompInfo(p, PRECOMP_NAME), a, mu);
        }

        /**
         * Multiplies a {@link NBitcoin.BouncyCastle.math.ec.AbstractF2mPoint AbstractF2mPoint}
         * by an element
         * <code>&#955;</code>
         * of
         * <code><b>Z</b>[&#964;]</code>
         * using
         * the
         * <code>&#964;</code>
         * -adic NAF (TNAF) method.
         * @param p The AbstractF2mPoint to multiply.
         * @param lambda The element
         * <code>&#955;</code>
         * of
         * <code><b>Z</b>[&#964;]</code>
         * of which to compute the
         * <code>[&#964;]</code>
         * -adic NAF.
         * @return
         * <code>p</code>
         * multiplied by
         * <code>&#955;</code>
         * .
         */
        AbstractF2mPoint MultiplyWTnaf(AbstractF2mPoint p, ZTauElement lambda,
            PreCompInfo preCompInfo, sbyte a, sbyte mu)
        {
            var alpha = a == 0 ? Tnaf.Alpha0 : Tnaf.Alpha1;

            var tw = Tnaf.GetTw(mu, Tnaf.Width);

            var u = Tnaf.TauAdicWNaf(mu, lambda, Tnaf.Width,
                BigInteger.ValueOf(Tnaf.Pow2Width), tw, alpha);

            return MultiplyFromWTnaf(p, u, preCompInfo);
        }

        /**
         * Multiplies a {@link NBitcoin.BouncyCastle.math.ec.AbstractF2mPoint AbstractF2mPoint}
         * by an element
         * <code>&#955;</code>
         * of
         * <code><b>Z</b>[&#964;]</code>
         * using the window
         * <code>&#964;</code>
         * -adic NAF (TNAF) method, given the
         * WTNAF of
         * <code>&#955;</code>
         * .
         * @param p The AbstractF2mPoint to multiply.
         * @param u The the WTNAF of
         * <code>&#955;</code>
         * ..
         * @return
         * <code>&#955; * p</code>
         */
        static AbstractF2mPoint MultiplyFromWTnaf(AbstractF2mPoint p, sbyte[] u, PreCompInfo preCompInfo)
        {
            var curve = (AbstractF2mCurve) p.Curve;
            var a = (sbyte) curve.A.ToBigInteger().IntValue;

            AbstractF2mPoint[] pu;
            if (preCompInfo == null || !(preCompInfo is WTauNafPreCompInfo))
            {
                pu = Tnaf.GetPreComp(p, a);

                var pre = new WTauNafPreCompInfo();
                pre.PreComp = pu;
                curve.SetPreCompInfo(p, PRECOMP_NAME, pre);
            }
            else
            {
                pu = ((WTauNafPreCompInfo) preCompInfo).PreComp;
            }

            // TODO Include negations in precomp (optionally) and use from here
            var puNeg = new AbstractF2mPoint[pu.Length];
            for (var i = 0; i < pu.Length; ++i) puNeg[i] = (AbstractF2mPoint) pu[i].Negate();


            // q = infinity
            var q = (AbstractF2mPoint) p.Curve.Infinity;

            var tauCount = 0;
            for (var i = u.Length - 1; i >= 0; i--)
            {
                ++tauCount;
                int ui = u[i];
                if (ui != 0)
                {
                    q = q.TauPow(tauCount);
                    tauCount = 0;

                    ECPoint x = ui > 0 ? pu[ui >> 1] : puNeg[-ui >> 1];
                    q = (AbstractF2mPoint) q.Add(x);
                }
            }

            if (tauCount > 0) q = q.TauPow(tauCount);
            return q;
        }
    }
}