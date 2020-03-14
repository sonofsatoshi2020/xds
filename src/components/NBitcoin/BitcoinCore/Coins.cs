using System.Collections.Generic;
using System.Linq;

namespace NBitcoin.BitcoinCore
{
    public class Coins : IBitcoinSerializable
    {
        public static readonly TxOut NullTxOut = new TxOut(new Money(-1), Script.Empty);
        private bool fCoinStake;
        private uint nHeight;
        private uint nTime;
        private uint nVersion;

        public Coins()
        {
        }

        public Coins(Transaction tx, int height)
        {
            if (tx is IPosTransactionWithTime posTx)
            {
                this.fCoinStake = tx.IsCoinStake;
                this.nTime = posTx.Time;
            }

            this.CoinBase = tx.IsCoinBase;
            this.Outputs = tx.Outputs.ToList();
            this.nVersion = tx.Version;
            this.nHeight = (uint)height;

            ClearUnspendable();
            UpdateValue();
        }

        // Determines whether transaction is a coinbase.
        public bool CoinBase { get; set; }

        public bool CoinStake
        {
            get => this.fCoinStake;
            set => this.fCoinStake = value;
        }

        public uint Time
        {
            get => this.nTime;
            set => this.nTime = value;
        }

        // Specifies at which height this transaction was included in the active block chain
        public uint Height
        {
            get => this.nHeight;
            set => this.nHeight = value;
        }

        // Version of the CTransaction; accesses to this value should probably check for nHeight as well,
        // as new tx version will probably only be introduced at certain heights        .
        public uint Version
        {
            get => this.nVersion;
            set => this.nVersion = value;
        }

        // Lists unspent transaction outputs; spent outputs are .IsNull(); spent outputs at the end of the array are dropped.
        public List<TxOut> Outputs { get; private set; } = new List<TxOut>();

        public Money Value { get; private set; }
        public bool IsEmpty => this.Outputs.Count == 0;

        public int UnspentCount => this.Outputs.Count(c => !IsNull(c));

        private void UpdateValue()
        {
            this.Value = this.Outputs
                .Where(o => !IsNull(o))
                .Sum(o => o.Value);
        }

        private bool IsNull(TxOut o)
        {
            return o.Value.Satoshi == -1;
        }

        /// <summary>
        ///     Remove the last items that are <see cref="IsNull" />, this method may reduce the size of the collection.
        /// </summary>
        private void Cleanup()
        {
            var count = this.Outputs.Count;

            // Remove spent outputs at the end of vout.
            for (var i = count - 1; i >= 0; i--)
                if (IsNull(this.Outputs[i]))
                    this.Outputs.RemoveAt(i);
                else
                    break;
        }

        public void ClearUnspendable()
        {
            for (var i = 0; i < this.Outputs.Count; i++)
            {
                var o = this.Outputs[i];
                if (o.ScriptPubKey.IsUnspendable)
                    this.Outputs[i] = NullTxOut;
            }

            // Remove empty outputs form the end of the collection.
            Cleanup();
        }

        public void MergeFrom(Coins otherCoin)
        {
            var diff = otherCoin.Outputs.Count - this.Outputs.Count;
            if (diff > 0)
                for (var i = 0; i < diff; i++)
                    this.Outputs.Add(NullTxOut);

            for (var i = 0; i < otherCoin.Outputs.Count; i++)
                this.Outputs[i] = otherCoin.Outputs[i];

            UpdateValue();
        }

        #region IBitcoinSerializable Members

        public void ReadWrite(BitcoinStream stream)
        {
            if (stream.Serializing)
            {
                uint nMaskSize = 0, nMaskCode = 0;
                CalcMaskSize(ref nMaskSize, ref nMaskCode);

                var fFirst = this.Outputs.Count > 0 && !IsNull(this.Outputs[0]);
                var fSecond = this.Outputs.Count > 1 && !IsNull(this.Outputs[1]);
                var nCode = unchecked((uint)(8 * (nMaskCode - (fFirst || fSecond ? 0 : 1)) + (this.CoinBase ? 1 : 0) +
                                              (fFirst ? 2 : 0) + (fSecond ? 4 : 0)));

                // version
                stream.ReadWriteAsVarInt(ref this.nVersion);

                // size of header code
                stream.ReadWriteAsVarInt(ref nCode);

                // spentness bitmask
                for (uint b = 0; b < nMaskSize; b++)
                {
                    byte chAvail = 0;
                    for (uint i = 0; i < 8 && 2 + b * 8 + i < this.Outputs.Count; i++)
                        if (!IsNull(this.Outputs[2 + (int)b * 8 + (int)i]))
                            chAvail |= (byte)(1 << (int)i);

                    stream.ReadWrite(ref chAvail);
                }

                // txouts themself
                for (uint i = 0; i < this.Outputs.Count; i++)
                    if (!IsNull(this.Outputs[(int)i]))
                    {
                        var compressedTx = new TxOutCompressor(this.Outputs[(int)i]);
                        stream.ReadWrite(ref compressedTx);
                    }

                // coinbase height
                stream.ReadWriteAsVarInt(ref this.nHeight);

                // This is an ugly hack that will go away when refactoring coinview
                if (stream.ConsensusFactory.CreateTransaction() is IPosTransactionWithTime)
                {
                    stream.ReadWrite(ref this.fCoinStake);
                    stream.ReadWrite(ref this.nTime);
                }
                else
                {
                    stream.ReadWrite(ref this.fCoinStake);
                }
            }
            else
            {
                uint nCode = 0;

                // version
                stream.ReadWriteAsVarInt(ref this.nVersion);

                //// header code
                stream.ReadWriteAsVarInt(ref nCode);
                this.CoinBase = (nCode & 1) != 0;

                var vAvail = new List<bool> { false, false };
                vAvail[0] = (nCode & 2) != 0;
                vAvail[1] = (nCode & 4) != 0;

                var nMaskCode = unchecked((uint)(nCode / 8 + ((nCode & 6) != 0 ? 0 : 1)));

                //// spentness bitmask
                while (nMaskCode > 0)
                {
                    byte chAvail = 0;

                    stream.ReadWrite(ref chAvail);

                    for (uint p = 0; p < 8; p++)
                    {
                        var f = (chAvail & (1 << (int)p)) != 0;
                        vAvail.Add(f);
                    }

                    if (chAvail != 0)
                        nMaskCode--;
                }

                // txouts themself
                this.Outputs = Enumerable.Range(0, vAvail.Count).Select(_ => NullTxOut).ToList();
                for (uint i = 0; i < vAvail.Count; i++)
                    if (vAvail[(int)i])
                    {
                        var compressed = new TxOutCompressor();
                        stream.ReadWrite(ref compressed);
                        this.Outputs[(int)i] = compressed.TxOut;
                    }

                //// coinbase height
                stream.ReadWriteAsVarInt(ref this.nHeight);

                // This is an ugly hack that will go away when refactoring coinview
                if (stream.ConsensusFactory.CreateTransaction() is IPosTransactionWithTime)
                {
                    stream.ReadWrite(ref this.fCoinStake);
                    stream.ReadWrite(ref this.nTime);
                }
                else
                {
                    stream.ReadWrite(ref this.fCoinStake);
                }

                Cleanup();
                UpdateValue();
            }
        }

        // calculate number of bytes for the bitmask, and its number of non-zero bytes
        // each bit in the bitmask represents the availability of one output, but the
        // availabilities of the first two outputs are encoded separately
        private void CalcMaskSize(ref uint nBytes, ref uint nNonzeroBytes)
        {
            uint nLastUsedByte = 0;

            for (uint b = 0; 2 + b * 8 < this.Outputs.Count; b++)
            {
                var fZero = true;
                for (uint i = 0; i < 8 && 2 + b * 8 + i < this.Outputs.Count; i++)
                    if (!IsNull(this.Outputs[2 + (int)b * 8 + (int)i]))
                    {
                        fZero = false;
                    }

                if (!fZero)
                {
                    nLastUsedByte = b + 1;
                    nNonzeroBytes++;
                }
            }

            nBytes += nLastUsedByte;
        }

        // check whether a particular output is still available
        public bool IsAvailable(uint position)
        {
            return position <= int.MaxValue && position < this.Outputs.Count && !IsNull(this.Outputs[(int)position]);
        }

        public TxOut TryGetOutput(uint position)
        {
            if (!IsAvailable(position))
                return null;

            return this.Outputs[(int)position];
        }

        // check whether the entire CCoins is spent
        // note that only !IsPruned() CCoins can be serialized
        public bool IsPruned => this.IsEmpty || this.Outputs.All(v => IsNull(v));

        #endregion IBitcoinSerializable Members
    }
}