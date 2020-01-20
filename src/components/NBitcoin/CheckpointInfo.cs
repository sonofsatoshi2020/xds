namespace NBitcoin
{
    /// <summary>
    ///     Description of checkpointed block.
    /// </summary>
    public class CheckpointInfo
    {
        /// <summary>
        ///     Initializes a new instance of the object.
        /// </summary>
        /// <param name="hash">Hash of the checkpointed block header.</param>
        /// <param name="stakeModifierV2">Stake modifier V2 value of the checkpointed block.</param>
        public CheckpointInfo(uint256 hash, uint256 stakeModifierV2 = null)
        {
            this.Hash = hash;
            this.StakeModifierV2 = stakeModifierV2;
        }

        /// <summary>Hash of the checkpointed block header.</summary>
        public uint256 Hash { get; }

        /// <summary>Stake modifier V2 value of the checkpointed block.</summary>
        public uint256 StakeModifierV2 { get; }
    }
}