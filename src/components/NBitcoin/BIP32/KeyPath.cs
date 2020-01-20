using System;
using System.Globalization;
using System.Linq;

namespace NBitcoin.BIP32
{
    /// <summary>
    ///     Represent a path in the hierarchy of HD keys (BIP32)
    /// </summary>
    public class KeyPath
    {
        readonly uint[] _Indexes;

        string _Path;

        public KeyPath()
        {
            this._Indexes = new uint[0];
        }

        public KeyPath(string path)
        {
            this._Indexes =
                path
                    .Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => p != "m")
                    .Select(ParseCore)
                    .ToArray();
        }

        public KeyPath(params uint[] indexes)
        {
            this._Indexes = indexes;
        }

        public uint this[int index] => this._Indexes[index];

        public uint[] Indexes => this._Indexes.ToArray();

        public KeyPath Parent
        {
            get
            {
                if (this._Indexes.Length == 0)
                    return null;
                return new KeyPath(this._Indexes.Take(this._Indexes.Length - 1).ToArray());
            }
        }

        public bool IsHardened
        {
            get
            {
                if (this._Indexes.Length == 0)
                    throw new InvalidOperationException("No indice found in this KeyPath");
                return (this._Indexes[this._Indexes.Length - 1] & 0x80000000u) != 0;
            }
        }

        /// <summary>
        ///     Parse a KeyPath
        /// </summary>
        /// <param name="path">The KeyPath formated like 10/0/2'/3</param>
        /// <returns></returns>
        public static KeyPath Parse(string path)
        {
            var parts = path
                .Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p != "m")
                .Select(ParseCore)
                .ToArray();
            return new KeyPath(parts);
        }

        static uint ParseCore(string i)
        {
            var hardened = i.EndsWith("'");
            var nonhardened = hardened ? i.Substring(0, i.Length - 1) : i;
            var index = uint.Parse(nonhardened);
            return hardened ? index | 0x80000000u : index;
        }

        public KeyPath Derive(int index, bool hardened)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException("index", "the index can't be negative");
            var realIndex = (uint) index;
            realIndex = hardened ? realIndex | 0x80000000u : realIndex;
            return Derive(new KeyPath(realIndex));
        }

        public KeyPath Derive(uint index)
        {
            return Derive(new KeyPath(index));
        }

        public KeyPath Derive(KeyPath derivation)
        {
            return new KeyPath(this._Indexes
                .Concat(derivation._Indexes)
                .ToArray());
        }

        public KeyPath Increment()
        {
            if (this._Indexes.Length == 0)
                return null;
            var indices = this._Indexes.ToArray();
            indices[indices.Length - 1]++;
            return new KeyPath(indices);
        }

        public override bool Equals(object obj)
        {
            var item = obj as KeyPath;
            if (item == null)
                return false;
            return ToString().Equals(item.ToString());
        }

        public static bool operator ==(KeyPath a, KeyPath b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if ((object) a == null || (object) b == null)
                return false;
            return a.ToString() == b.ToString();
        }

        public static bool operator !=(KeyPath a, KeyPath b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public override string ToString()
        {
            return this._Path ?? (this._Path = string.Join("/", this._Indexes.Select(ToString).ToArray()));
        }

        static string ToString(uint i)
        {
            var hardened = (i & 0x80000000u) != 0;
            var nonhardened = i & ~0x80000000u;
            return hardened ? nonhardened + "'" : nonhardened.ToString(CultureInfo.InvariantCulture);
        }
    }
}