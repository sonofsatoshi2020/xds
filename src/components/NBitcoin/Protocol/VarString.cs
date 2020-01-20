using System;
using System.Linq;

namespace NBitcoin.Protocol
{
    public class VarString : IBitcoinSerializable
    {
        byte[] bytes = new byte[0];

        public VarString()
        {
        }

        public VarString(byte[] bytes)
        {
            this.bytes = bytes ?? throw new ArgumentNullException("bytes");
        }

        public int Length => this.bytes.Length;

        #region IBitcoinSerializable Members

        public void ReadWrite(BitcoinStream stream)
        {
            var len = new VarInt((ulong) this.bytes.Length);
            stream.ReadWrite(ref len);
            if (!stream.Serializing)
            {
                if (len.ToLong() > (uint) stream.MaxArraySize)
                    throw new ArgumentOutOfRangeException("Array size not big");

                this.bytes = new byte[len.ToLong()];
            }

            stream.ReadWrite(ref this.bytes);
        }

        #endregion

        public byte[] GetString()
        {
            return GetString(false);
        }

        public byte[] GetString(bool @unsafe)
        {
            if (@unsafe)
                return this.bytes;

            return this.bytes.ToArray();
        }
    }
}