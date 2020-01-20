using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin.BIP39;

namespace NBitcoin
{
    class BitReader
    {
        readonly BitArray array;

        public BitReader(byte[] data, int bitCount)
        {
            var writer = new BitWriter();
            writer.Write(data, bitCount);
            this.array = writer.ToBitArray();
        }

        public BitReader(BitArray array)
        {
            this.array = new BitArray(array.Length);
            for (var i = 0; i < array.Length; i++)
                this.array.Set(i, array.Get(i));
        }

        public int Position { get; set; }

        public int Count => this.array.Length;

        public bool Read()
        {
            var v = this.array.Get(this.Position);
            this.Position++;
            return v;
        }

        public uint ReadUInt(int bitCount)
        {
            uint value = 0;
            for (var i = 0; i < bitCount; i++)
            {
                var v = Read() ? 1U : 0U;
                value += v << i;
            }

            return value;
        }

        public BitArray ToBitArray()
        {
            var result = new BitArray(this.array.Length);
            for (var i = 0; i < this.array.Length; i++)
                result.Set(i, this.array.Get(i));
            return result;
        }

        public BitWriter ToWriter()
        {
            var writer = new BitWriter();
            writer.Write(this.array);
            return writer;
        }

        public void Consume(int count)
        {
            this.Position += count;
        }

        public bool Same(BitReader b)
        {
            while (this.Position != this.Count && b.Position != b.Count)
            {
                var valuea = Read();
                var valueb = b.Read();
                if (valuea != valueb)
                    return false;
            }

            return true;
        }

        public override string ToString()
        {
            var builder = new StringBuilder(this.array.Length);
            for (var i = 0; i < this.Count; i++)
            {
                if (i != 0 && i % 8 == 0)
                    builder.Append(' ');
                builder.Append(this.array.Get(i) ? "1" : "0");
            }

            return builder.ToString();
        }
    }

    class BitWriter
    {
        readonly List<bool> values = new List<bool>();

        public int Count => this.values.Count;

        public int Position { get; set; }

        public void Write(bool value)
        {
            this.values.Insert(this.Position, value);
            this.Position++;
        }

        internal void Write(byte[] bytes)
        {
            Write(bytes, bytes.Length * 8);
        }

        public void Write(byte[] bytes, int bitCount)
        {
            bytes = SwapEndianBytes(bytes);
            var array = new BitArray(bytes);
            this.values.InsertRange(this.Position, array.OfType<bool>().Take(bitCount));
            this.Position += bitCount;
        }

        public byte[] ToBytes()
        {
            var array = ToBitArray();
            var bytes = ToByteArray(array);
            bytes = SwapEndianBytes(bytes);
            return bytes;
        }

        //BitArray.CopyTo do not exist in portable lib
        static byte[] ToByteArray(BitArray bits)
        {
            var arrayLength = bits.Length / 8;
            if (bits.Length % 8 != 0)
                arrayLength++;
            var array = new byte[arrayLength];

            for (var i = 0; i < bits.Length; i++)
            {
                var b = i / 8;
                var offset = i % 8;
                array[b] |= bits.Get(i) ? (byte) (1 << offset) : (byte) 0;
            }

            return array;
        }


        public BitArray ToBitArray()
        {
            return new BitArray(this.values.ToArray());
        }

        public int[] ToIntegers()
        {
            var array = new BitArray(this.values.ToArray());
            return Wordlist.ToIntegers(array);
        }


        static byte[] SwapEndianBytes(byte[] bytes)
        {
            var output = new byte[bytes.Length];
            for (var i = 0; i < output.Length; i++)
            {
                byte newByte = 0;
                for (var ib = 0; ib < 8; ib++) newByte += (byte) (((bytes[i] >> ib) & 1) << (7 - ib));
                output[i] = newByte;
            }

            return output;
        }


        public void Write(uint value, int bitCount)
        {
            for (var i = 0; i < bitCount; i++)
            {
                Write((value & 1) == 1);
                value = value >> 1;
            }
        }

        internal void Write(BitReader reader, int bitCount)
        {
            for (var i = 0; i < bitCount; i++) Write(reader.Read());
        }

        public void Write(BitArray bitArray)
        {
            Write(bitArray, bitArray.Length);
        }

        public void Write(BitArray bitArray, int bitCount)
        {
            for (var i = 0; i < bitCount; i++) Write(bitArray.Get(i));
        }

        public void Write(BitReader reader)
        {
            Write(reader, reader.Count - reader.Position);
        }

        public BitReader ToReader()
        {
            return new BitReader(ToBitArray());
        }

        public override string ToString()
        {
            var builder = new StringBuilder(this.values.Count);
            for (var i = 0; i < this.Count; i++)
            {
                if (i != 0 && i % 8 == 0)
                    builder.Append(' ');
                builder.Append(this.values[i] ? "1" : "0");
            }

            return builder.ToString();
        }
    }
}