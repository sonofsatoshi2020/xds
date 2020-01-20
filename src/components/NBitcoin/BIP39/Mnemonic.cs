using System;
using System.Linq;
using System.Text;
using NBitcoin.BIP32;
using NBitcoin.BouncyCastle.crypto.digests;
using NBitcoin.BouncyCastle.crypto.macs;
using NBitcoin.BouncyCastle.crypto.parameters;
using NBitcoin.Crypto;
using NBitcoin.Crypto.Cryptsharp;

namespace NBitcoin.BIP39
{
    /// <summary>
    ///     A .NET implementation of the Bitcoin Improvement Proposal - 39 (BIP39)
    ///     BIP39 specification used as reference located here: https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki
    ///     Made by thashiznets@yahoo.com.au
    ///     v1.0.1.1
    ///     I ♥ Bitcoin :)
    ///     Bitcoin:1ETQjMkR1NNh4jwLuN5LxY7bMsHC9PUPSV
    /// </summary>
    public class Mnemonic
    {
        static readonly int[] msArray = {12, 15, 18, 21, 24};
        static readonly int[] csArray = {4, 5, 6, 7, 8};
        static readonly int[] entArray = {128, 160, 192, 224, 256};

        bool? _IsValidChecksum;


        readonly string _Mnemonic;

        public Mnemonic(string mnemonic, Wordlist wordlist = null)
        {
            if (mnemonic == null)
                throw new ArgumentNullException("mnemonic");
            this._Mnemonic = mnemonic.Trim();

            if (wordlist == null)
                wordlist = Wordlist.AutoDetect(mnemonic) ?? Wordlist.English;

            var words = mnemonic.Split(new[] {' ', '　'}, StringSplitOptions.RemoveEmptyEntries);
            //if the sentence is not at least 12 characters or cleanly divisible by 3, it is bad!
            if (!CorrectWordCount(words.Length))
                throw new FormatException("Word count should be equals to 12,15,18,21 or 24");

            this.Words = words;
            this.WordList = wordlist;
            this.Indices = wordlist.ToIndices(words);
        }

        /// <summary>
        ///     Generate a mnemonic
        /// </summary>
        /// <param name="wordList"></param>
        /// <param name="entropy"></param>
        public Mnemonic(Wordlist wordList, byte[] entropy = null)
        {
            wordList = wordList ?? Wordlist.English;
            this.WordList = wordList;
            if (entropy == null)
                entropy = RandomUtils.GetBytes(32);

            var i = Array.IndexOf(entArray, entropy.Length * 8);
            if (i == -1)
                throw new ArgumentException("The length for entropy should be : " + string.Join(",", entArray),
                    "entropy");

            var cs = csArray[i];
            var checksum = Hashes.SHA256(entropy);
            var entcsResult = new BitWriter();

            entcsResult.Write(entropy);
            entcsResult.Write(checksum, cs);
            this.Indices = entcsResult.ToIntegers();
            this.Words = this.WordList.GetWords(this.Indices);
            this._Mnemonic = this.WordList.GetSentence(this.Indices);
        }

        public Mnemonic(Wordlist wordList, WordCount wordCount)
            : this(wordList, GenerateEntropy(wordCount))
        {
        }

        public bool IsValidChecksum
        {
            get
            {
                if (this._IsValidChecksum == null)
                {
                    var i = Array.IndexOf(msArray, this.Indices.Length);
                    var cs = csArray[i];
                    var ent = entArray[i];

                    var writer = new BitWriter();
                    var bits = Wordlist.ToBits(this.Indices);
                    writer.Write(bits, ent);
                    var entropy = writer.ToBytes();
                    var checksum = Hashes.SHA256(entropy);

                    writer.Write(checksum, cs);
                    var expectedIndices = writer.ToIntegers();
                    this._IsValidChecksum = expectedIndices.SequenceEqual(this.Indices);
                }

                return this._IsValidChecksum.Value;
            }
        }

        public Wordlist WordList { get; }

        public int[] Indices { get; }

        public string[] Words { get; }

        static byte[] GenerateEntropy(WordCount wordCount)
        {
            var ms = (int) wordCount;
            if (!CorrectWordCount(ms))
                throw new ArgumentException("Word count should be equal to 12,15,18,21 or 24", "wordCount");
            var i = Array.IndexOf(msArray, (int) wordCount);
            return RandomUtils.GetBytes(entArray[i] / 8);
        }

        static bool CorrectWordCount(int ms)
        {
            return msArray.Any(_ => _ == ms);
        }

        public byte[] DeriveSeed(string passphrase = null)
        {
            passphrase = passphrase ?? "";
            var salt = Concat(Encoding.UTF8.GetBytes("mnemonic"), Normalize(passphrase));
            var bytes = Normalize(this._Mnemonic);

#if NETCORE
            var mac = new HMac(new Sha512Digest());
            mac.Init(new KeyParameter(bytes));
            return Pbkdf2.ComputeDerivedKey(mac, salt, 2048, 64);
#else
            return Pbkdf2.ComputeDerivedKey(new HMACSHA512(bytes), salt, 2048, 64);
#endif
        }

        internal static byte[] Normalize(string str)
        {
            return Encoding.UTF8.GetBytes(NormalizeString(str));
        }

        internal static string NormalizeString(string word)
        {
            return KDTable.NormalizeKD(word);
        }

        public ExtKey DeriveExtKey(string passphrase = null)
        {
            return new ExtKey(DeriveSeed(passphrase));
        }

        static byte[] Concat(byte[] source1, byte[] source2)
        {
            //Most efficient way to merge two arrays this according to http://stackoverflow.com/questions/415291/best-way-to-combine-two-or-more-byte-arrays-in-c-sharp
            var buffer = new byte[source1.Length + source2.Length];
            Buffer.BlockCopy(source1, 0, buffer, 0, source1.Length);
            Buffer.BlockCopy(source2, 0, buffer, source1.Length, source2.Length);

            return buffer;
        }

        public override string ToString()
        {
            return this._Mnemonic;
        }
    }

    public enum WordCount
    {
        Twelve = 12,
        Fifteen = 15,
        Eighteen = 18,
        TwentyOne = 21,
        TwentyFour = 24
    }
}