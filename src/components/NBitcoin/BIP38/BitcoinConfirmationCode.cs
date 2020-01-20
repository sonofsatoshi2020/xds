using System;
using System.Linq;
using NBitcoin.BouncyCastle.math;
using NBitcoin.BouncyCastle.math.ec;
using NBitcoin.Crypto;

namespace NBitcoin.BIP38
{
    public class BitcoinConfirmationCode : Base58Data
    {
        byte[] _AddressHash;

        byte[] _EncryptedPointB;

        LotSequence _LotSequence;

        byte[] _OwnerEntropy;

        public BitcoinConfirmationCode(string wif, Network expectedNetwork = null)
            : base(wif, expectedNetwork)
        {
        }

        public BitcoinConfirmationCode(byte[] rawBytes, Network network)
            : base(rawBytes, network)
        {
        }

        public byte[] AddressHash => this._AddressHash ?? (this._AddressHash = this.vchData.SafeSubarray(1, 4));

        public bool IsCompressed => (this.vchData[0] & 0x20) != 0;

        public byte[] OwnerEntropy => this._OwnerEntropy ?? (this._OwnerEntropy = this.vchData.SafeSubarray(5, 8));

        public LotSequence LotSequence
        {
            get
            {
                var hasLotSequence = (this.vchData[0] & 0x04) != 0;
                if (!hasLotSequence)
                    return null;
                if (this._LotSequence == null)
                    this._LotSequence = new LotSequence(this.OwnerEntropy.SafeSubarray(4, 4));
                return this._LotSequence;
            }
        }

        byte[] EncryptedPointB => this._EncryptedPointB ?? (this._EncryptedPointB = this.vchData.SafeSubarray(13));

        public override Base58Type Type => Base58Type.CONFIRMATION_CODE;

        protected override bool IsValid => this.vchData.Length == 1 + 4 + 8 + 33;


        public bool Check(string passphrase, BitcoinAddress expectedAddress)
        {
            //Derive passfactor using scrypt with ownerentropy and the user's passphrase and use it to recompute passpoint 
            var passfactor =
                BitcoinEncryptedSecretEC.CalculatePassFactor(passphrase, this.LotSequence, this.OwnerEntropy);
            //Derive decryption key for pointb using scrypt with passpoint, addresshash, and ownerentropy
            var passpoint = BitcoinEncryptedSecretEC.CalculatePassPoint(passfactor);
            var derived =
                BitcoinEncryptedSecretEC.CalculateDecryptionKey(passpoint, this.AddressHash, this.OwnerEntropy);

            //Decrypt encryptedpointb to yield pointb
            var pointbprefix = this.EncryptedPointB[0];
            pointbprefix = (byte) (pointbprefix ^ (byte) (derived[63] & 0x01));

            //Optional since ArithmeticException will catch it, but it saves some times
            if (pointbprefix != 0x02 && pointbprefix != 0x03)
                return false;
            var pointb = BitcoinEncryptedSecret.DecryptKey(this.EncryptedPointB.Skip(1).ToArray(), derived);
            pointb = new[] {pointbprefix}.Concat(pointb).ToArray();

            //4.ECMultiply pointb by passfactor. Use the resulting EC point as a public key
            var curve = ECKey.Secp256k1;
            ECPoint pointbec;
            try
            {
                pointbec = curve.Curve.DecodePoint(pointb);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (ArithmeticException)
            {
                return false;
            }

            var pubkey = new PubKey(pointbec.Multiply(new BigInteger(1, passfactor)).GetEncoded());

            //and hash it into address using either compressed or uncompressed public key methodology as specifid in flagbyte.
            pubkey = this.IsCompressed ? pubkey.Compress() : pubkey.Decompress();

            var actualhash = BitcoinEncryptedSecretEC.HashAddress(pubkey.GetAddress(this.Network));
            var expectedhash = BitcoinEncryptedSecretEC.HashAddress(expectedAddress);

            return Utils.ArrayEqual(actualhash, expectedhash);
        }
    }
}