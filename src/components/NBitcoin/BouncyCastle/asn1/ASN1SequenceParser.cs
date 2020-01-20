namespace NBitcoin.BouncyCastle.asn1
{
    interface Asn1SequenceParser
        : IAsn1Convertible
    {
        IAsn1Convertible ReadObject();
    }
}