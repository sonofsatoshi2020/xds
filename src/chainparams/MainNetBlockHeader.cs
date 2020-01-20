using System.IO;
using NBitcoin;
using NBitcoin.Crypto;

namespace ChainParams
{
    public class MainNetBlockHeader : PosBlockHeader
    {
	    public override uint256 GetPoWHash()
        {
            byte[] serialized;

            using (var ms = new MemoryStream())
            {
                this.ReadWriteHashingStream(new BitcoinStream(ms, true));
                serialized = ms.ToArray();
            }

		    return Sha512T.GetHash(serialized);
	    }
    }

    public class MainNetProvenBlockHeader : ProvenBlockHeader
    {
        public MainNetProvenBlockHeader()
        {
        }

        public MainNetProvenBlockHeader(PosBlock block) : base(block)
        {
        }

        public override uint256 GetPoWHash()
        {
            byte[] serialized;

            using (var ms = new MemoryStream())
            {
                this.ReadWriteHashingStream(new BitcoinStream(ms, true));
                serialized = ms.ToArray();
            }

            return Sha512T.GetHash(serialized);
        }
    }
}
