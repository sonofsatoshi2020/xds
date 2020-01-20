using System;
using System.Text;

namespace NBitcoin.BouncyCastle.math.ec.abc
{
    /**
     * Class representing a simple version of a big decimal. A
     * <code>SimpleBigDecimal</code>
     * is basically a
     * {@link java.math.BigInteger BigInteger} with a few digits on the right of
     * the decimal point. The number of (binary) digits on the right of the decimal
     * point is called the
     * <code>scale</code>
     * of the
     * <code>SimpleBigDecimal</code>
     * .
     * Unlike in {@link java.math.BigDecimal BigDecimal}, the scale is not adjusted
     * automatically, but must be set manually. All
     * <code>SimpleBigDecimal</code>
     * s
     * taking part in the same arithmetic operation must have equal scale. The
     * result of a multiplication of two
     * <code>SimpleBigDecimal</code>
     * s returns a
     * <code>SimpleBigDecimal</code>
     * with double scale.
     */
    class SimpleBigDecimal
        //    : Number
    {
        //    private static final long serialVersionUID = 1L;

        readonly BigInteger bigInt;

        /**
         * Constructor for
         * <code>SimpleBigDecimal</code>
         * . The value of the
         * constructed
         * <code>SimpleBigDecimal</code>
         * Equals
         * <code>bigInt / 
         * 2<sup>scale</sup></code>
         * .
         * @param bigInt The
         * <code>bigInt</code>
         * value parameter.
         * @param scale The scale of the constructed
         * <code>SimpleBigDecimal</code>
         * .
         */
        public SimpleBigDecimal(BigInteger bigInt, int scale)
        {
            if (scale < 0)
                throw new ArgumentException("scale may not be negative");

            this.bigInt = bigInt;
            this.Scale = scale;
        }

        SimpleBigDecimal(SimpleBigDecimal limBigDec)
        {
            this.bigInt = limBigDec.bigInt;
            this.Scale = limBigDec.Scale;
        }

        public int IntValue => Floor().IntValue;

        public long LongValue => Floor().LongValue;

        //        public double doubleValue()
        //        {
        //            return new Double(ToString()).doubleValue();
        //        }
        //
        //        public float floatValue()
        //        {
        //            return new Float(ToString()).floatValue();
        //        }

        public int Scale { get; }

        /**
         * Returns a
         * <code>SimpleBigDecimal</code>
         * representing the same numerical
         * value as
         * <code>value</code>
         * .
         * @param value The value of the
         * <code>SimpleBigDecimal</code>
         * to be
         * created. 
         * @param scale The scale of the
         * <code>SimpleBigDecimal</code>
         * to be
         * created. 
         * @return The such created
         * <code>SimpleBigDecimal</code>
         * .
         */
        public static SimpleBigDecimal GetInstance(BigInteger val, int scale)
        {
            return new SimpleBigDecimal(val.ShiftLeft(scale), scale);
        }

        void CheckScale(SimpleBigDecimal b)
        {
            if (this.Scale != b.Scale)
                throw new ArgumentException("Only SimpleBigDecimal of same scale allowed in arithmetic operations");
        }

        public SimpleBigDecimal AdjustScale(int newScale)
        {
            if (newScale < 0)
                throw new ArgumentException("scale may not be negative");

            if (newScale == this.Scale)
                return this;

            return new SimpleBigDecimal(this.bigInt.ShiftLeft(newScale - this.Scale), newScale);
        }

        public SimpleBigDecimal Add(SimpleBigDecimal b)
        {
            CheckScale(b);
            return new SimpleBigDecimal(this.bigInt.Add(b.bigInt), this.Scale);
        }

        public SimpleBigDecimal Add(BigInteger b)
        {
            return new SimpleBigDecimal(this.bigInt.Add(b.ShiftLeft(this.Scale)), this.Scale);
        }

        public SimpleBigDecimal Negate()
        {
            return new SimpleBigDecimal(this.bigInt.Negate(), this.Scale);
        }

        public SimpleBigDecimal Subtract(SimpleBigDecimal b)
        {
            return Add(b.Negate());
        }

        public SimpleBigDecimal Subtract(BigInteger b)
        {
            return new SimpleBigDecimal(this.bigInt.Subtract(b.ShiftLeft(this.Scale)), this.Scale);
        }

        public SimpleBigDecimal Multiply(SimpleBigDecimal b)
        {
            CheckScale(b);
            return new SimpleBigDecimal(this.bigInt.Multiply(b.bigInt), this.Scale + this.Scale);
        }

        public SimpleBigDecimal Multiply(BigInteger b)
        {
            return new SimpleBigDecimal(this.bigInt.Multiply(b), this.Scale);
        }

        public SimpleBigDecimal Divide(SimpleBigDecimal b)
        {
            CheckScale(b);
            var dividend = this.bigInt.ShiftLeft(this.Scale);
            return new SimpleBigDecimal(dividend.Divide(b.bigInt), this.Scale);
        }

        public SimpleBigDecimal Divide(BigInteger b)
        {
            return new SimpleBigDecimal(this.bigInt.Divide(b), this.Scale);
        }

        public SimpleBigDecimal ShiftLeft(int n)
        {
            return new SimpleBigDecimal(this.bigInt.ShiftLeft(n), this.Scale);
        }

        public int CompareTo(SimpleBigDecimal val)
        {
            CheckScale(val);
            return this.bigInt.CompareTo(val.bigInt);
        }

        public int CompareTo(BigInteger val)
        {
            return this.bigInt.CompareTo(val.ShiftLeft(this.Scale));
        }

        public BigInteger Floor()
        {
            return this.bigInt.ShiftRight(this.Scale);
        }

        public BigInteger Round()
        {
            var oneHalf = new SimpleBigDecimal(BigInteger.One, 1);
            return Add(oneHalf.AdjustScale(this.Scale)).Floor();
        }

        public override string ToString()
        {
            if (this.Scale == 0)
                return this.bigInt.ToString();

            var floorBigInt = Floor();

            var fract = this.bigInt.Subtract(floorBigInt.ShiftLeft(this.Scale));
            if (this.bigInt.SignValue < 0) fract = BigInteger.One.ShiftLeft(this.Scale).Subtract(fract);

            if (floorBigInt.SignValue == -1 && !fract.Equals(BigInteger.Zero))
                floorBigInt = floorBigInt.Add(BigInteger.One);
            var leftOfPoint = floorBigInt.ToString();

            var fractCharArr = new char[this.Scale];
            var fractStr = fract.ToString(2);
            var fractLen = fractStr.Length;
            var zeroes = this.Scale - fractLen;
            for (var i = 0; i < zeroes; i++) fractCharArr[i] = '0';
            for (var j = 0; j < fractLen; j++) fractCharArr[zeroes + j] = fractStr[j];
            var rightOfPoint = new string(fractCharArr);

            var sb = new StringBuilder(leftOfPoint);
            sb.Append(".");
            sb.Append(rightOfPoint);

            return sb.ToString();
        }

        public override bool Equals(
            object obj)
        {
            if (this == obj)
                return true;

            var other = obj as SimpleBigDecimal;

            if (other == null)
                return false;

            return this.bigInt.Equals(other.bigInt)
                   && this.Scale == other.Scale;
        }

        public override int GetHashCode()
        {
            return this.bigInt.GetHashCode() ^ this.Scale;
        }
    }
}