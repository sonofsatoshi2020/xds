namespace NBitcoin.BouncyCastle.math.field
{
    interface IFiniteField
    {
        BigInteger Characteristic { get; }

        int Dimension { get; }
    }
}