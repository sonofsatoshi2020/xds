using NBitcoin.BouncyCastle.math.ec;
using NBitcoin.BouncyCastle.util;

namespace NBitcoin.BouncyCastle.asn1.x9
{
    /**
     * class for describing an ECPoint as a Der object.
     */
    class X9ECPoint
        : Asn1Encodable
    {
        readonly Asn1OctetString encoding;

        readonly ECCurve c;
        ECPoint p;

        public X9ECPoint(ECPoint p)
            : this(p, false)
        {
        }

        public X9ECPoint(ECPoint p, bool compressed)
        {
            this.p = p.Normalize();
            this.encoding = new DerOctetString(p.GetEncoded(compressed));
        }

        public X9ECPoint(ECCurve c, byte[] encoding)
        {
            this.c = c;
            this.encoding = new DerOctetString(Arrays.Clone(encoding));
        }

        public X9ECPoint(ECCurve c, Asn1OctetString s)
            : this(c, s.GetOctets())
        {
        }

        public ECPoint Point
        {
            get
            {
                if (this.p == null) this.p = this.c.DecodePoint(this.encoding.GetOctets()).Normalize();

                return this.p;
            }
        }

        public bool IsPointCompressed
        {
            get
            {
                var octets = this.encoding.GetOctets();
                return octets != null && octets.Length > 0 && (octets[0] == 2 || octets[0] == 3);
            }
        }

        public byte[] GetPointEncoding()
        {
            return Arrays.Clone(this.encoding.GetOctets());
        }

        /**
         * Produce an object suitable for an Asn1OutputStream.
         * <pre>
         *     ECPoint ::= OCTET STRING
         * </pre>
         * <p>
         *     Octet string produced using ECPoint.GetEncoded().
         * </p>
         */
        public override Asn1Object ToAsn1Object()
        {
            return this.encoding;
        }
    }
}