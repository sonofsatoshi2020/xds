using System;
using System.Linq;
using NBitcoin.DataEncoders;

namespace NBitcoin.OpenAsset
{
    public class BitcoinColoredAddress : Base58Data, IDestination
    {
        BitcoinAddress _Address;

        public BitcoinColoredAddress(string base58, Network expectedNetwork = null)
            : base(base58, expectedNetwork)
        {
        }

        public BitcoinColoredAddress(BitcoinAddress address)
            : base(Build(address), address.Network)
        {
        }

        protected override bool IsValid => this.Address != null;

        public BitcoinAddress Address
        {
            get
            {
                if (this._Address == null)
                {
                    var base58 = Encoders.Base58Check.EncodeData(this.vchData);
                    this._Address = BitcoinAddress.Create(base58, this.Network);
                }

                return this._Address;
            }
        }

        public override Base58Type Type => Base58Type.COLORED_ADDRESS;

        #region IDestination Members

        public Script ScriptPubKey => this.Address.ScriptPubKey;

        #endregion

        static byte[] Build(BitcoinAddress address)
        {
            if (address is IBase58Data)
            {
                var b58 = (IBase58Data) address;
                var version = address.Network.GetVersionBytes(b58.Type, true);
                var data = Encoders.Base58Check.DecodeData(b58.ToString()).Skip(version.Length).ToArray();
                return version.Concat(data).ToArray();
            }

            throw new NotSupportedException("Building a colored address out of a non base58 string is not supported");
        }

        public static string GetWrappedBase58(string base58, Network network)
        {
            var coloredVersion = network.GetVersionBytes(Base58Type.COLORED_ADDRESS, true);
            var inner = Encoders.Base58Check.DecodeData(base58);
            inner = inner.Skip(coloredVersion.Length).ToArray();
            return Encoders.Base58Check.EncodeData(inner);
        }
    }
}