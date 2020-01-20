using System.Collections;

namespace NBitcoin.Protocol
{
    public class UTxOutputs : IBitcoinSerializable
    {
        VarString bitmap;
        int chainHeight;

        uint256 chainTipHash;

        UTxOut[] outputs;

        public int ChainHeight
        {
            get => this.chainHeight;
            internal set => this.chainHeight = value;
        }

        public uint256 ChainTipHash
        {
            get => this.chainTipHash;
            internal set => this.chainTipHash = value;
        }

        public BitArray Bitmap
        {
            get => new BitArray(this.bitmap.ToBytes());
            internal set
            {
                var bits = value;
                var buffer = new BitReader(bits).ToWriter().ToBytes();
                this.bitmap = new VarString(buffer);
            }
        }

        public UTxOut[] Outputs
        {
            get => this.outputs;
            internal set => this.outputs = value;
        }

        public void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.chainHeight);
            stream.ReadWrite(ref this.chainTipHash);
            stream.ReadWrite(ref this.bitmap);
            stream.ReadWrite(ref this.outputs);
        }
    }

    public class UTxOut : IBitcoinSerializable
    {
        uint height;

        TxOut txOut;
        uint version;

        public uint Version
        {
            get => this.version;
            internal set => this.version = value;
        }

        public uint Height
        {
            get => this.height;
            internal set => this.height = value;
        }

        public TxOut Output
        {
            get => this.txOut;
            internal set => this.txOut = value;
        }

        public void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.version);
            stream.ReadWrite(ref this.height);
            stream.ReadWrite(ref this.txOut);
        }
    }
}