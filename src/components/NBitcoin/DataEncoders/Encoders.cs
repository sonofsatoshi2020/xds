namespace NBitcoin.DataEncoders
{
    public abstract class DataEncoder
    {
        internal DataEncoder()
        {
        }

        // char.IsWhiteSpace fits well but it match other whitespaces 
        // characters too and also works for unicode characters.
        public static bool IsSpace(char c)
        {
            switch (c)
            {
                case ' ':
                case '\t':
                case '\n':
                case '\v':
                case '\f':
                case '\r':
                    return true;
            }

            return false;
        }

        public string EncodeData(byte[] data)
        {
            return EncodeData(data, 0, data.Length);
        }

        public abstract string EncodeData(byte[] data, int offset, int count);

        public abstract byte[] DecodeData(string encoded);
    }

    public static class Encoders
    {
        static readonly ASCIIEncoder _ASCII = new ASCIIEncoder();

        static readonly HexEncoder _Hex = new HexEncoder();

        static readonly Base58Encoder _Base58 = new Base58Encoder();

        static readonly Base58CheckEncoder _Base58Check = new Base58CheckEncoder();

        static readonly Base64Encoder _Base64 = new Base64Encoder();

        public static DataEncoder ASCII => _ASCII;

        public static DataEncoder Hex => _Hex;

        public static DataEncoder Base58 => _Base58;

        public static DataEncoder Base58Check => _Base58Check;

        public static DataEncoder Base64 => _Base64;

        public static Bech32Encoder Bech32(string hrp)
        {
            return new Bech32Encoder(hrp);
        }

        public static Bech32Encoder Bech32(byte[] hrp)
        {
            return new Bech32Encoder(hrp);
        }
    }
}