namespace NBitcoin.BouncyCastle.math.field
{
    interface IExtensionField
        : IFiniteField
    {
        IFiniteField Subfield { get; }

        int Degree { get; }
    }
}