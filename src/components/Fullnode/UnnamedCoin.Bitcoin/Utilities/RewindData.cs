using System.Collections.Generic;
using System.Text;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.Utilities
{
    /// <summary>
    ///     Information about a previous state of the coinview that contains all information
    ///     needed to rewind the coinview from the current state to the previous state.
    /// </summary>
    public class RewindData : IBitcoinSerializable
    {
        /// <summary>
        ///     List of unspent output transaction information that needs to be restored when rewinding to the previous state
        ///     as they were fully spent in the current view.
        /// </summary>
        List<UnspentOutputs> outputsToRestore = new List<UnspentOutputs>();

        /// <summary>Hash of the block header of the tip of the previous state of the coinview.</summary>
        uint256 previousBlockHash;

        /// <summary>
        ///     List of transaction IDs that needs to be removed when rewinding to the previous state as they haven't existed
        ///     in the previous state.
        /// </summary>
        List<uint256> transactionsToRemove = new List<uint256>();

        /// <summary>
        ///     Creates uninitialized instance of the object.
        /// </summary>
        public RewindData()
        {
        }

        /// <summary>
        ///     Initializes instance of the object with coinview's tip hash.
        /// </summary>
        /// <param name="previousBlockHash">Hash of the block header of the tip of the previous state of the coinview.</param>
        public RewindData(uint256 previousBlockHash)
        {
            this.previousBlockHash = previousBlockHash;
        }

        /// <summary>Hash of the block header of the tip of the previous state of the coinview.</summary>
        public uint256 PreviousBlockHash
        {
            get => this.previousBlockHash;

            set => this.previousBlockHash = value;
        }

        /// <summary>
        ///     List of transaction IDs that needs to be removed when rewinding to the previous state as they haven't existed
        ///     in the previous state.
        /// </summary>
        public List<uint256> TransactionsToRemove
        {
            get => this.transactionsToRemove;

            set => this.transactionsToRemove = value;
        }

        /// <summary>
        ///     List of unspent output transaction information that needs to be restored when rewinding to the previous state
        ///     as they were fully spent in the current view.
        /// </summary>
        public List<UnspentOutputs> OutputsToRestore
        {
            get => this.outputsToRestore;

            set => this.outputsToRestore = value;
        }

        /// <inheritdoc />
        public void ReadWrite(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.previousBlockHash);
            stream.ReadWrite(ref this.transactionsToRemove);
            stream.ReadWrite(ref this.outputsToRestore);
        }

        public override string ToString()
        {
            var data = new StringBuilder();

            data.AppendLine($"{nameof(this.previousBlockHash)}={this.previousBlockHash}");
            data.AppendLine(
                $"{nameof(this.transactionsToRemove)}.{nameof(this.transactionsToRemove.Count)}={this.transactionsToRemove.Count}:");

            foreach (var txToRemove in this.transactionsToRemove)
                data.AppendLine(txToRemove.ToString());

            data.AppendLine(
                $"{nameof(this.outputsToRestore)}.{nameof(this.outputsToRestore.Count)}={this.outputsToRestore.Count}:");

            foreach (var output in this.outputsToRestore)
                data.AppendLine(output.ToString());

            return data.ToString();
        }
    }
}