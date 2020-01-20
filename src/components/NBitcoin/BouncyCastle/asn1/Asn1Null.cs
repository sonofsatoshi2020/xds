namespace NBitcoin.BouncyCastle.asn1
{
    /**
     * A Null object.
     */
    abstract class Asn1Null
        : Asn1Object
    {
        public override string ToString()
        {
            return "NULL";
        }
    }
}