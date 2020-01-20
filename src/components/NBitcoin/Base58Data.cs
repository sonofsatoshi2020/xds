using System;
using System.Linq;
using NBitcoin.DataEncoders;
using NBitcoin.Networks;

namespace NBitcoin
{
    public interface IBase58Data : IBitcoinString
    {
        Base58Type Type { get; }
    }

    /// <summary>
    ///     Base class for all Base58 check representation of data
    /// </summary>
    public abstract class Base58Data : IBase58Data
    {
        protected byte[] vchData = new byte[0];
        protected byte[] vchVersion = new byte[0];
        protected string wifData = "";

        protected Base58Data(string base64, Network expectedNetwork = null)
        {
            this.Network = expectedNetwork;
            SetString(base64);
        }

        protected Base58Data(byte[] rawBytes, Network network)
        {
            if (network == null)
                throw new ArgumentNullException("network");
            this.Network = network;
            SetData(rawBytes);
        }


        protected virtual bool IsValid => true;

        public Network Network { get; set; }

        public abstract Base58Type Type { get; }

        void SetString(string base64)
        {
            if (this.Network == null)
            {
                this.Network = NetworkRegistration.GetNetworkFromBase58Data(base64, this.Type);
                if (this.Network == null)
                    throw new FormatException("Invalid " + GetType().Name);
            }

            var vchTemp = Encoders.Base58Check.DecodeData(base64);
            var expectedVersion = this.Network.GetVersionBytes(this.Type, true);


            this.vchVersion = vchTemp.SafeSubarray(0, expectedVersion.Length);
            if (!Utils.ArrayEqual(this.vchVersion, expectedVersion))
                throw new FormatException("The version prefix does not match the expected one " +
                                          string.Join(",", expectedVersion));

            this.vchData = vchTemp.SafeSubarray(expectedVersion.Length);
            this.wifData = base64;

            if (!this.IsValid)
                throw new FormatException("Invalid " + GetType().Name);
        }


        void SetData(byte[] vchData)
        {
            this.vchData = vchData;
            this.vchVersion = this.Network.GetVersionBytes(this.Type, true);
            this.wifData = Encoders.Base58Check.EncodeData(this.vchVersion.Concat(vchData).ToArray());

            if (!this.IsValid)
                throw new FormatException("Invalid " + GetType().Name);
        }


        public string ToWif()
        {
            return this.wifData;
        }

        public byte[] ToBytes()
        {
            return this.vchData.ToArray();
        }

        public override string ToString()
        {
            return this.wifData;
        }

        public override bool Equals(object obj)
        {
            var item = obj as Base58Data;
            if (item == null)
                return false;
            return ToString().Equals(item.ToString());
        }

        public static bool operator ==(Base58Data a, Base58Data b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if ((object) a == null || (object) b == null)
                return false;
            return a.ToString() == b.ToString();
        }

        public static bool operator !=(Base58Data a, Base58Data b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }
    }
}