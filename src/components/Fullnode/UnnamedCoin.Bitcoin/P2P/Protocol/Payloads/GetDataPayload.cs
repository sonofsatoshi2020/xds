using System.Collections.Generic;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    /// <summary>
    ///     Ask for transaction, block or merkle block.
    /// </summary>
    [Payload("getdata")]
    public class GetDataPayload : Payload
    {
        List<InventoryVector> inventory = new List<InventoryVector>();

        public GetDataPayload()
        {
        }

        public GetDataPayload(params InventoryVector[] vectors)
        {
            this.inventory.AddRange(vectors);
        }

        public List<InventoryVector> Inventory
        {
            get => this.inventory;
            set => this.inventory = value;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.inventory);
        }
    }
}