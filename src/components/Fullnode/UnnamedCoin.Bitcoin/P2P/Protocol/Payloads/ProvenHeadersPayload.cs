using System.Collections.Generic;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    /// <summary>
    ///     Proven headers payload which contains list of up to 2000 proven headers.
    /// </summary>
    /// <seealso cref="Payload" />
    [Payload("provhdr")]
    public class ProvenHeadersPayload : Payload
    {
        /// <summary>
        ///     <see cref="Headers" />
        /// </summary>
        List<ProvenBlockHeader> headers = new List<ProvenBlockHeader>();

        public ProvenHeadersPayload()
        {
        }

        public ProvenHeadersPayload(params ProvenBlockHeader[] headers)
        {
            this.Headers.AddRange(headers);
        }

        /// <summary>
        ///     Gets a list of up to 2,000 proven headers.
        /// </summary>
        public List<ProvenBlockHeader> Headers => this.headers;

        /// <inheritdoc />
        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.headers);
        }
    }
}