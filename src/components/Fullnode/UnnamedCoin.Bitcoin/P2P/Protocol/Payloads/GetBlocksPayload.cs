using NBitcoin;
using NBitcoin.Protocol;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Payloads
{
    /// <summary>
    ///     Ask for the block hashes (inv) that happened since BlockLocator.
    /// </summary>
    [Payload("getblocks")]
    public class GetBlocksPayload : Payload
    {
        BlockLocator blockLocators;

        uint256 hashStop = uint256.Zero;
        uint version = (uint) ProtocolVersion.PROTOCOL_VERSION;

        public GetBlocksPayload()
        {
        }

        public GetBlocksPayload(BlockLocator locator)
        {
            this.BlockLocators = locator;
        }

        public ProtocolVersion Version
        {
            get => (ProtocolVersion) this.version;

            set => this.version = (uint) value;
        }

        public BlockLocator BlockLocators
        {
            get => this.blockLocators;

            set => this.blockLocators = value;
        }

        public uint256 HashStop
        {
            get => this.hashStop;
            set => this.hashStop = value;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.version);
            stream.ReadWrite(ref this.blockLocators);
            stream.ReadWrite(ref this.hashStop);
        }
    }
}