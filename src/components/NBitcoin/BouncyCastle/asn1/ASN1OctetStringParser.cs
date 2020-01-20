using System.IO;

namespace NBitcoin.BouncyCastle.asn1
{
    interface Asn1OctetStringParser
        : IAsn1Convertible
    {
        Stream GetOctetStream();
    }
}