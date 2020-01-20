using System;
using System.Linq;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;

namespace NBitcoin.BIP32
{
    /// <summary>
    ///     A public HD key
    /// </summary>
    public class ExtPubKey : IBitcoinSerializable, IDestination
    {
        const int FingerprintLength = 4;
        const int ChainCodeLength = 32;

        static readonly byte[] validPubKey =
            Encoders.Hex.DecodeData("0374ef3990e387b5a2992797f14c031a64efd80e5cb843d7c1d4a0274a9bc75e55");

        internal uint nChild;
        internal byte nDepth;

        //
        internal PubKey pubkey = new PubKey(validPubKey);
        internal byte[] vchChainCode = new byte[ChainCodeLength];
        internal byte[] vchFingerprint = new byte[FingerprintLength];

        internal ExtPubKey()
        {
        }

        public ExtPubKey(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException("bytes");
            this.ReadWrite(bytes);
        }

        public ExtPubKey(PubKey pubkey, byte[] chainCode, byte depth, byte[] fingerprint, uint child)
        {
            if (pubkey == null)
                throw new ArgumentNullException("pubkey");
            if (chainCode == null)
                throw new ArgumentNullException("chainCode");
            if (fingerprint == null)
                throw new ArgumentNullException("fingerprint");
            if (fingerprint.Length != FingerprintLength)
                throw new ArgumentException(string.Format("The fingerprint must be {0} bytes.", FingerprintLength),
                    "fingerprint");
            if (chainCode.Length != ChainCodeLength)
                throw new ArgumentException(string.Format("The chain code must be {0} bytes.", ChainCodeLength),
                    "chainCode");
            this.pubkey = pubkey;
            this.nDepth = depth;
            this.nChild = child;
            Buffer.BlockCopy(fingerprint, 0, this.vchFingerprint, 0, FingerprintLength);
            Buffer.BlockCopy(chainCode, 0, this.vchChainCode, 0, ChainCodeLength);
        }

        public ExtPubKey(PubKey masterKey, byte[] chainCode)
        {
            if (masterKey == null)
                throw new ArgumentNullException("masterKey");
            if (chainCode == null)
                throw new ArgumentNullException("chainCode");
            if (chainCode.Length != ChainCodeLength)
                throw new ArgumentException(string.Format("The chain code must be {0} bytes.", ChainCodeLength),
                    "chainCode");
            this.pubkey = masterKey;
            Buffer.BlockCopy(chainCode, 0, this.vchChainCode, 0, ChainCodeLength);
        }

        public byte Depth => this.nDepth;

        public uint Child => this.nChild;

        public bool IsHardened => (this.nChild & 0x80000000u) != 0;

        public PubKey PubKey => this.pubkey;

        public byte[] ChainCode
        {
            get
            {
                var chainCodeCopy = new byte[ChainCodeLength];
                Buffer.BlockCopy(this.vchChainCode, 0, chainCodeCopy, 0, ChainCodeLength);

                return chainCodeCopy;
            }
        }

        public byte[] Fingerprint => this.vchFingerprint;

        #region IDestination Members

        /// <summary>
        ///     The P2PKH payment script
        /// </summary>
        public Script ScriptPubKey => this.PubKey.Hash.ScriptPubKey;

        #endregion

        public static ExtPubKey Parse(string wif, Network expectedNetwork = null)
        {
            return Network.Parse<BitcoinExtPubKey>(wif, expectedNetwork).ExtPubKey;
        }


        public bool IsChildOf(ExtPubKey parentKey)
        {
            if (this.Depth != parentKey.Depth + 1)
                return false;
            return parentKey.CalculateChildFingerprint().SequenceEqual(this.Fingerprint);
        }

        public bool IsParentOf(ExtPubKey childKey)
        {
            return childKey.IsChildOf(this);
        }

        public byte[] CalculateChildFingerprint()
        {
            return this.pubkey.Hash.ToBytes().SafeSubarray(0, FingerprintLength);
        }

        public ExtPubKey Derive(uint index)
        {
            var result = new ExtPubKey
            {
                nDepth = (byte) (this.nDepth + 1),
                vchFingerprint = CalculateChildFingerprint(),
                nChild = index
            };
            result.pubkey = this.pubkey.Derivate(this.vchChainCode, index, out result.vchChainCode);
            return result;
        }

        public ExtPubKey Derive(KeyPath derivation)
        {
            var result = this;
            return derivation.Indexes.Aggregate(result, (current, index) => current.Derive(index));
        }

        public ExtPubKey Derive(int index, bool hardened)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException("index", "the index can't be negative");
            var realIndex = (uint) index;
            realIndex = hardened ? realIndex | 0x80000000u : realIndex;
            return Derive(realIndex);
        }

        public BitcoinExtPubKey GetWif(Network network)
        {
            return new BitcoinExtPubKey(this, network);
        }

        public string ToString(Network network)
        {
            return new BitcoinExtPubKey(this, network).ToString();
        }

        #region IBitcoinSerializable Members

        public void ReadWrite(BitcoinStream stream)
        {
            using (stream.BigEndianScope())
            {
                stream.ReadWrite(ref this.nDepth);
                stream.ReadWrite(ref this.vchFingerprint);
                stream.ReadWrite(ref this.nChild);
                stream.ReadWrite(ref this.vchChainCode);
                stream.ReadWrite(ref this.pubkey);
            }
        }


        uint256 Hash => Hashes.Hash256(this.ToBytes());

        public override bool Equals(object obj)
        {
            var item = obj as ExtPubKey;
            if (item == null)
                return false;
            return this.Hash.Equals(item.Hash);
        }

        public static bool operator ==(ExtPubKey a, ExtPubKey b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if ((object) a == null || (object) b == null)
                return false;
            return a.Hash == b.Hash;
        }

        public static bool operator !=(ExtPubKey a, ExtPubKey b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return this.Hash.GetHashCode();
        }

        #endregion
    }
}