using System.Linq;
using NBitcoin.BIP38;
using NBitcoin.DataEncoders;

namespace NBitcoin
{
    public class BitcoinSecret : Base58Data, IDestination, ISecret
    {
        BitcoinPubKeyAddress _address;

        public BitcoinSecret(Key key, Network network)
            : base(ToBytes(key), network)
        {
        }

        public BitcoinSecret(string base58, Network expectedAddress = null)
            : base(base58, expectedAddress)
        {
        }

        public virtual KeyId PubKeyHash => this.PrivateKey.PubKey.Hash;

        public PubKey PubKey => this.PrivateKey.PubKey;

        protected override bool IsValid
        {
            get
            {
                if (this.vchData.Length != 33 && this.vchData.Length != 32)
                    return false;

                if (this.vchData.Length == 33 && this.IsCompressed)
                    return true;
                if (this.vchData.Length == 32 && !this.IsCompressed)
                    return true;
                return false;
            }
        }

        public bool IsCompressed => this.vchData.Length > 32 && this.vchData[32] == 1;

        public override Base58Type Type => Base58Type.SECRET_KEY;

        #region IDestination Members

        public Script ScriptPubKey => GetAddress().ScriptPubKey;

        #endregion

        static byte[] ToBytes(Key key)
        {
            var keyBytes = key.ToBytes();
            if (!key.IsCompressed)
                return keyBytes;
            return keyBytes.Concat(new byte[] {0x01}).ToArray();
        }

        public BitcoinPubKeyAddress GetAddress()
        {
            return this._address ?? (this._address = this.PrivateKey.PubKey.GetAddress(this.Network));
        }

        public BitcoinEncryptedSecret Encrypt(string password)
        {
            return this.PrivateKey.GetEncryptedBitcoinSecret(password, this.Network);
        }


        public BitcoinSecret Copy(bool? compressed)
        {
            if (compressed == null)
                compressed = this.IsCompressed;

            if (compressed.Value && this.IsCompressed)
            {
                return new BitcoinSecret(this.wifData, this.Network);
            }

            var result = Encoders.Base58Check.DecodeData(this.wifData);
            var resultList = result.ToList();

            if (compressed.Value)
                resultList.Insert(resultList.Count, 0x1);
            else
                resultList.RemoveAt(resultList.Count - 1);
            return new BitcoinSecret(Encoders.Base58Check.EncodeData(resultList.ToArray()), this.Network);
        }

        #region ISecret Members

        Key _Key;

        public Key PrivateKey => this._Key ?? (this._Key = new Key(this.vchData, 32, this.IsCompressed));

        #endregion
    }
}