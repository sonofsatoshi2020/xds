namespace NBitcoin.BouncyCastle.math.field
{
    interface IPolynomialExtensionField
        : IExtensionField
    {
        IPolynomial MinimalPolynomial { get; }
    }
}