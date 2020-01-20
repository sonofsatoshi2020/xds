using System;
using System.Linq;
using System.Text;
using NBitcoin.BouncyCastle.math;
using NBitcoin.Crypto;
using NBitcoin.Crypto.Cryptsharp;
using NBitcoin.DataEncoders;

namespace NBitcoin.BIP38
{
    public class EncryptedKeyResult
    {
        Func<BitcoinConfirmationCode> _CalculateConfirmation;
        BitcoinConfirmationCode _ConfirmationCode;

        public EncryptedKeyResult(BitcoinEncryptedSecretEC key, BitcoinAddress address, byte[] seed,
            Func<BitcoinConfirmationCode> calculateConfirmation)
        {
            this.EncryptedKey = key;
            this.GeneratedAddress = address;
            this._CalculateConfirmation = calculateConfirmation;
            this.Seed = seed;
        }

        public BitcoinEncryptedSecretEC EncryptedKey { get; }

        public BitcoinConfirmationCode ConfirmationCode
        {
            get
            {
                if (this._ConfirmationCode == null)
                {
                    this._ConfirmationCode = this._CalculateConfirmation();
                    this._CalculateConfirmation = null;
                }

                return this._ConfirmationCode;
            }
        }

        public BitcoinAddress GeneratedAddress { get; }

        public byte[] Seed { get; }
    }

    public class LotSequence
    {
        readonly byte[] _Bytes;

        public LotSequence(int lot, int sequence)
        {
            if (lot > 1048575 || lot < 0)
                throw new ArgumentOutOfRangeException("lot");
            if (sequence > 1024 || sequence < 0)
                throw new ArgumentOutOfRangeException("sequence");

            this.Lot = lot;
            this.Sequence = sequence;
            var lotSequence = (uint) lot * 4096 + (uint) sequence;
            this._Bytes =
                new[]
                {
                    (byte) (lotSequence >> 24),
                    (byte) (lotSequence >> 16),
                    (byte) (lotSequence >> 8),
                    (byte) lotSequence
                };
        }

        public LotSequence(byte[] bytes)
        {
            this._Bytes = bytes.ToArray();
            var lotSequence =
                ((uint) this._Bytes[0] << 24) +
                ((uint) this._Bytes[1] << 16) +
                ((uint) this._Bytes[2] << 8) +
                ((uint) this._Bytes[3] << 0);

            this.Lot = (int) (lotSequence / 4096);
            this.Sequence = (int) (lotSequence - this.Lot);
        }

        public int Lot { get; }

        public int Sequence { get; }

        int Id => Utils.ToInt32(this._Bytes, 0, true);

        public byte[] ToBytes()
        {
            return this._Bytes.ToArray();
        }

        public override bool Equals(object obj)
        {
            var item = obj as LotSequence;
            return item != null && this.Id.Equals(item.Id);
        }

        public static bool operator ==(LotSequence a, LotSequence b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if ((object) a == null || (object) b == null)
                return false;
            return a.Id == b.Id;
        }

        public static bool operator !=(LotSequence a, LotSequence b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }
    }

    public class BitcoinPassphraseCode : Base58Data
    {
        LotSequence _LotSequence;


        byte[] _OwnerEntropy;

        byte[] _Passpoint;

        public BitcoinPassphraseCode(string passphrase, Network network, LotSequence lotsequence,
            byte[] ownersalt = null)
            : base(GenerateWif(passphrase, network, lotsequence, ownersalt), network)
        {
        }

        public BitcoinPassphraseCode(string wif, Network expectedNetwork = null)
            : base(wif, expectedNetwork)
        {
        }

        public LotSequence LotSequence
        {
            get
            {
                var hasLotSequence = this.vchData[0] == 0x51;
                if (!hasLotSequence)
                    return null;
                return this._LotSequence ??
                       (this._LotSequence = new LotSequence(this.OwnerEntropy.Skip(4).Take(4).ToArray()));
            }
        }

        public byte[] OwnerEntropy =>
            this._OwnerEntropy ?? (this._OwnerEntropy = this.vchData.Skip(1).Take(8).ToArray());

        public byte[] Passpoint => this._Passpoint ?? (this._Passpoint = this.vchData.Skip(1).Skip(8).ToArray());

        protected override bool IsValid =>
            1 + 8 + 33 == this.vchData.Length && (this.vchData[0] == 0x53 || this.vchData[0] == 0x51);


        public override Base58Type Type => Base58Type.PASSPHRASE_CODE;

        static string GenerateWif(string passphrase, Network network, LotSequence lotsequence, byte[] ownersalt)
        {
            var hasLotSequence = lotsequence != null;

            //ownersalt is 8 random bytes
            ownersalt = ownersalt ?? RandomUtils.GetBytes(8);
            var ownerEntropy = ownersalt;

            if (hasLotSequence)
            {
                ownersalt = ownersalt.Take(4).ToArray();
                ownerEntropy = ownersalt.Concat(lotsequence.ToBytes()).ToArray();
            }


            var prefactor = SCrypt.BitcoinComputeDerivedKey(Encoding.UTF8.GetBytes(passphrase), ownersalt, 32);
            var passfactor = prefactor;
            if (hasLotSequence) passfactor = Hashes.Hash256(prefactor.Concat(ownerEntropy).ToArray()).ToBytes();

            var passpoint = new Key(passfactor).PubKey.ToBytes();

            var bytes =
                network.GetVersionBytes(Base58Type.PASSPHRASE_CODE, true)
                    .Concat(new[] {hasLotSequence ? (byte) 0x51 : (byte) 0x53})
                    .Concat(ownerEntropy)
                    .Concat(passpoint)
                    .ToArray();
            return Encoders.Base58Check.EncodeData(bytes);
        }

        public EncryptedKeyResult GenerateEncryptedSecret(bool isCompressed = true, byte[] seedb = null)
        {
            //Set flagbyte.
            byte flagByte = 0;
            //Turn on bit 0x20 if the Bitcoin address will be formed by hashing the compressed public key
            flagByte |= isCompressed ? (byte) 0x20 : (byte) 0x00;
            flagByte |= this.LotSequence != null ? (byte) 0x04 : (byte) 0x00;

            //Generate 24 random bytes, call this seedb. Take SHA256(SHA256(seedb)) to yield 32 bytes, call this factorb.
            seedb = seedb ?? RandomUtils.GetBytes(24);

            var factorb = Hashes.Hash256(seedb).ToBytes();

            //ECMultiply passpoint by factorb.
            var curve = ECKey.Secp256k1;
            var passpoint = curve.Curve.DecodePoint(this.Passpoint);
            var pubPoint = passpoint.Multiply(new BigInteger(1, factorb));

            //Use the resulting EC point as a public key
            var pubKey = new PubKey(pubPoint.GetEncoded());

            //and hash it into a Bitcoin address using either compressed or uncompressed public key
            //This is the generated Bitcoin address, call it generatedaddress.
            pubKey = isCompressed ? pubKey.Compress() : pubKey.Decompress();

            //call it generatedaddress.
            var generatedaddress = pubKey.GetAddress(this.Network);

            //Take the first four bytes of SHA256(SHA256(generatedaddress)) and call it addresshash.
            var addresshash = BitcoinEncryptedSecretEC.HashAddress(generatedaddress);

            //Derive a second key from passpoint using scrypt
            //salt is addresshash + ownerentropy
            var derived =
                BitcoinEncryptedSecretEC.CalculateDecryptionKey(this.Passpoint, addresshash, this.OwnerEntropy);

            //Now we will encrypt seedb.
            var encrypted = BitcoinEncryptedSecret.EncryptSeed
            (seedb,
                derived);

            //0x01 0x43 + flagbyte + addresshash + ownerentropy + encryptedpart1[0...7] + encryptedpart2 which totals 39 bytes
            var bytes =
                new[] {flagByte}
                    .Concat(addresshash)
                    .Concat(this.OwnerEntropy)
                    .Concat(encrypted.Take(8).ToArray())
                    .Concat(encrypted.Skip(16).ToArray())
                    .ToArray();

            var encryptedSecret = new BitcoinEncryptedSecretEC(bytes, this.Network);

            return new EncryptedKeyResult(encryptedSecret, generatedaddress, seedb, () =>
            {
                //ECMultiply factorb by G, call the result pointb. The result is 33 bytes.
                var pointb = new Key(factorb).PubKey.ToBytes();
                //The first byte is 0x02 or 0x03. XOR it by (derivedhalf2[31] & 0x01), call the resulting byte pointbprefix.
                var pointbprefix = (byte) (pointb[0] ^ (byte) (derived[63] & 0x01));
                var pointbx = BitcoinEncryptedSecret.EncryptKey(pointb.Skip(1).ToArray(), derived);
                var encryptedpointb = new[] {pointbprefix}.Concat(pointbx).ToArray();

                var confirmBytes = this.Network.GetVersionBytes(Base58Type.CONFIRMATION_CODE, true)
                    .Concat(new[] {flagByte})
                    .Concat(addresshash)
                    .Concat(this.OwnerEntropy)
                    .Concat(encryptedpointb)
                    .ToArray();

                return new BitcoinConfirmationCode(Encoders.Base58Check.EncodeData(confirmBytes), this.Network);
            });
        }
    }
}