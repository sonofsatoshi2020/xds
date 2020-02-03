using System;
using System.Globalization;
using System.Linq;
using NBitcoin.BouncyCastle.math;

namespace NBitcoin
{
    /// <summary>
    ///     Represent the challenge that miners must solve for finding a new block
    /// </summary>
    public class Target
    {
        double? _Difficulty;


        readonly BigInteger _Target;

        public Target(uint compact)
            : this(ToBytes(compact))
        {
        }

        public Target(byte[] compact)
        {
            if (compact.Length == 4)
            {
                var exp = compact[0];
                var val = new BigInteger(compact.SafeSubarray(1, 3));
                this._Target = val.ShiftLeft(8 * (exp - 3));
            }
            else
            {
                throw new FormatException("Invalid number of bytes");
            }
        }

        public Target(BigInteger target)
        {
            this._Target = target;
            this._Target = new Target(ToCompact())._Target;
        }

        public Target(uint256 target)
        {
            this._Target = new BigInteger(target.ToBytes(false));
            this._Target = new Target(ToCompact())._Target;
        }

        public static BigInteger Pow256 = BigInteger.ValueOf(2).Pow(256);

        // XDS:     0x1e0fffff, 0x1e = 30, 0xfffff * 2**(8*(0x1e - 3)) == 0x00000fffff000000000000000000000000000000000000000000000000000000
        // Bitcoin: 0x1d00ffff, 0x1d = 29, 0xffff * 2**(8*(0x1d - 3)) ==  0x00000000FFFF0000000000000000000000000000000000000000000000000000

        public static Target Difficulty1 { get; } = new Target(new byte[] { 0x1e, 0x0f, 0xff, 0xff });
       // public static Target Difficulty1 { get; } = new Target(new byte[] { 0x1d, 0x00, 0xff, 0xff });

        public double Difficulty
        {
            get
            {
                if (this._Difficulty == null)
                {
                    var qr = Difficulty1._Target.DivideAndRemainder(this._Target);
                    var quotient = qr[0];
                    var remainder = qr[1];
                    var decimalPart = BigInteger.Zero;
                    for (var i = 0; i < 12; i++)
                    {
                        var div = remainder.Multiply(BigInteger.Ten).Divide(this._Target);

                        decimalPart = decimalPart.Multiply(BigInteger.Ten);
                        decimalPart = decimalPart.Add(div);

                        remainder = remainder.Multiply(BigInteger.Ten).Subtract(div.Multiply(this._Target));
                    }

                    this._Difficulty = double.Parse(quotient + "." + decimalPart, new NumberFormatInfo
                    {
                        NegativeSign = "-",
                        NumberDecimalSeparator = "."
                    });
                }

                return this._Difficulty.Value;
            }
        }

        static byte[] ToBytes(uint bits)
        {
            return new[]
            {
                (byte) (bits >> 24),
                (byte) (bits >> 16),
                (byte) (bits >> 8),
                (byte) bits
            };
        }

        public static implicit operator Target(uint a)
        {
            return new Target(a);
        }

        public static implicit operator uint(Target a)
        {
            var bytes = a._Target.ToByteArray();
            var val = bytes.SafeSubarray(0, Math.Min(bytes.Length, 3));
            Array.Reverse(val);
            var exp = (byte) bytes.Length;
            if (exp == 1 && bytes[0] == 0)
                exp = 0;
            var missing = 4 - val.Length;
            if (missing > 0)
                val = val.Concat(new byte[missing]).ToArray();
            if (missing < 0)
                val = val.Take(-missing).ToArray();
            return val[0] + (uint) (val[1] << 8) + (uint) (val[2] << 16) + (uint) (exp << 24);
        }

        public override bool Equals(object obj)
        {
            var item = obj as Target;
            if (item == null)
                return false;
            return this._Target.Equals(item._Target);
        }

        public static bool operator ==(Target a, Target b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if ((object) a == null || (object) b == null)
                return false;
            return a._Target.Equals(b._Target);
        }

        public static bool operator !=(Target a, Target b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return this._Target.GetHashCode();
        }

        public BigInteger ToBigInteger()
        {
            return this._Target;
        }

        public uint ToCompact()
        {
            return (uint) this;
        }

        public uint256 ToUInt256()
        {
            return ToUInt256(this._Target);
        }

        internal static uint256 ToUInt256(BigInteger input)
        {
            var array = input.ToByteArray();

            var missingZero = 32 - array.Length;

            if (missingZero < 0)
                throw new InvalidOperationException("Awful bug, this should never happen");

            if (missingZero != 0)
                return new uint256(new byte[missingZero].Concat(array), false);

            return new uint256(array, false);
        }

        public override string ToString()
        {
            return ToUInt256().ToString();
        }
    }
}