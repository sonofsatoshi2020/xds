using System;
using System.IO;
using NBitcoin.BouncyCastle.crypto.digests;

namespace NBitcoin.Crypto
{
    public class HashStream : Stream
    {
        public override bool CanRead => throw new NotImplementedException();

        public override bool CanSeek => throw new NotImplementedException();

        public override bool CanWrite => throw new NotImplementedException();

        public override long Length => throw new NotImplementedException();

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var copied = 0;
            var toCopy = 0;
            while (copied != count)
            {
                toCopy = Math.Min(this._Buffer.Length - this._Pos, count - copied);
                Buffer.BlockCopy(buffer, offset + copied, this._Buffer, this._Pos, toCopy);
                copied += (byte) toCopy;
                this._Pos += (byte) toCopy;
                ProcessBlockIfNeeded();
            }
        }


        readonly byte[] _Buffer = new byte[32];
        byte _Pos;

        public override void WriteByte(byte value)
        {
            this._Buffer[this._Pos++] = value;
            ProcessBlockIfNeeded();
        }

        void ProcessBlockIfNeeded()
        {
            if (this._Pos == this._Buffer.Length)
                ProcessBlock();
        }

#if NETCORE
        readonly Sha256Digest sha = new Sha256Digest();

        void ProcessBlock()
        {
            this.sha.BlockUpdate(this._Buffer, 0, this._Pos);
            this._Pos = 0;
        }

        public uint256 GetHash()
        {
            ProcessBlock();
            this.sha.DoFinal(this._Buffer, 0);
            this._Pos = 32;
            ProcessBlock();
            this.sha.DoFinal(this._Buffer, 0);
            return new uint256(this._Buffer);
        }

#else
        System.Security.Cryptography.SHA256Managed sha = new System.Security.Cryptography.SHA256Managed();
        private void ProcessBlock()
        {
            sha.TransformBlock(_Buffer, 0, _Pos, _Buffer, 0);
            _Pos = 0;
        }

        static readonly byte[] Empty = new byte[0];
        public uint256 GetHash()
        {
            ProcessBlock();
            sha.TransformFinalBlock(Empty, 0, 0);
            var hash1 = sha.Hash;
            Buffer.BlockCopy(sha.Hash, 0, _Buffer, 0, 32);
            sha.Initialize();
            sha.TransformFinalBlock(_Buffer, 0, 32);
            var hash2 = sha.Hash;
            return new uint256(hash2);
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing)
                sha.Dispose();
            base.Dispose(disposing);
        }
#endif
    }
}