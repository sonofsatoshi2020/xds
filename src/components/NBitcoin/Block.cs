using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NBitcoin.DataEncoders;
using NBitcoin.Formatters;
using Newtonsoft.Json.Linq;

namespace NBitcoin
{
    public class Block : IBitcoinSerializable
    {
        public const uint MaxBlockSize = 1000 * 1000;

        BlockHeader header;

        // network and disk
        List<Transaction> transactions = new List<Transaction>();

        [Obsolete("Should use Block.Load outside of ConsensusFactories")]
        public Block()
        {
            this.header = new BlockHeader();
            this.transactions.Clear();
            this.BlockSize = null;
        }

        [Obsolete("Should use Block.Load outside of ConsensusFactories")]
        public Block(BlockHeader blockHeader) : this()
        {
            this.header = blockHeader;
        }

        /// <summary>
        ///     The size of the block in bytes, the block must be serialized for this property to be set.
        ///     This property will be set only once on the first serialization(or deserialization).
        /// </summary>
        public long? BlockSize { get; protected set; }

        public List<Transaction> Transactions
        {
            get => this.transactions;
            set => this.transactions = value;
        }

        public bool HeaderOnly => this.transactions == null || this.transactions.Count == 0;

        public BlockHeader Header => this.header;

        public virtual void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.header);
            stream.ReadWrite(ref this.transactions);

            if (this.BlockSize == null)
                this.BlockSize = stream.Serializing ? stream.Counter.WrittenBytes : stream.Counter.ReadBytes;
        }

        public MerkleNode GetMerkleRoot()
        {
            return MerkleNode.GetRoot(this.Transactions.Select(t => t.GetHash()));
        }

        /// <summary>
        ///     A block's hash is it's header's hash.
        /// </summary>
        public uint256 GetHash()
        {
            return this.header.GetHash();
        }

        public Transaction AddTransaction(Transaction tx)
        {
            this.Transactions.Add(tx);
            return tx;
        }

        /// <summary>
        ///     Create a block with the specified option only. (useful for stripping data from a block).
        /// </summary>
        /// <param name="consensusFactory">The network consensus factory.</param>
        /// <param name="options">Options to keep.</param>
        /// <returns>A new block with only the options wanted.</returns>
        public Block WithOptions(ConsensusFactory consensusFactory, TransactionOptions options)
        {
            if (this.Transactions.Count == 0)
                return this;

            if (options == TransactionOptions.Witness && this.Transactions[0].HasWitness)
                return this;

            if (options == TransactionOptions.None && !this.Transactions[0].HasWitness)
                return this;

            var instance = consensusFactory.CreateBlock();
            using (var ms = new MemoryStream())
            {
                var bms = new BitcoinStream(ms, true)
                {
                    TransactionOptions = options,
                    ConsensusFactory = consensusFactory
                };

                ReadWrite(bms);
                ms.Position = 0;
                bms = new BitcoinStream(ms, false)
                {
                    TransactionOptions = options,
                    ConsensusFactory = consensusFactory
                };

                instance.ReadWrite(bms);
            }

            return instance;
        }

        public void UpdateMerkleRoot()
        {
            this.Header.HashMerkleRoot = GetMerkleRoot().Hash;
        }

        public bool CheckProofOfWork()
        {
            return this.Header.CheckProofOfWork();
        }

        public bool CheckMerkleRoot()
        {
            return this.Header.HashMerkleRoot == GetMerkleRoot().Hash;
        }

        public static Block ParseJson(Network network, string json)
        {
            var formatter = new BlockExplorerFormatter(network);
            var block = JObject.Parse(json);
            var txs = (JArray) block["tx"];

            var blk = network.Consensus.ConsensusFactory.CreateBlock();
            blk.Header.Bits = new Target((uint) block["bits"]);
            blk.Header.BlockTime = Utils.UnixTimeToDateTime((uint) block["time"]);
            blk.Header.Nonce = (uint) block["nonce"];
            blk.Header.Version = (int) block["ver"];
            blk.Header.HashPrevBlock = uint256.Parse((string) block["prev_block"]);
            blk.Header.HashMerkleRoot = uint256.Parse((string) block["mrkl_root"]);

            foreach (var tx in txs) blk.AddTransaction(formatter.Parse((JObject) tx));

            return blk;
        }

        public static Block Parse(string hex, ConsensusFactory consensusFactory)
        {
            if (string.IsNullOrEmpty(hex))
                throw new ArgumentNullException(nameof(hex));

            if (consensusFactory == null)
                throw new ArgumentNullException(nameof(consensusFactory));

            var block = consensusFactory.CreateBlock();
            block.ReadWrite(Encoders.Hex.DecodeData(hex), consensusFactory);

            return block;
        }

        public static Block Load(byte[] bytes, ConsensusFactory consensusFactory)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (consensusFactory == null)
                throw new ArgumentNullException(nameof(consensusFactory));

            var block = consensusFactory.CreateBlock();
            block.ReadWrite(bytes, consensusFactory);

            return block;
        }

        public MerkleBlock Filter(params uint256[] txIds)
        {
            return new MerkleBlock(this, txIds);
        }

        public MerkleBlock Filter(BloomFilter filter)
        {
            return new MerkleBlock(this, filter);
        }
    }
}