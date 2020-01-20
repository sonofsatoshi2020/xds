using System;
using System.IO;
using NBitcoin.BouncyCastle.util.io;

namespace NBitcoin.BouncyCastle.asn1
{
    class DefiniteLengthInputStream
        : LimitedInputStream
    {
        static readonly byte[] EmptyBytes = new byte[0];

        readonly int _originalLength;

        internal DefiniteLengthInputStream(
            Stream inStream,
            int length)
            : base(inStream, length)
        {
            if (length < 0)
                throw new ArgumentException("negative lengths not allowed", "length");

            this._originalLength = length;
            this.Remaining = length;

            if (length == 0) SetParentEofDetect(true);
        }

        internal int Remaining { get; set; }

        public override int ReadByte()
        {
            if (this.Remaining == 0)
                return -1;

            var b = this._in.ReadByte();

            if (b < 0)
                throw new EndOfStreamException("DEF length " + this._originalLength + " object truncated by " +
                                               this.Remaining);

            if (--this.Remaining == 0) SetParentEofDetect(true);

            return b;
        }

        public override int Read(
            byte[] buf,
            int off,
            int len)
        {
            if (this.Remaining == 0)
                return 0;

            var toRead = System.Math.Min(len, this.Remaining);
            var numRead = this._in.Read(buf, off, toRead);

            if (numRead < 1)
                throw new EndOfStreamException("DEF length " + this._originalLength + " object truncated by " +
                                               this.Remaining);

            if ((this.Remaining -= numRead) == 0) SetParentEofDetect(true);

            return numRead;
        }

        internal void ReadAllIntoByteArray(byte[] buf)
        {
            if (this.Remaining != buf.Length)
                throw new ArgumentException("buffer length not right for data");

            if ((this.Remaining -= Streams.ReadFully(this._in, buf)) != 0)
                throw new EndOfStreamException("DEF length " + this._originalLength + " object truncated by " +
                                               this.Remaining);
            SetParentEofDetect(true);
        }

        internal byte[] ToArray()
        {
            if (this.Remaining == 0)
                return EmptyBytes;

            var bytes = new byte[this.Remaining];
            if ((this.Remaining -= Streams.ReadFully(this._in, bytes)) != 0)
                throw new EndOfStreamException("DEF length " + this._originalLength + " object truncated by " +
                                               this.Remaining);
            SetParentEofDetect(true);
            return bytes;
        }
    }
}