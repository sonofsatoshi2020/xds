using NBitcoin.BouncyCastle.math;
using NBitcoin.BouncyCastle.math.ec;

namespace NBitcoin.BouncyCastle.asn1.x9
{
    /**
     * Class for processing an ECFieldElement as a DER object.
     */
    class X9FieldElement
        : Asn1Encodable
    {
        public X9FieldElement(
            ECFieldElement f)
        {
            this.Value = f;
        }

        public X9FieldElement(
            BigInteger p,
            Asn1OctetString s)
#pragma warning disable
            : this(new FpFieldElement(p, new BigInteger(1, s.GetOctets())))
#pragma warning restore
        {
        }

        public X9FieldElement(
            int m,
            int k1,
            int k2,
            int k3,
            Asn1OctetString s)
            : this(new F2mFieldElement(m, k1, k2, k3, new BigInteger(1, s.GetOctets())))
        {
        }

        public ECFieldElement Value { get; }

        /**
         * Produce an object suitable for an Asn1OutputStream.
         * <pre>
         *     FieldElement ::= OCTET STRING
         * </pre>
         * <p>
         *     <ol>
         *         <li>
         *             if <i>q</i> is an odd prime then the field element is
         *             processed as an Integer and converted to an octet string
         *             according to x 9.62 4.3.1.
         *         </li>
         *         <li>
         *             if <i>q</i> is 2<sup>m</sup> then the bit string
         *             contained in the field element is converted into an octet
         *             string with the same ordering padded at the front if necessary.
         *         </li>
         *     </ol>
         * </p>
         */
        public override Asn1Object ToAsn1Object()
        {
            var byteCount = X9IntegerConverter.GetByteLength(this.Value);
            var paddedBigInteger = X9IntegerConverter.IntegerToBytes(this.Value.ToBigInteger(), byteCount);

            return new DerOctetString(paddedBigInteger);
        }
    }
}