using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    /// <summary>
    ///     Announce the hash of a transaction or block.
    /// </summary>
    [Payload("inv")]
    public class InvPayload : Payload, IEnumerable<InventoryVector>
    {
        /// <summary>Maximal number of inventory items in response to "getblocks" message.</summary>
        public const int MaxGetBlocksInventorySize = 500;

        public const int MaxInventorySize = 50000;

        List<InventoryVector> inventory = new List<InventoryVector>();

        public InvPayload()
        {
        }

        public InvPayload(params Transaction[] transactions)
            : this(transactions.Select(tx => new InventoryVector(InventoryType.MSG_TX, tx.GetHash())).ToArray())
        {
        }

        public InvPayload(params Block[] blocks)
            : this(blocks.Select(b => new InventoryVector(InventoryType.MSG_BLOCK, b.GetHash())).ToArray())
        {
        }

        public InvPayload(params InventoryVector[] invs)
        {
            this.inventory.AddRange(invs);
        }

        public List<InventoryVector> Inventory => this.inventory;

        public IEnumerator<InventoryVector> GetEnumerator()
        {
            return this.Inventory.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            var old = stream.MaxArraySize;
            stream.MaxArraySize = MaxInventorySize;
            stream.ReadWrite(ref this.inventory);
            stream.MaxArraySize = old;
        }


        public override string ToString()
        {
            return $"Count: {this.Inventory.Count}";
        }
    }
}