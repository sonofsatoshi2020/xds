using NBitcoin;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;

namespace UnnamedCoin.Bitcoin.P2P.Protocol
{
    public enum InventoryType : uint
    {
        Error = 0,
        MSG_TX = 1,
        MSG_BLOCK = 2,

        // Nodes may always request a MSG_FILTERED_BLOCK/MSG_CMPCT_BLOCK in a getdata, however,
        // MSG_FILTERED_BLOCK/MSG_CMPCT_BLOCK should not appear in any invs except as a part of getdata.
        MSG_FILTERED_BLOCK = 3,
        MSG_CMPCT_BLOCK,

        // The following can only occur in getdata. Invs always use TX or BLOCK.
        MSG_TYPE_MASK = 0xffffffff >> 2,
        MSG_WITNESS_FLAG = 1 << 30,
        MSG_WITNESS_BLOCK = MSG_BLOCK | MSG_WITNESS_FLAG,
        MSG_WITNESS_TX = MSG_TX | MSG_WITNESS_FLAG,
        MSG_FILTERED_WITNESS_BLOCK = MSG_FILTERED_BLOCK | MSG_WITNESS_FLAG
    }

    public class InventoryVector : Payload, IBitcoinSerializable
    {
        uint256 hash = uint256.Zero;
        uint type;

        public InventoryVector()
        {
        }

        public InventoryVector(InventoryType type, uint256 hash)
        {
            this.Type = type;
            this.Hash = hash;
        }

        public InventoryType Type
        {
            get => (InventoryType) this.type;

            set => this.type = (uint) value;
        }

        public uint256 Hash
        {
            get => this.hash;
            set => this.hash = value;
        }


        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.type);
            stream.ReadWrite(ref this.hash);
        }

        public override string ToString()
        {
            return this.Type.ToString();
        }
    }
}