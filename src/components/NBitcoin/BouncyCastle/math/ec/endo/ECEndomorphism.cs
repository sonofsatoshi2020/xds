namespace NBitcoin.BouncyCastle.math.ec.endo
{
    interface ECEndomorphism
    {
        ECPointMap PointMap { get; }

        bool HasEfficientPointMap { get; }
    }
}