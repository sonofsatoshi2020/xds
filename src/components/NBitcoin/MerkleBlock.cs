using System.Collections.Generic;
using System.Linq;

namespace NBitcoin
{
    public class MerkleBlock : IBitcoinSerializable
    {
        PartialMerkleTree _PartialMerkleTree;

        // Public only for unit testing
        BlockHeader header;

        public MerkleBlock()
        {
        }

        // Create from a CBlock, filtering transactions according to filter
        // Note that this will call IsRelevantAndUpdate on the filter for each transaction,
        // thus the filter will likely be modified.
        public MerkleBlock(Block block, BloomFilter filter)
        {
            this.header = block.Header;

            var vMatch = new List<bool>();
            var vHashes = new List<uint256>();


            for (uint i = 0; i < block.Transactions.Count; i++)
            {
                var hash = block.Transactions[(int) i].GetHash();
                vMatch.Add(filter.IsRelevantAndUpdate(block.Transactions[(int) i]));
                vHashes.Add(hash);
            }

            this._PartialMerkleTree = new PartialMerkleTree(vHashes.ToArray(), vMatch.ToArray());
        }

        public MerkleBlock(Block block, uint256[] txIds)
        {
            this.header = block.Header;

            var vMatch = new List<bool>();
            var vHashes = new List<uint256>();
            for (var i = 0; i < block.Transactions.Count; i++)
            {
                var hash = block.Transactions[i].GetHash();
                vHashes.Add(hash);
                vMatch.Add(txIds.Contains(hash));
            }

            this._PartialMerkleTree = new PartialMerkleTree(vHashes.ToArray(), vMatch.ToArray());
        }

        public BlockHeader Header
        {
            get => this.header;
            set => this.header = value;
        }

        public PartialMerkleTree PartialMerkleTree
        {
            get => this._PartialMerkleTree;
            set => this._PartialMerkleTree = value;
        }

        #region IBitcoinSerializable Members

        public void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.header);
            stream.ReadWrite(ref this._PartialMerkleTree);
        }

        #endregion
    }
}