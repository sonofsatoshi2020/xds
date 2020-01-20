using System;
using NBitcoin.BouncyCastle.math;

namespace NBitcoin.BouncyCastle.asn1.x9
{
    /**
     * ASN.1 def for Elliptic-Curve Field ID structure. See
     * X9.62, for further details.
     */
    class X9FieldID
        : Asn1Encodable
    {
        /**
         * Constructor for elliptic curves over prime fields
         * <code>F<sub>2</sub></code>
         * .
         * @param primeP The prime
         * <code>p</code>
         * defining the prime field.
         */
        public X9FieldID(
            BigInteger primeP)
        {
            this.Identifier = X9ObjectIdentifiers.PrimeField;
            this.Parameters = new DerInteger(primeP);
        }

        /**
         * Constructor for elliptic curves over binary fields
         * <code>F<sub>2<sup>m</sup></sub></code>
         * .
         * @param m  The exponent
         * <code>m</code>
         * of
         * <code>F<sub>2<sup>m</sup></sub></code>
         * .
         * @param k1 The integer
         * <code>k1</code>
         * where
         * <code>x<sup>m</sup> +
         * x<sup>k1</sup> + 1</code>
         * represents the reduction polynomial
         * <code>f(z)</code>
         * .
         */
        public X9FieldID(int m, int k1)
            : this(m, k1, 0, 0)
        {
        }

        /**
         * Constructor for elliptic curves over binary fields
         * <code>F<sub>2<sup>m</sup></sub></code>
         * .
         * @param m  The exponent
         * <code>m</code>
         * of
         * <code>F<sub>2<sup>m</sup></sub></code>
         * .
         * @param k1 The integer
         * <code>k1</code>
         * where
         * <code>x<sup>m</sup> +
         * x<sup>k3</sup> + x<sup>k2</sup> + x<sup>k1</sup> + 1</code>
         * represents the reduction polynomial
         * <code>f(z)</code>
         * .
         * @param k2 The integer
         * <code>k2</code>
         * where
         * <code>x<sup>m</sup> +
         * x<sup>k3</sup> + x<sup>k2</sup> + x<sup>k1</sup> + 1</code>
         * represents the reduction polynomial
         * <code>f(z)</code>
         * .
         * @param k3 The integer
         * <code>k3</code>
         * where
         * <code>x<sup>m</sup> +
         * x<sup>k3</sup> + x<sup>k2</sup> + x<sup>k1</sup> + 1</code>
         * represents the reduction polynomial
         * <code>f(z)</code>
         * ..
         */
        public X9FieldID(
            int m,
            int k1,
            int k2,
            int k3)
        {
            this.Identifier = X9ObjectIdentifiers.CharacteristicTwoField;

            var fieldIdParams = new Asn1EncodableVector(new DerInteger(m));

            if (k2 == 0)
            {
                if (k3 != 0)
                    throw new ArgumentException("inconsistent k values");

                fieldIdParams.Add(
                    X9ObjectIdentifiers.TPBasis,
                    new DerInteger(k1));
            }
            else
            {
                if (k2 <= k1 || k3 <= k2)
                    throw new ArgumentException("inconsistent k values");

                fieldIdParams.Add(
                    X9ObjectIdentifiers.PPBasis,
                    new DerSequence(
                        new DerInteger(k1),
                        new DerInteger(k2),
                        new DerInteger(k3)));
            }

            this.Parameters = new DerSequence(fieldIdParams);
        }

        public DerObjectIdentifier Identifier { get; }

        public Asn1Object Parameters { get; }

        /**
         * Produce a Der encoding of the following structure.
         * <pre>
         *     FieldID ::= Sequence {
         *     fieldType       FIELD-ID.&amp;id({IOSet}),
         *     parameters      FIELD-ID.&amp;Type({IOSet}{&#64;fieldType})
         *     }
         * </pre>
         */
        public override Asn1Object ToAsn1Object()
        {
            return new DerSequence(this.Identifier, this.Parameters);
        }
    }
}