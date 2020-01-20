using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Base;
using UnnamedCoin.Bitcoin.Consensus;
using UnnamedCoin.Bitcoin.Interfaces;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.BlockStore
{
    /// <inheritdoc />
    public class ProvenHeadersBlockStoreBehavior : BlockStoreBehavior
    {
        readonly ICheckpoints checkpoints;
        readonly Network network;

        public ProvenHeadersBlockStoreBehavior(Network network, ChainIndexer chainIndexer, IChainState chainState,
            ILoggerFactory loggerFactory, IConsensusManager consensusManager, ICheckpoints checkpoints,
            IBlockStoreQueue blockStoreQueue)
            : base(chainIndexer, chainState, loggerFactory, consensusManager, blockStoreQueue)
        {
            this.network = Guard.NotNull(network, nameof(network));
            this.checkpoints = Guard.NotNull(checkpoints, nameof(checkpoints));
        }

        /// <inheritdoc />
        /// <returns>
        ///     The <see cref="HeadersPayload" /> instance to announce to the peer, or <see cref="ProvenHeadersPayload" /> if
        ///     the peers requires it.
        /// </returns>
        protected override Payload BuildHeadersAnnouncePayload(IEnumerable<BlockHeader> headers)
        {
            // Sanity check. That should never happen.
            if (!headers.All(x => x is ProvenBlockHeader))
                throw new BlockStoreException("UnexpectedError: BlockHeader is expected to be a ProvenBlockHeader");

            var provenHeadersPayload = new ProvenHeadersPayload(headers.Cast<ProvenBlockHeader>().ToArray());

            return provenHeadersPayload;
        }


        public override object Clone()
        {
            var res = new ProvenHeadersBlockStoreBehavior(this.network, this.ChainIndexer, this.chainState,
                this.loggerFactory, this.consensusManager, this.checkpoints, this.blockStoreQueue)
            {
                CanRespondToGetBlocksPayload = this.CanRespondToGetBlocksPayload,
                CanRespondToGetDataPayload = this.CanRespondToGetDataPayload
            };

            return res;
        }
    }
}