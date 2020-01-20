using System;
using NBitcoin.BouncyCastle.math;
using NBitcoin.BouncyCastle.math.ec;
using NBitcoin.BouncyCastle.math.field;

namespace NBitcoin.BouncyCastle.asn1.x9
{
    /**
     * ASN.1 def for Elliptic-Curve ECParameters structure. See
     * X9.62, for further details.
     */
    class X9ECParameters
        : Asn1Encodable
    {
        readonly byte[] seed;

        public X9ECParameters(
            ECCurve curve,
            ECPoint g,
            BigInteger n)
            : this(curve, g, n, null, null)
        {
        }

        public X9ECParameters(
            ECCurve curve,
            X9ECPoint g,
            BigInteger n,
            BigInteger h)
            : this(curve, g, n, h, null)
        {
        }

        public X9ECParameters(
            ECCurve curve,
            ECPoint g,
            BigInteger n,
            BigInteger h)
            : this(curve, g, n, h, null)
        {
        }

        public X9ECParameters(
            ECCurve curve,
            ECPoint g,
            BigInteger n,
            BigInteger h,
            byte[] seed)
            : this(curve, new X9ECPoint(g), n, h, seed)
        {
        }

        public X9ECParameters(
            ECCurve curve,
            X9ECPoint g,
            BigInteger n,
            BigInteger h,
            byte[] seed)
        {
            this.Curve = curve;
            this.BaseEntry = g;
            this.N = n;
            this.H = h;
            this.seed = seed;

            if (ECAlgorithms.IsFpCurve(curve))
            {
                this.FieldIDEntry = new X9FieldID(curve.Field.Characteristic);
            }
            else if (ECAlgorithms.IsF2mCurve(curve))
            {
                var field = (IPolynomialExtensionField) curve.Field;
                var exponents = field.MinimalPolynomial.GetExponentsPresent();
                if (exponents.Length == 3)
                    this.FieldIDEntry = new X9FieldID(exponents[2], exponents[1]);
                else if (exponents.Length == 5)
                    this.FieldIDEntry = new X9FieldID(exponents[4], exponents[1], exponents[2], exponents[3]);
                else
                    throw new ArgumentException("Only trinomial and pentomial curves are supported");
            }
            else
            {
                throw new ArgumentException("'curve' is of an unsupported type");
            }
        }

        public ECCurve Curve { get; }

        public ECPoint G => this.BaseEntry.Point;

        public BigInteger N { get; }

        public BigInteger H { get; }

        /**
         * Return the ASN.1 entry representing the Curve.
         *
         * @return the X9Curve for the curve in these parameters.
         */
        public X9Curve CurveEntry => new X9Curve(this.Curve, this.seed);

        /**
         * Return the ASN.1 entry representing the FieldID.
         *
         * @return the X9FieldID for the FieldID in these parameters.
         */
        public X9FieldID FieldIDEntry { get; }

        /**
         * Return the ASN.1 entry representing the base point G.
         *
         * @return the X9ECPoint for the base point in these parameters.
         */
        public X9ECPoint BaseEntry { get; }

        public byte[] GetSeed()
        {
            return this.seed;
        }

        /**
         * Produce an object suitable for an Asn1OutputStream.
         * <pre>
         *     ECParameters ::= Sequence {
         *     version         Integer { ecpVer1(1) } (ecpVer1),
         *     fieldID         FieldID {{FieldTypes}},
         *     curve           X9Curve,
         *     base            X9ECPoint,
         *     order           Integer,
         *     cofactor        Integer OPTIONAL
         *     }
         * </pre>
         */
        public override Asn1Object ToAsn1Object()
        {
            var v = new Asn1EncodableVector(
                new DerInteger(BigInteger.One), this.FieldIDEntry,
                new X9Curve(this.Curve, this.seed), this.BaseEntry,
                new DerInteger(this.N));

            if (this.H != null) v.Add(new DerInteger(this.H));

            return new DerSequence(v);
        }
    }
}