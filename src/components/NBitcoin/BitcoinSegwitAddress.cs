using System;
using NBitcoin.DataEncoders;

namespace NBitcoin
{
    public class BitcoinWitPubKeyAddress : BitcoinAddress, IBech32Data
    {
        public BitcoinWitPubKeyAddress(string bech32, Network expectedNetwork)
            : base(Validate(bech32, expectedNetwork), expectedNetwork)
        {
            var encoder = expectedNetwork.GetBech32Encoder(Bech32Type.WITNESS_PUBKEY_ADDRESS, true);
            byte witVersion;
            var decoded = encoder.Decode(bech32, out witVersion);
            this.Hash = new WitKeyId(decoded);
        }

        public BitcoinWitPubKeyAddress(WitKeyId segwitKeyId, Network network) :
            base(
                NotNull(segwitKeyId) ??
                Network.CreateBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, segwitKeyId.ToBytes(), 0, network), network)
        {
            this.Hash = segwitKeyId;
        }

        public WitKeyId Hash { get; }

        public Bech32Type Type => Bech32Type.WITNESS_PUBKEY_ADDRESS;

        static string Validate(string bech32, Network expectedNetwork)
        {
            var isValid = IsValid(bech32, expectedNetwork, out var exception);

            if (exception != null)
                throw exception;

            if (isValid)
                return bech32;

            throw new FormatException("Invalid BitcoinWitPubKeyAddress");
        }

        public static bool IsValid(string bech32, Network expectedNetwork, out Exception exception)
        {
            exception = null;

            if (bech32 == null)
            {
                exception = new ArgumentNullException("bech32");
                return false;
            }

            var encoder = expectedNetwork.GetBech32Encoder(Bech32Type.WITNESS_PUBKEY_ADDRESS, false);
            if (encoder == null)
                return false;

            try
            {
                byte witVersion;
                var data = encoder.Decode(bech32, out witVersion);
                if (data.Length == 20 && witVersion == 0) return true;
            }
            catch (Bech32FormatException bech32FormatException)
            {
                exception = bech32FormatException;
                return false;
            }
            catch (FormatException)
            {
                exception = new FormatException("Invalid BitcoinWitPubKeyAddress");
                return false;
            }

            exception = new FormatException("Invalid BitcoinWitScriptAddress");
            return false;
        }

        static string NotNull(WitKeyId segwitKeyId)
        {
            if (segwitKeyId == null)
                throw new ArgumentNullException("segwitKeyId");
            return null;
        }

        public bool VerifyMessage(string message, string signature)
        {
            var key = PubKey.RecoverFromMessage(message, signature);
            return key.WitHash == this.Hash;
        }


        protected override Script GeneratePaymentScript()
        {
            return PayToWitTemplate.Instance.GenerateScriptPubKey(OpcodeType.OP_0, this.Hash._DestBytes);
        }
    }

    public class BitcoinWitScriptAddress : BitcoinAddress, IBech32Data
    {
        public BitcoinWitScriptAddress(string bech32, Network expectedNetwork = null)
            : base(Validate(bech32, expectedNetwork), expectedNetwork)
        {
            var encoder = expectedNetwork.GetBech32Encoder(Bech32Type.WITNESS_SCRIPT_ADDRESS, true);
            byte witVersion;
            var decoded = encoder.Decode(bech32, out witVersion);
            this.Hash = new WitScriptId(decoded);
        }

        public BitcoinWitScriptAddress(WitScriptId segwitScriptId, Network network)
            : base(
                NotNull(segwitScriptId) ?? Network.CreateBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS,
                    segwitScriptId.ToBytes(), 0, network), network)
        {
            this.Hash = segwitScriptId;
        }

        public WitScriptId Hash { get; }

        public Bech32Type Type => Bech32Type.WITNESS_SCRIPT_ADDRESS;

        static string Validate(string bech32, Network expectedNetwork)
        {
            var isValid = IsValid(bech32, expectedNetwork, out var exception);

            if (exception != null)
                throw exception;

            if (isValid)
                return bech32;

            throw new FormatException("Invalid BitcoinWitScriptAddress");
        }

        public static bool IsValid(string bech32, Network expectedNetwork, out Exception exception)
        {
            exception = null;

            if (bech32 == null)
            {
                exception = new ArgumentNullException("bech32");
                return false;
            }

            var encoder = expectedNetwork.GetBech32Encoder(Bech32Type.WITNESS_SCRIPT_ADDRESS, false);
            if (encoder == null)
                return false;
            try
            {
                byte witVersion;
                var data = encoder.Decode(bech32, out witVersion);
                if (data.Length == 32 && witVersion == 0) return true;
            }
            catch (Bech32FormatException bech32FormatException)
            {
                exception = bech32FormatException;
                return false;
            }
            catch (FormatException)
            {
                exception = new FormatException("Invalid BitcoinWitPubKeyAddress");
                return false;
            }

            exception = new FormatException("Invalid BitcoinWitPubKeyAddress");
            return false;
        }


        static string NotNull(WitScriptId segwitScriptId)
        {
            if (segwitScriptId == null)
                throw new ArgumentNullException("segwitScriptId");
            return null;
        }

        protected override Script GeneratePaymentScript()
        {
            return PayToWitTemplate.Instance.GenerateScriptPubKey(OpcodeType.OP_0, this.Hash._DestBytes);
        }
    }
}