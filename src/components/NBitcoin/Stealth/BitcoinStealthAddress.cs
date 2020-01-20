using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NBitcoin.Stealth
{
    public class BitField
    {
        readonly byte[] _Rawform;

        public BitField(byte[] rawform, int bitcount)
        {
            if (rawform == null)
                throw new ArgumentNullException("rawform");
            this.BitCount = bitcount;

            var byteCount = GetPrefixByteLength(bitcount);
            if (rawform.Length == byteCount) this._Rawform = rawform.ToArray();
            if (rawform.Length < byteCount)
                this._Rawform = rawform.Concat(new byte[byteCount - rawform.Length]).ToArray();
            if (rawform.Length > byteCount) this._Rawform = rawform.SafeSubarray(0, byteCount);

            this.Mask = new byte[byteCount];
            var bitleft = bitcount;

            for (var i = 0; i < byteCount; i++)
            {
                var numberBits = Math.Min(8, bitleft);
                this.Mask[i] = (byte) ((1 << numberBits) - 1);
                bitleft -= numberBits;
                if (bitleft == 0)
                    break;
            }
        }

        public BitField(uint encodedForm, int bitcount)
            : this(Utils.ToBytes(encodedForm, true), bitcount)
        {
        }

        public int BitCount { get; }

        public int ByteCount => this._Rawform.Length;

        public byte[] Mask { get; }

        public static int GetPrefixByteLength(int bitcount)
        {
            if (bitcount > 32)
                throw new ArgumentException("Bitcount should be less or equal to 32", "bitcount");
            if (bitcount == 0)
                return 0;
            return Math.Min(4, bitcount / 8 + 1);
        }

        public byte[] GetRawForm()
        {
            return this._Rawform.ToArray();
        }

        public uint GetEncodedForm()
        {
            var encoded = this._Rawform.Length == 4
                ? this._Rawform
                : this._Rawform.Concat(new byte[4 - this._Rawform.Length]).ToArray();

            return Utils.ToUInt32(encoded, true);
        }

        public bool Match(uint value)
        {
            var data = Utils.ToBytes(value, true);
            if (data.Length * 8 < this.BitCount)
                return false;

            for (var i = 0; i < this.Mask.Length; i++)
                if ((data[i] & this.Mask[i]) != (this._Rawform[i] & this.Mask[i]))
                    return false;
            return true;
        }

        public bool Match(StealthMetadata metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException("metadata");
            return Match(metadata.BitField);
        }

        public StealthPayment[] GetPayments(Transaction transaction)
        {
            return StealthPayment.GetPayments(transaction, null, null).Where(p => Match(p.Metadata)).ToArray();
        }
    }

    public class BitcoinStealthAddress : Base58Data
    {
        public BitcoinStealthAddress(string base58, Network expectedNetwork = null)
            : base(base58, expectedNetwork)
        {
        }

        public BitcoinStealthAddress(byte[] raw, Network network)
            : base(raw, network)
        {
        }


        public BitcoinStealthAddress(PubKey scanKey, PubKey[] pubKeys, int signatureCount, BitField bitfield,
            Network network)
            : base(GenerateBytes(scanKey, pubKeys, bitfield, signatureCount), network)
        {
        }


        public byte Options { get; private set; }

        public byte SignatureCount { get; set; }

        public PubKey ScanPubKey { get; private set; }

        public PubKey[] SpendPubKeys { get; private set; }

        public BitField Prefix { get; private set; }

        protected override bool IsValid
        {
            get
            {
                try
                {
                    var ms = new MemoryStream(this.vchData);
                    this.Options = (byte) ms.ReadByte();
                    this.ScanPubKey = new PubKey(ms.ReadBytes(33));
                    var pubkeycount = (byte) ms.ReadByte();
                    var pubKeys = new List<PubKey>();
                    for (var i = 0; i < pubkeycount; i++) pubKeys.Add(new PubKey(ms.ReadBytes(33)));

                    this.SpendPubKeys = pubKeys.ToArray();
                    this.SignatureCount = (byte) ms.ReadByte();

                    var bitcount = (byte) ms.ReadByte();
                    var byteLength = BitField.GetPrefixByteLength(bitcount);

                    var prefix = ms.ReadBytes(byteLength);
                    this.Prefix = new BitField(prefix, bitcount);
                }
                catch (Exception)
                {
                    return false;
                }

                return true;
            }
        }

        public override Base58Type Type => Base58Type.STEALTH_ADDRESS;

        static byte[] GenerateBytes(PubKey scanKey, PubKey[] pubKeys, BitField bitField, int signatureCount)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0); //Options
            ms.Write(scanKey.Compress().ToBytes(), 0, 33);
            ms.WriteByte((byte) pubKeys.Length);
            foreach (var key in pubKeys) ms.Write(key.Compress().ToBytes(), 0, 33);
            ms.WriteByte((byte) signatureCount);
            if (bitField == null)
            {
                ms.Write(new byte[] {0}, 0, 1);
            }
            else
            {
                ms.WriteByte((byte) bitField.BitCount);
                var raw = bitField.GetRawForm();
                ms.Write(raw, 0, raw.Length);
            }

            return ms.ToArray();
        }


        /// <summary>
        ///     Scan the Transaction for StealthCoin given address and scan key
        /// </summary>
        /// <param name="tx">The transaction to scan</param>
        /// <param name="address">The stealth address</param>
        /// <param name="scan">The scan private key</param>
        /// <returns></returns>
        public StealthPayment[] GetPayments(Transaction transaction, Key scanKey)
        {
            return StealthPayment.GetPayments(transaction, this, scanKey);
        }

        /// <summary>
        ///     Scan the Transaction for StealthCoin given address and scan key
        /// </summary>
        /// <param name="tx">The transaction to scan</param>
        /// <param name="address">The stealth address</param>
        /// <param name="scan">The scan private key</param>
        /// <returns></returns>
        public StealthPayment[] GetPayments(Transaction transaction, ISecret scanKey)
        {
            if (transaction == null)
                throw new ArgumentNullException("transaction");
            if (scanKey == null)
                throw new ArgumentNullException("scanKey");
            return GetPayments(transaction, scanKey.PrivateKey);
        }

        /// <summary>
        ///     Prepare a stealth payment
        /// </summary>
        /// <param name="ephemKey">Ephem Key</param>
        /// <returns>Stealth Payment</returns>
        public StealthPayment CreatePayment(Key ephemKey = null)
        {
            if (ephemKey == null)
                ephemKey = new Key();

            var metadata = StealthMetadata.CreateMetadata(ephemKey, this.Prefix);
            return new StealthPayment(this, ephemKey, metadata);
        }

        /// <summary>
        ///     Add a stealth payment to the transaction
        /// </summary>
        /// <param name="transaction">Destination transaction</param>
        /// <param name="value">Money to send</param>
        /// <param name="ephemKey">Ephem Key</param>
        public void SendTo(Transaction transaction, Money value, Key ephemKey = null)
        {
            CreatePayment(ephemKey).AddToTransaction(transaction, value);
        }
    }
}