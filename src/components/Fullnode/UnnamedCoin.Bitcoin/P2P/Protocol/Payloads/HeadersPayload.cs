using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.Protocol;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    /// <summary>
    ///     Block headers received after a getheaders messages.
    /// </summary>
    [Payload("headers")]
    public class HeadersPayload : Payload
    {
        public HeadersPayload()
        {
        }

        public HeadersPayload(IEnumerable<BlockHeader> headers)
        {
            this.Headers.AddRange(headers);
        }

        public List<BlockHeader> Headers { get; } = new List<BlockHeader>();


        public override void ReadWriteCore(BitcoinStream stream)
        {
            if (stream.Serializing)
            {
                var headersOff = this.Headers.Select(h => new BlockHeaderWithTxCount(h)).ToList();
                stream.ReadWrite(ref headersOff);
            }
            else
            {
                this.Headers.Clear();
                var headersOff = new List<BlockHeaderWithTxCount>();
                stream.ReadWrite(ref headersOff);
                this.Headers.AddRange(headersOff.Select(h => h.Header));
            }
        }

        class BlockHeaderWithTxCount : IBitcoinSerializable
        {
            internal BlockHeader Header;

            public BlockHeaderWithTxCount()
            {
            }

            public BlockHeaderWithTxCount(BlockHeader header)
            {
                this.Header = header;
            }


            public void ReadWrite(BitcoinStream stream)
            {
                stream.ReadWrite(ref this.Header);
                var txCount = new VarInt(0);
                stream.ReadWrite(ref txCount);

                // Network adds an additional byte to the end of a header need to investigate why.
                if (stream.ConsensusFactory is PosConsensusFactory)
                    stream.ReadWrite(ref txCount);
            }
        }
    }
}