namespace NBitcoin.BouncyCastle.math.ec.endo
{
    interface GlvEndomorphism
        : ECEndomorphism
    {
        BigInteger[] DecomposeScalar(BigInteger k);
    }
}