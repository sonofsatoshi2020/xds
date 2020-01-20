using System;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using NBitcoin.BouncyCastle.math;
using NBitcoin.Crypto;
using NBitcoin.Crypto.Cryptsharp;
using NBitcoin.DataEncoders;

namespace NBitcoin.BIP38
{
    public class BitcoinEncryptedSecretNoEC : BitcoinEncryptedSecret
    {
        byte[] _Encrypted;

        byte[] _FirstHalf;

        public BitcoinEncryptedSecretNoEC(string wif, Network expectedNetwork = null)
            : base(wif, expectedNetwork)
        {
        }

        public BitcoinEncryptedSecretNoEC(byte[] raw, Network network)
            : base(raw, network)
        {
        }

        public BitcoinEncryptedSecretNoEC(Key key, string password, Network network)
            : base(GenerateWif(key, password, network), network)
        {
        }

        public byte[] EncryptedHalf1 =>
            this._FirstHalf ?? (this._FirstHalf = this.vchData.SafeSubarray(this.ValidLength - 32, 16));

        public byte[] Encrypted =>
            this._Encrypted ?? (this._Encrypted = this.EncryptedHalf1.Concat(this.EncryptedHalf2).ToArray());

        public override Base58Type Type => Base58Type.ENCRYPTED_SECRET_KEY_NO_EC;

        static string GenerateWif(Key key, string password, Network network)
        {
            var vch = key.ToBytes();
            //Compute the Bitcoin address (ASCII),
            byte[] addressBytes;
            try
            {
                //Compute the Bitcoin address (ASCII),
                addressBytes = Encoders.ASCII.DecodeData(key.PubKey.GetAddress(network).ToString());
            }
            catch (Exception)
            {
                addressBytes =
                    key.PubKey
                        .ToBytes(); // in case there is no Base58Type.PUBKEY_ADDRESS defined, we deviate from the WIF Spec
            }

            // and take the first four bytes of SHA256(SHA256()) of it. Let's call this "addresshash".
            var addresshash = Hashes.Hash256(addressBytes).ToBytes().SafeSubarray(0, 4);

            var derived = SCrypt.BitcoinComputeDerivedKey(Encoding.UTF8.GetBytes(password), addresshash);

            var encrypted = EncryptKey(vch, derived);


            var version = network.GetVersionBytes(Base58Type.ENCRYPTED_SECRET_KEY_NO_EC, true);
            byte flagByte = 0;
            flagByte |= 0x0C0;
            flagByte |= key.IsCompressed ? (byte) 0x20 : (byte) 0x00;

            var bytes = version
                .Concat(new[] {flagByte})
                .Concat(addresshash)
                .Concat(encrypted).ToArray();
            return Encoders.Base58Check.EncodeData(bytes);
        }

        public override Key GetKey(string password)
        {
            var derived = SCrypt.BitcoinComputeDerivedKey(password, this.AddressHash);
            var bitcoinprivkey = DecryptKey(this.Encrypted, derived);

            var key = new Key(bitcoinprivkey, fCompressedIn: this.IsCompressed);

            var addressBytes = Encoders.ASCII.DecodeData(key.PubKey.GetAddress(this.Network).ToString());
            var salt = Hashes.Hash256(addressBytes).ToBytes().SafeSubarray(0, 4);

            if (!Utils.ArrayEqual(salt, this.AddressHash))
                throw new SecurityException("Invalid password (or invalid Network)");
            return key;
        }
    }

    public class DecryptionResult
    {
        public Key Key { get; set; }

        public LotSequence LotSequence { get; set; }
    }

    public class BitcoinEncryptedSecretEC : BitcoinEncryptedSecret
    {
        byte[] _EncryptedHalfHalf1;

        LotSequence _LotSequence;

        byte[] _OwnerEntropy;

        byte[] _PartialEncrypted;

        public BitcoinEncryptedSecretEC(string wif, Network expectedNetwork = null)
            : base(wif, expectedNetwork)
        {
        }

        public BitcoinEncryptedSecretEC(byte[] raw, Network network)
            : base(raw, network)
        {
        }

        public byte[] OwnerEntropy => this._OwnerEntropy ??
                                      (this._OwnerEntropy = this.vchData.SafeSubarray(this.ValidLength - 32, 8));

        public LotSequence LotSequence
        {
            get
            {
                var hasLotSequence = (this.vchData[0] & 0x04) != 0;
                if (!hasLotSequence)
                    return null;
                return this._LotSequence ?? (this._LotSequence = new LotSequence(this.OwnerEntropy.SafeSubarray(4, 4)));
            }
        }

        public byte[] EncryptedHalfHalf1 => this._EncryptedHalfHalf1 ??
                                            (this._EncryptedHalfHalf1 =
                                                this.vchData.SafeSubarray(this.ValidLength - 32 + 8, 8));

        public byte[] PartialEncrypted => this._PartialEncrypted ?? (this._PartialEncrypted =
                                              this.EncryptedHalfHalf1.Concat(new byte[8]).Concat(this.EncryptedHalf2)
                                                  .ToArray());


        public override Base58Type Type => Base58Type.ENCRYPTED_SECRET_KEY_EC;

        public override Key GetKey(string password)
        {
            var encrypted = this.PartialEncrypted.ToArray();
            //Derive passfactor using scrypt with ownerentropy and the user's passphrase and use it to recompute passpoint
            var passfactor = CalculatePassFactor(password, this.LotSequence, this.OwnerEntropy);
            var passpoint = CalculatePassPoint(passfactor);

            var derived =
                SCrypt.BitcoinComputeDerivedKey2(passpoint, this.AddressHash.Concat(this.OwnerEntropy).ToArray());

            //Decrypt encryptedpart1 to yield the remainder of seedb.
            var seedb = DecryptSeed(encrypted, derived);
            var factorb = Hashes.Hash256(seedb).ToBytes();

            var curve = ECKey.Secp256k1;

            //Multiply passfactor by factorb mod N to yield the private key associated with generatedaddress.
            var keyNum = new BigInteger(1, passfactor).Multiply(new BigInteger(1, factorb)).Mod(curve.N);
            var keyBytes = keyNum.ToByteArrayUnsigned();
            if (keyBytes.Length < 32)
                keyBytes = new byte[32 - keyBytes.Length].Concat(keyBytes).ToArray();

            var key = new Key(keyBytes, fCompressedIn: this.IsCompressed);

            var generatedaddress = key.PubKey.GetAddress(this.Network);
            var addresshash = HashAddress(generatedaddress);

            if (!Utils.ArrayEqual(addresshash, this.AddressHash))
                throw new SecurityException("Invalid password (or invalid Network)");

            return key;
        }

        /// <summary>
        ///     Take the first four bytes of SHA256(SHA256(generatedaddress)) and call it addresshash.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        internal static byte[] HashAddress(BitcoinAddress address)
        {
            return Hashes.Hash256(Encoders.ASCII.DecodeData(address.ToString())).ToBytes().Take(4).ToArray();
        }

        internal static byte[] CalculatePassPoint(byte[] passfactor)
        {
            return new Key(passfactor).PubKey.ToBytes();
        }

        internal static byte[] CalculatePassFactor(string password, LotSequence lotSequence, byte[] ownerEntropy)
        {
            byte[] passfactor;
            if (lotSequence == null)
            {
                passfactor = SCrypt.BitcoinComputeDerivedKey(Encoding.UTF8.GetBytes(password), ownerEntropy, 32);
            }
            else
            {
                var ownersalt = ownerEntropy.SafeSubarray(0, 4);
                var prefactor = SCrypt.BitcoinComputeDerivedKey(Encoding.UTF8.GetBytes(password), ownersalt, 32);
                passfactor = Hashes.Hash256(prefactor.Concat(ownerEntropy).ToArray()).ToBytes();
            }

            return passfactor;
        }

        internal static byte[] CalculateDecryptionKey(byte[] Passpoint, byte[] addresshash, byte[] ownerEntropy)
        {
            return SCrypt.BitcoinComputeDerivedKey2(Passpoint, addresshash.Concat(ownerEntropy).ToArray());
        }
    }

    public abstract class BitcoinEncryptedSecret : Base58Data
    {
        byte[] _AddressHash;

        byte[] _LastHalf;
        protected int ValidLength = 1 + 4 + 16 + 16;


        protected BitcoinEncryptedSecret(byte[] raw, Network network)
            : base(raw, network)
        {
        }

        protected BitcoinEncryptedSecret(string wif, Network network)
            : base(wif, network)
        {
        }


        public bool EcMultiply => this is BitcoinEncryptedSecretEC;

        public byte[] AddressHash => this._AddressHash ?? (this._AddressHash = this.vchData.SafeSubarray(1, 4));

        public bool IsCompressed => (this.vchData[0] & 0x20) != 0;

        public byte[] EncryptedHalf2 =>
            this._LastHalf ?? (this._LastHalf = this.vchData.Skip(this.ValidLength - 16).ToArray());


        protected override bool IsValid
        {
            get
            {
                var lenOk = this.vchData.Length == this.ValidLength;
                if (!lenOk)
                    return false;
                var reserved = (this.vchData[0] & 0x10) == 0 && (this.vchData[0] & 0x08) == 0;
                return reserved;
            }
        }

        public static BitcoinEncryptedSecret Create(string wif, Network expectedNetwork = null)
        {
            return Network.Parse<BitcoinEncryptedSecret>(wif, expectedNetwork);
        }

        public static BitcoinEncryptedSecretNoEC Generate(Key key, string password, Network network)
        {
            return new BitcoinEncryptedSecretNoEC(key, password, network);
        }

        public abstract Key GetKey(string password);

        public BitcoinSecret GetSecret(string password)
        {
            return new BitcoinSecret(GetKey(password), this.Network);
        }

        internal static Aes CreateAES256()
        {
            var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.ECB;
            aes.IV = new byte[16];
            return aes;
        }

        internal static byte[] EncryptKey(byte[] key, byte[] derived)
        {
            var keyhalf1 = key.SafeSubarray(0, 16);
            var keyhalf2 = key.SafeSubarray(16, 16);
            return EncryptKey(keyhalf1, keyhalf2, derived);
        }

        static byte[] EncryptKey(byte[] keyhalf1, byte[] keyhalf2, byte[] derived)
        {
            var derivedhalf1 = derived.SafeSubarray(0, 32);
            var derivedhalf2 = derived.SafeSubarray(32, 32);

            var encryptedhalf1 = new byte[16];
            var encryptedhalf2 = new byte[16];

            var aes = CreateAES256();
            aes.Key = derivedhalf2;
            var encrypt = aes.CreateEncryptor();

            for (var i = 0; i < 16; i++) derivedhalf1[i] = (byte) (keyhalf1[i] ^ derivedhalf1[i]);

            encrypt.TransformBlock(derivedhalf1, 0, 16, encryptedhalf1, 0);

            for (var i = 0; i < 16; i++) derivedhalf1[16 + i] = (byte) (keyhalf2[i] ^ derivedhalf1[16 + i]);
            encrypt.TransformBlock(derivedhalf1, 16, 16, encryptedhalf2, 0);

            return encryptedhalf1.Concat(encryptedhalf2).ToArray();
        }

        internal static byte[] DecryptKey(byte[] encrypted, byte[] derived)
        {
            var derivedhalf1 = derived.SafeSubarray(0, 32);
            var derivedhalf2 = derived.SafeSubarray(32, 32);

            var encryptedHalf1 = encrypted.SafeSubarray(0, 16);
            var encryptedHalf2 = encrypted.SafeSubarray(16, 16);

            var bitcoinprivkey1 = new byte[16];
            var bitcoinprivkey2 = new byte[16];

            var aes = CreateAES256();
            aes.Key = derivedhalf2;
            var decrypt = aes.CreateDecryptor();
            //Need to call that two time, seems AES bug
            decrypt.TransformBlock(encryptedHalf1, 0, 16, bitcoinprivkey1, 0);
            decrypt.TransformBlock(encryptedHalf1, 0, 16, bitcoinprivkey1, 0);

            for (var i = 0; i < 16; i++) bitcoinprivkey1[i] ^= derivedhalf1[i];

            //Need to call that two time, seems AES bug
            decrypt.TransformBlock(encryptedHalf2, 0, 16, bitcoinprivkey2, 0);
            decrypt.TransformBlock(encryptedHalf2, 0, 16, bitcoinprivkey2, 0);

            for (var i = 0; i < 16; i++) bitcoinprivkey2[i] ^= derivedhalf1[16 + i];

            return bitcoinprivkey1.Concat(bitcoinprivkey2).ToArray();
        }


        internal static byte[] EncryptSeed(byte[] seedb, byte[] derived)
        {
            var derivedhalf1 = derived.SafeSubarray(0, 32);
            var derivedhalf2 = derived.SafeSubarray(32, 32);

            var encryptedhalf1 = new byte[16];
            var encryptedhalf2 = new byte[16];

            var aes = CreateAES256();
            aes.Key = derivedhalf2;
            var encrypt = aes.CreateEncryptor();

            //AES256Encrypt(seedb[0...15] xor derivedhalf1[0...15], derivedhalf2), call the 16-byte result encryptedpart1
            for (var i = 0; i < 16; i++) derivedhalf1[i] = (byte) (seedb[i] ^ derivedhalf1[i]);

            encrypt.TransformBlock(derivedhalf1, 0, 16, encryptedhalf1, 0);

            //AES256Encrypt((encryptedpart1[8...15] + seedb[16...23]) xor derivedhalf1[16...31], derivedhalf2), call the 16-byte result encryptedpart2. The "+" operator is concatenation.
            var half = encryptedhalf1.SafeSubarray(8, 8).Concat(seedb.SafeSubarray(16, 8)).ToArray();
            for (var i = 0; i < 16; i++) derivedhalf1[16 + i] = (byte) (half[i] ^ derivedhalf1[16 + i]);

            encrypt.TransformBlock(derivedhalf1, 16, 16, encryptedhalf2, 0);

            return encryptedhalf1.Concat(encryptedhalf2).ToArray();
        }

        internal static byte[] DecryptSeed(byte[] encrypted, byte[] derived)
        {
            var seedb = new byte[24];
            var derivedhalf1 = derived.SafeSubarray(0, 32);
            var derivedhalf2 = derived.SafeSubarray(32, 32);

            var encryptedhalf2 = encrypted.SafeSubarray(16, 16);

            var aes = CreateAES256();
            aes.Key = derivedhalf2;
            var decrypt = aes.CreateDecryptor();

            var half = new byte[16];
            //Decrypt encryptedpart2 using AES256Decrypt to yield the last 8 bytes of seedb and the last 8 bytes of encryptedpart1.

            decrypt.TransformBlock(encryptedhalf2, 0, 16, half, 0);
            decrypt.TransformBlock(encryptedhalf2, 0, 16, half, 0);

            //half = (encryptedpart1[8...15] + seedb[16...23]) xor derivedhalf1[16...31])
            for (var i = 0; i < 16; i++) half[i] = (byte) (half[i] ^ derivedhalf1[16 + i]);

            //half =  (encryptedpart1[8...15] + seedb[16...23])
            for (var i = 0; i < 8; i++) seedb[seedb.Length - i - 1] = half[half.Length - i - 1];
            //Restore missing encrypted part
            for (var i = 0; i < 8; i++) encrypted[i + 8] = half[i];
            var encryptedhalf1 = encrypted.SafeSubarray(0, 16);

            decrypt.TransformBlock(encryptedhalf1, 0, 16, seedb, 0);
            decrypt.TransformBlock(encryptedhalf1, 0, 16, seedb, 0);

            //seedb = seedb[0...15] xor derivedhalf1[0...15]
            for (var i = 0; i < 16; i++) seedb[i] = (byte) (seedb[i] ^ derivedhalf1[i]);
            return seedb;
        }
    }
}