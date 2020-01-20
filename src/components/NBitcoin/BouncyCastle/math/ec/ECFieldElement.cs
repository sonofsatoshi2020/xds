using System;
using System.Diagnostics;
using NBitcoin.BouncyCastle.math.raw;
using NBitcoin.BouncyCastle.util;

namespace NBitcoin.BouncyCastle.math.ec
{
    abstract class ECFieldElement
    {
        public abstract string FieldName { get; }

        public abstract int FieldSize { get; }

        public virtual int BitLength => ToBigInteger().BitLength;

        public virtual bool IsOne => this.BitLength == 1;

        public virtual bool IsZero => 0 == ToBigInteger().SignValue;

        public abstract BigInteger ToBigInteger();
        public abstract ECFieldElement Add(ECFieldElement b);
        public abstract ECFieldElement AddOne();
        public abstract ECFieldElement Subtract(ECFieldElement b);
        public abstract ECFieldElement Multiply(ECFieldElement b);
        public abstract ECFieldElement Divide(ECFieldElement b);
        public abstract ECFieldElement Negate();
        public abstract ECFieldElement Square();
        public abstract ECFieldElement Invert();
        public abstract ECFieldElement Sqrt();

        public virtual ECFieldElement MultiplyMinusProduct(ECFieldElement b, ECFieldElement x, ECFieldElement y)
        {
            return Multiply(b).Subtract(x.Multiply(y));
        }

        public virtual ECFieldElement MultiplyPlusProduct(ECFieldElement b, ECFieldElement x, ECFieldElement y)
        {
            return Multiply(b).Add(x.Multiply(y));
        }

        public virtual ECFieldElement SquareMinusProduct(ECFieldElement x, ECFieldElement y)
        {
            return Square().Subtract(x.Multiply(y));
        }

        public virtual ECFieldElement SquarePlusProduct(ECFieldElement x, ECFieldElement y)
        {
            return Square().Add(x.Multiply(y));
        }

        public virtual ECFieldElement SquarePow(int pow)
        {
            var r = this;
            for (var i = 0; i < pow; ++i) r = r.Square();
            return r;
        }

        public virtual bool TestBitZero()
        {
            return ToBigInteger().TestBit(0);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ECFieldElement);
        }

        public virtual bool Equals(ECFieldElement other)
        {
            if (this == other)
                return true;
            if (null == other)
                return false;
            return ToBigInteger().Equals(other.ToBigInteger());
        }

        public override int GetHashCode()
        {
            return ToBigInteger().GetHashCode();
        }

        public override string ToString()
        {
            return ToBigInteger().ToString(16);
        }

        public virtual byte[] GetEncoded()
        {
            return BigIntegers.AsUnsignedByteArray((this.FieldSize + 7) / 8, ToBigInteger());
        }
    }

    class FpFieldElement
        : ECFieldElement
    {
        readonly BigInteger r, x;

        [Obsolete("Use ECCurve.FromBigInteger to construct field elements")]
        public FpFieldElement(BigInteger q, BigInteger x)
            : this(q, CalculateResidue(q), x)
        {
        }

        internal FpFieldElement(BigInteger q, BigInteger r, BigInteger x)
        {
            if (x == null || x.SignValue < 0 || x.CompareTo(q) >= 0)
                throw new ArgumentException("value invalid in Fp field element", "x");

            this.Q = q;
            this.r = r;
            this.x = x;
        }

        /**
         * return the field name for this field.
         *
         * @return the string "Fp".
         */
        public override string FieldName => "Fp";

        public override int FieldSize => this.Q.BitLength;

        public BigInteger Q { get; }

        internal static BigInteger CalculateResidue(BigInteger p)
        {
            var bitLength = p.BitLength;
            if (bitLength >= 96)
            {
                var firstWord = p.ShiftRight(bitLength - 64);
                if (firstWord.LongValue == -1L) return BigInteger.One.ShiftLeft(bitLength).Subtract(p);
                if ((bitLength & 7) == 0) return BigInteger.One.ShiftLeft(bitLength << 1).Divide(p).Negate();
            }

            return null;
        }

        public override BigInteger ToBigInteger()
        {
            return this.x;
        }

        public override ECFieldElement Add(
            ECFieldElement b)
        {
            return new FpFieldElement(this.Q, this.r, ModAdd(this.x, b.ToBigInteger()));
        }

        public override ECFieldElement AddOne()
        {
            var x2 = this.x.Add(BigInteger.One);
            if (x2.CompareTo(this.Q) == 0) x2 = BigInteger.Zero;
            return new FpFieldElement(this.Q, this.r, x2);
        }

        public override ECFieldElement Subtract(
            ECFieldElement b)
        {
            return new FpFieldElement(this.Q, this.r, ModSubtract(this.x, b.ToBigInteger()));
        }

        public override ECFieldElement Multiply(
            ECFieldElement b)
        {
            return new FpFieldElement(this.Q, this.r, ModMult(this.x, b.ToBigInteger()));
        }

        public override ECFieldElement MultiplyMinusProduct(ECFieldElement b, ECFieldElement x, ECFieldElement y)
        {
            BigInteger ax = this.x, bx = b.ToBigInteger(), xx = x.ToBigInteger(), yx = y.ToBigInteger();
            var ab = ax.Multiply(bx);
            var xy = xx.Multiply(yx);
            return new FpFieldElement(this.Q, this.r, ModReduce(ab.Subtract(xy)));
        }

        public override ECFieldElement MultiplyPlusProduct(ECFieldElement b, ECFieldElement x, ECFieldElement y)
        {
            BigInteger ax = this.x, bx = b.ToBigInteger(), xx = x.ToBigInteger(), yx = y.ToBigInteger();
            var ab = ax.Multiply(bx);
            var xy = xx.Multiply(yx);
            var sum = ab.Add(xy);
            if (this.r != null && this.r.SignValue < 0 && sum.BitLength > this.Q.BitLength << 1)
                sum = sum.Subtract(this.Q.ShiftLeft(this.Q.BitLength));
            return new FpFieldElement(this.Q, this.r, ModReduce(sum));
        }

        public override ECFieldElement Divide(
            ECFieldElement b)
        {
            return new FpFieldElement(this.Q, this.r, ModMult(this.x, ModInverse(b.ToBigInteger())));
        }

        public override ECFieldElement Negate()
        {
            return this.x.SignValue == 0 ? this : new FpFieldElement(this.Q, this.r, this.Q.Subtract(this.x));
        }

        public override ECFieldElement Square()
        {
            return new FpFieldElement(this.Q, this.r, ModMult(this.x, this.x));
        }

        public override ECFieldElement SquareMinusProduct(ECFieldElement x, ECFieldElement y)
        {
            BigInteger ax = this.x, xx = x.ToBigInteger(), yx = y.ToBigInteger();
            var aa = ax.Multiply(ax);
            var xy = xx.Multiply(yx);
            return new FpFieldElement(this.Q, this.r, ModReduce(aa.Subtract(xy)));
        }

        public override ECFieldElement SquarePlusProduct(ECFieldElement x, ECFieldElement y)
        {
            BigInteger ax = this.x, xx = x.ToBigInteger(), yx = y.ToBigInteger();
            var aa = ax.Multiply(ax);
            var xy = xx.Multiply(yx);
            var sum = aa.Add(xy);
            if (this.r != null && this.r.SignValue < 0 && sum.BitLength > this.Q.BitLength << 1)
                sum = sum.Subtract(this.Q.ShiftLeft(this.Q.BitLength));
            return new FpFieldElement(this.Q, this.r, ModReduce(sum));
        }

        public override ECFieldElement Invert()
        {
            // TODO Modular inversion can be faster for a (Generalized) Mersenne Prime.
            return new FpFieldElement(this.Q, this.r, ModInverse(this.x));
        }

        /**
         * return a sqrt root - the routine verifies that the calculation
         * returns the right value - if none exists it returns null.
         */
        public override ECFieldElement Sqrt()
        {
            if (this.IsZero || this.IsOne)
                return this;

            if (!this.Q.TestBit(0))
                throw Platform.CreateNotImplementedException("even value of q");

            if (this.Q.TestBit(1)) // q == 4m + 3
            {
                var e = this.Q.ShiftRight(2).Add(BigInteger.One);
                return CheckSqrt(new FpFieldElement(this.Q, this.r, this.x.ModPow(e, this.Q)));
            }

            if (this.Q.TestBit(2)) // q == 8m + 5
            {
                var t1 = this.x.ModPow(this.Q.ShiftRight(3), this.Q);
                var t2 = ModMult(t1, this.x);
                var t3 = ModMult(t2, t1);

                if (t3.Equals(BigInteger.One)) return CheckSqrt(new FpFieldElement(this.Q, this.r, t2));

                // TODO This is constant and could be precomputed
                var t4 = BigInteger.Two.ModPow(this.Q.ShiftRight(2), this.Q);

                var y = ModMult(t2, t4);

                return CheckSqrt(new FpFieldElement(this.Q, this.r, y));
            }

            // q == 8m + 1

            var legendreExponent = this.Q.ShiftRight(1);
            if (!this.x.ModPow(legendreExponent, this.Q).Equals(BigInteger.One))
                return null;

            var X = this.x;
            var fourX = ModDouble(ModDouble(X));
            ;

            BigInteger k = legendreExponent.Add(BigInteger.One), qMinusOne = this.Q.Subtract(BigInteger.One);

            BigInteger U, V;
            do
            {
                BigInteger P;
                do
                {
                    P = BigInteger.Arbitrary(this.Q.BitLength);
                } while (P.CompareTo(this.Q) >= 0
                         || !ModReduce(P.Multiply(P).Subtract(fourX)).ModPow(legendreExponent, this.Q)
                             .Equals(qMinusOne));

                var result = LucasSequence(P, X, k);
                U = result[0];
                V = result[1];

                if (ModMult(V, V).Equals(fourX)) return new FpFieldElement(this.Q, this.r, ModHalfAbs(V));
            } while (U.Equals(BigInteger.One) || U.Equals(qMinusOne));

            return null;
        }

        ECFieldElement CheckSqrt(ECFieldElement z)
        {
            return z.Square().Equals(this) ? z : null;
        }

        BigInteger[] LucasSequence(
            BigInteger P,
            BigInteger Q,
            BigInteger k)
        {
            // TODO Research and apply "common-multiplicand multiplication here"

            var n = k.BitLength;
            var s = k.GetLowestSetBit();

            Debug.Assert(k.TestBit(s));

            var Uh = BigInteger.One;
            var Vl = BigInteger.Two;
            var Vh = P;
            var Ql = BigInteger.One;
            var Qh = BigInteger.One;

            for (var j = n - 1; j >= s + 1; --j)
            {
                Ql = ModMult(Ql, Qh);

                if (k.TestBit(j))
                {
                    Qh = ModMult(Ql, Q);
                    Uh = ModMult(Uh, Vh);
                    Vl = ModReduce(Vh.Multiply(Vl).Subtract(P.Multiply(Ql)));
                    Vh = ModReduce(Vh.Multiply(Vh).Subtract(Qh.ShiftLeft(1)));
                }
                else
                {
                    Qh = Ql;
                    Uh = ModReduce(Uh.Multiply(Vl).Subtract(Ql));
                    Vh = ModReduce(Vh.Multiply(Vl).Subtract(P.Multiply(Ql)));
                    Vl = ModReduce(Vl.Multiply(Vl).Subtract(Ql.ShiftLeft(1)));
                }
            }

            Ql = ModMult(Ql, Qh);
            Qh = ModMult(Ql, Q);
            Uh = ModReduce(Uh.Multiply(Vl).Subtract(Ql));
            Vl = ModReduce(Vh.Multiply(Vl).Subtract(P.Multiply(Ql)));
            Ql = ModMult(Ql, Qh);

            for (var j = 1; j <= s; ++j)
            {
                Uh = ModMult(Uh, Vl);
                Vl = ModReduce(Vl.Multiply(Vl).Subtract(Ql.ShiftLeft(1)));
                Ql = ModMult(Ql, Ql);
            }

            return new[] {Uh, Vl};
        }

        protected virtual BigInteger ModAdd(BigInteger x1, BigInteger x2)
        {
            var x3 = x1.Add(x2);
            if (x3.CompareTo(this.Q) >= 0) x3 = x3.Subtract(this.Q);
            return x3;
        }

        protected virtual BigInteger ModDouble(BigInteger x)
        {
            var _2x = x.ShiftLeft(1);
            if (_2x.CompareTo(this.Q) >= 0) _2x = _2x.Subtract(this.Q);
            return _2x;
        }

        protected virtual BigInteger ModHalf(BigInteger x)
        {
            if (x.TestBit(0)) x = this.Q.Add(x);
            return x.ShiftRight(1);
        }

        protected virtual BigInteger ModHalfAbs(BigInteger x)
        {
            if (x.TestBit(0)) x = this.Q.Subtract(x);
            return x.ShiftRight(1);
        }

        protected virtual BigInteger ModInverse(BigInteger x)
        {
            var bits = this.FieldSize;
            var len = (bits + 31) >> 5;
            var p = Nat.FromBigInteger(bits, this.Q);
            var n = Nat.FromBigInteger(bits, x);
            var z = Nat.Create(len);
            Mod.Invert(p, n, z);
            return Nat.ToBigInteger(len, z);
        }

        protected virtual BigInteger ModMult(BigInteger x1, BigInteger x2)
        {
            return ModReduce(x1.Multiply(x2));
        }

        protected virtual BigInteger ModReduce(BigInteger x)
        {
            if (this.r == null)
            {
                x = x.Mod(this.Q);
            }
            else
            {
                var negative = x.SignValue < 0;
                if (negative) x = x.Abs();
                var qLen = this.Q.BitLength;
                if (this.r.SignValue > 0)
                {
                    var qMod = BigInteger.One.ShiftLeft(qLen);
                    var rIsOne = this.r.Equals(BigInteger.One);
                    while (x.BitLength > qLen + 1)
                    {
                        var u = x.ShiftRight(qLen);
                        var v = x.Remainder(qMod);
                        if (!rIsOne) u = u.Multiply(this.r);
                        x = u.Add(v);
                    }
                }
                else
                {
                    var d = ((qLen - 1) & 31) + 1;
                    var mu = this.r.Negate();
                    var u = mu.Multiply(x.ShiftRight(qLen - d));
                    var quot = u.ShiftRight(qLen + d);
                    var v = quot.Multiply(this.Q);
                    var bk1 = BigInteger.One.ShiftLeft(qLen + d);
                    v = v.Remainder(bk1);
                    x = x.Remainder(bk1);
                    x = x.Subtract(v);
                    if (x.SignValue < 0) x = x.Add(bk1);
                }

                while (x.CompareTo(this.Q) >= 0) x = x.Subtract(this.Q);
                if (negative && x.SignValue != 0) x = this.Q.Subtract(x);
            }

            return x;
        }

        protected virtual BigInteger ModSubtract(BigInteger x1, BigInteger x2)
        {
            var x3 = x1.Subtract(x2);
            if (x3.SignValue < 0) x3 = x3.Add(this.Q);
            return x3;
        }

        public override bool Equals(
            object obj)
        {
            if (obj == this)
                return true;

            var other = obj as FpFieldElement;

            if (other == null)
                return false;

            return Equals(other);
        }

        public virtual bool Equals(
            FpFieldElement other)
        {
            return this.Q.Equals(other.Q) && base.Equals(other);
        }

        public override int GetHashCode()
        {
            return this.Q.GetHashCode() ^ base.GetHashCode();
        }
    }

    /**
     * Class representing the Elements of the finite field
     * <code>F<sub>2<sup>m</sup></sub></code>
     * in polynomial basis (PB)
     * representation. Both trinomial (Tpb) and pentanomial (Ppb) polynomial
     * basis representations are supported. Gaussian normal basis (GNB)
     * representation is not supported.
     */
    class F2mFieldElement
        : ECFieldElement
    {
        /**
         * Indicates gaussian normal basis representation (GNB). Number chosen
         * according to X9.62. GNB is not implemented at present.
         */
        public const int Gnb = 1;

        /**
         * Indicates trinomial basis representation (Tpb). Number chosen
         * according to X9.62.
         */
        public const int Tpb = 2;

        /**
         * Indicates pentanomial basis representation (Ppb). Number chosen
         * according to X9.62.
         */
        public const int Ppb = 3;

        readonly int[] ks;

        /**
         * The exponent
         * <code>m</code>
         * of
         * <code>F<sub>2<sup>m</sup></sub></code>
         * .
         */
        readonly int m;

        /**
         * Tpb or Ppb.
         */
        readonly int representation;

        /**
         * The
         * <code>LongArray</code>
         * holding the bits.
         */
        readonly LongArray x;

        /**
         * Constructor for Ppb.
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
         * .
         * @param x The BigInteger representing the value of the field element.
         */
        public F2mFieldElement(
            int m,
            int k1,
            int k2,
            int k3,
            BigInteger x)
        {
            if (x == null || x.SignValue < 0 || x.BitLength > m)
                throw new ArgumentException("value invalid in F2m field element", "x");

            if (k2 == 0 && k3 == 0)
            {
                this.representation = Tpb;
                this.ks = new[] {k1};
            }
            else
            {
                if (k2 >= k3)
                    throw new ArgumentException("k2 must be smaller than k3");
                if (k2 <= 0)
                    throw new ArgumentException("k2 must be larger than 0");

                this.representation = Ppb;
                this.ks = new[] {k1, k2, k3};
            }

            this.m = m;
            this.x = new LongArray(x);
        }

        /**
         * Constructor for Tpb.
         * @param m  The exponent
         * <code>m</code>
         * of
         * <code>F<sub>2<sup>m</sup></sub></code>
         * .
         * @param k The integer
         * <code>k</code>
         * where
         * <code>x<sup>m</sup> +
         * x<sup>k</sup> + 1</code>
         * represents the reduction
         * polynomial
         * <code>f(z)</code>
         * .
         * @param x The BigInteger representing the value of the field element.
         */
        public F2mFieldElement(
            int m,
            int k,
            BigInteger x)
            : this(m, k, 0, 0, x)
        {
            // Set k1 to k, and set k2 and k3 to 0
        }

        F2mFieldElement(int m, int[] ks, LongArray x)
        {
            this.m = m;
            this.representation = ks.Length == 1 ? Tpb : Ppb;
            this.ks = ks;
            this.x = x;
        }

        public override int BitLength => this.x.Degree();

        public override bool IsOne => this.x.IsOne();

        public override bool IsZero => this.x.IsZero();

        public override string FieldName => "F2m";

        public override int FieldSize => this.m;

        /**
         * @return the representation of the field
         * <code>F<sub>2<sup>m</sup></sub></code>
         * , either of
         * {@link F2mFieldElement.Tpb} (trinomial
         * basis representation) or
         * {@link F2mFieldElement.Ppb} (pentanomial
         * basis representation).
         */
        public int Representation => this.representation;

        /**
         * @return the degree
         * <code>m</code>
         * of the reduction polynomial
         * <code>f(z)</code>
         * .
         */
        public int M => this.m;

        /**
         * @return Tpb: The integer
         * <code>k</code>
         * where
         * <code>x<sup>m</sup> +
         * x<sup>k</sup> + 1</code>
         * represents the reduction polynomial
         * <code>f(z)</code>
         * .
         * <br />
         * Ppb: The integer
         * <code>k1</code>
         * where
         * <code>x<sup>m</sup> +
         * x<sup>k3</sup> + x<sup>k2</sup> + x<sup>k1</sup> + 1</code>
         * represents the reduction polynomial
         * <code>f(z)</code>
         * .
         * <br />
         */
        public int K1 => this.ks[0];

        /**
         * @return Tpb: Always returns
         * <code>0</code>
         * <br />
         * Ppb: The integer
         * <code>k2</code>
         * where
         * <code>x<sup>m</sup> +
         * x<sup>k3</sup> + x<sup>k2</sup> + x<sup>k1</sup> + 1</code>
         * represents the reduction polynomial
         * <code>f(z)</code>
         * .
         * <br />
         */
        public int K2 => this.ks.Length >= 2 ? this.ks[1] : 0;

        /**
         * @return Tpb: Always set to
         * <code>0</code>
         * <br />
         * Ppb: The integer
         * <code>k3</code>
         * where
         * <code>x<sup>m</sup> +
         * x<sup>k3</sup> + x<sup>k2</sup> + x<sup>k1</sup> + 1</code>
         * represents the reduction polynomial
         * <code>f(z)</code>
         * .
         * <br />
         */
        public int K3 => this.ks.Length >= 3 ? this.ks[2] : 0;

        public override bool TestBitZero()
        {
            return this.x.TestBitZero();
        }

        public override BigInteger ToBigInteger()
        {
            return this.x.ToBigInteger();
        }

        /**
         * Checks, if the ECFieldElements
         * <code>a</code>
         * and
         * <code>b</code>
         * are elements of the same field
         * <code>F<sub>2<sup>m</sup></sub></code>
         * (having the same representation).
         * @param a field element.
         * @param b field element to be compared.
         * @throws ArgumentException if
         * <code>a</code>
         * and
         * <code>b</code>
         * are not elements of the same field
         * <code>F<sub>2<sup>m</sup></sub></code>
         * (having the same
         * representation).
         */
        public static void CheckFieldElements(
            ECFieldElement a,
            ECFieldElement b)
        {
            if (!(a is F2mFieldElement) || !(b is F2mFieldElement))
                throw new ArgumentException("Field elements are not "
                                            + "both instances of F2mFieldElement");

            var aF2m = (F2mFieldElement) a;
            var bF2m = (F2mFieldElement) b;

            if (aF2m.representation != bF2m.representation)
                // Should never occur
                throw new ArgumentException("One of the F2m field elements has incorrect representation");

            if (aF2m.m != bF2m.m || !Arrays.AreEqual(aF2m.ks, bF2m.ks))
                throw new ArgumentException("Field elements are not elements of the same field F2m");
        }

        public override ECFieldElement Add(
            ECFieldElement b)
        {
            // No check performed here for performance reasons. Instead the
            // elements involved are checked in ECPoint.F2m
            // checkFieldElements(this, b);
            var iarrClone = this.x.Copy();
            var bF2m = (F2mFieldElement) b;
            iarrClone.AddShiftedByWords(bF2m.x, 0);
            return new F2mFieldElement(this.m, this.ks, iarrClone);
        }

        public override ECFieldElement AddOne()
        {
            return new F2mFieldElement(this.m, this.ks, this.x.AddOne());
        }

        public override ECFieldElement Subtract(
            ECFieldElement b)
        {
            // Addition and subtraction are the same in F2m
            return Add(b);
        }

        public override ECFieldElement Multiply(
            ECFieldElement b)
        {
            // Right-to-left comb multiplication in the LongArray
            // Input: Binary polynomials a(z) and b(z) of degree at most m-1
            // Output: c(z) = a(z) * b(z) mod f(z)

            // No check performed here for performance reasons. Instead the
            // elements involved are checked in ECPoint.F2m
            // checkFieldElements(this, b);
            return new F2mFieldElement(this.m, this.ks, this.x.ModMultiply(((F2mFieldElement) b).x, this.m, this.ks));
        }

        public override ECFieldElement MultiplyMinusProduct(ECFieldElement b, ECFieldElement x, ECFieldElement y)
        {
            return MultiplyPlusProduct(b, x, y);
        }

        public override ECFieldElement MultiplyPlusProduct(ECFieldElement b, ECFieldElement x, ECFieldElement y)
        {
            LongArray ax = this.x,
                bx = ((F2mFieldElement) b).x,
                xx = ((F2mFieldElement) x).x,
                yx = ((F2mFieldElement) y).x;

            var ab = ax.Multiply(bx, this.m, this.ks);
            var xy = xx.Multiply(yx, this.m, this.ks);

            if (ab == ax || ab == bx) ab = ab.Copy();

            ab.AddShiftedByWords(xy, 0);
            ab.Reduce(this.m, this.ks);

            return new F2mFieldElement(this.m, this.ks, ab);
        }

        public override ECFieldElement Divide(
            ECFieldElement b)
        {
            // There may be more efficient implementations
            var bInv = b.Invert();
            return Multiply(bInv);
        }

        public override ECFieldElement Negate()
        {
            // -x == x holds for all x in F2m
            return this;
        }

        public override ECFieldElement Square()
        {
            return new F2mFieldElement(this.m, this.ks, this.x.ModSquare(this.m, this.ks));
        }

        public override ECFieldElement SquareMinusProduct(ECFieldElement x, ECFieldElement y)
        {
            return SquarePlusProduct(x, y);
        }

        public override ECFieldElement SquarePlusProduct(ECFieldElement x, ECFieldElement y)
        {
            LongArray ax = this.x, xx = ((F2mFieldElement) x).x, yx = ((F2mFieldElement) y).x;

            var aa = ax.Square(this.m, this.ks);
            var xy = xx.Multiply(yx, this.m, this.ks);

            if (aa == ax) aa = aa.Copy();

            aa.AddShiftedByWords(xy, 0);
            aa.Reduce(this.m, this.ks);

            return new F2mFieldElement(this.m, this.ks, aa);
        }

        public override ECFieldElement SquarePow(int pow)
        {
            return pow < 1 ? this : new F2mFieldElement(this.m, this.ks, this.x.ModSquareN(pow, this.m, this.ks));
        }

        public override ECFieldElement Invert()
        {
            return new F2mFieldElement(this.m, this.ks, this.x.ModInverse(this.m, this.ks));
        }

        public override ECFieldElement Sqrt()
        {
            return this.x.IsZero() || this.x.IsOne() ? this : SquarePow(this.m - 1);
        }

        public override bool Equals(
            object obj)
        {
            if (obj == this)
                return true;

            var other = obj as F2mFieldElement;

            if (other == null)
                return false;

            return Equals(other);
        }

        public virtual bool Equals(
            F2mFieldElement other)
        {
            return this.m == other.m
                   && this.representation == other.representation
                   && Arrays.AreEqual(this.ks, other.ks)
                   && this.x.Equals(other.x);
        }

        public override int GetHashCode()
        {
            return this.x.GetHashCode() ^ this.m ^ Arrays.GetHashCode(this.ks);
        }
    }
}