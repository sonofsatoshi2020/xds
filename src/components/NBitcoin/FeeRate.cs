using System;

namespace NBitcoin
{
    public class FeeRate : IEquatable<FeeRate>, IComparable<FeeRate>
    {
        public FeeRate(Money feePerK)
        {
            if (feePerK == null)
                throw new ArgumentNullException("feePerK");
            if (feePerK.Satoshi < 0)
                throw new ArgumentOutOfRangeException("feePerK");
            this.FeePerK = feePerK;
        }

        public FeeRate(Money feePaid, int size)
        {
            if (feePaid == null)
                throw new ArgumentNullException("feePaid");
            if (feePaid.Satoshi < 0)
                throw new ArgumentOutOfRangeException("feePaid");
            if (size > 0)
                this.FeePerK = (long) (feePaid.Satoshi / (decimal) size * 1000);
            else
                this.FeePerK = 0;
        }

        /// <summary>
        ///     Fee per KB
        /// </summary>
        public Money FeePerK { get; }

        public static FeeRate Zero { get; } = new FeeRate(Money.Zero);

        /// <summary>
        ///     Get fee for the size
        /// </summary>
        /// <param name="virtualSize">Size in bytes</param>
        /// <returns></returns>
        public Money GetFee(int virtualSize)
        {
            Money nFee = this.FeePerK.Satoshi * virtualSize / 1000;
            if (nFee == 0 && this.FeePerK.Satoshi > 0)
                nFee = this.FeePerK.Satoshi;
            return nFee;
        }

        public Money GetFee(Transaction tx, int witnessScaleFactor)
        {
            return GetFee(tx.GetVirtualSize(witnessScaleFactor));
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
                return true;
            if ((object) this == null || obj == null)
                return false;
            var left = this;
            var right = obj as FeeRate;
            if (right == null)
                return false;
            return left.FeePerK == right.FeePerK;
        }

        public override string ToString()
        {
            return string.Format("{0} BTC/kB", this.FeePerK);
        }

        #region IComparable Members

        public int CompareTo(object obj)
        {
            if (obj == null)
                return 1;
            var m = obj as FeeRate;
            if (m != null)
                return this.FeePerK.CompareTo(m.FeePerK);
#if !NETCORE
            return _FeePerK.CompareTo(obj);
#else
            return this.FeePerK.CompareTo((long) obj);
#endif
        }

        #endregion

        public static bool operator <(FeeRate left, FeeRate right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left.FeePerK < right.FeePerK;
        }

        public static bool operator >(FeeRate left, FeeRate right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left.FeePerK > right.FeePerK;
        }

        public static bool operator <=(FeeRate left, FeeRate right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left.FeePerK <= right.FeePerK;
        }

        public static bool operator >=(FeeRate left, FeeRate right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left.FeePerK >= right.FeePerK;
        }

        public static bool operator ==(FeeRate left, FeeRate right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if ((object) left == null || (object) right == null)
                return false;
            return left.FeePerK == right.FeePerK;
        }

        public static bool operator !=(FeeRate left, FeeRate right)
        {
            return !(left == right);
        }

        public override int GetHashCode()
        {
            return this.FeePerK.GetHashCode();
        }

        public static FeeRate Min(FeeRate left, FeeRate right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left <= right
                ? left
                : right;
        }

        public static FeeRate Max(FeeRate left, FeeRate right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left >= right
                ? left
                : right;
        }

        #region IEquatable<FeeRate> Members

        public bool Equals(FeeRate other)
        {
            return other != null && this.FeePerK.Equals(other.FeePerK);
        }

        public int CompareTo(FeeRate other)
        {
            return other == null
                ? 1
                : this.FeePerK.CompareTo(other.FeePerK);
        }

        #endregion
    }
}