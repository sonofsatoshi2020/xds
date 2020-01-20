using System;
using System.Net;

namespace UnnamedCoin.Bitcoin.Consensus
{
    public class ConsensusException : Exception
    {
        protected ConsensusException()
        {
        }

        public ConsensusException(string messsage) : base(messsage)
        {
        }
    }

    public class MaxReorgViolationException : ConsensusException
    {
    }

    public class ConnectHeaderException : ConsensusException
    {
    }

    /// <summary>
    ///     This throws when the header of a previously block that failed
    ///     partial or full validation and was marked as invalid is passed to the node.
    /// </summary>
    public class HeaderInvalidException : ConsensusException
    {
    }

    /// <summary>
    ///     An exception that is contains exception coming from the <see cref="IConsensusRuleEngine" /> execution.
    /// </summary>
    public class ConsensusRuleException : ConsensusException
    {
        public ConsensusRuleException(ConsensusError consensusError) : base(consensusError.ToString())
        {
            this.ConsensusError = consensusError;
        }

        public ConsensusError ConsensusError { get; }
    }

    public class CheckpointMismatchException : ConsensusException
    {
    }

    public class BlockDownloadedForMissingChainedHeaderException : ConsensusException
    {
    }

    public class IntegrityValidationFailedException : ConsensusException
    {
        public IntegrityValidationFailedException(IPEndPoint peer, ConsensusError error, int banDurationSeconds)
        {
            this.PeerEndPoint = peer;
            this.Error = error;
            this.BanDurationSeconds = banDurationSeconds;
        }

        /// <summary>The peer this block came from.</summary>
        public IPEndPoint PeerEndPoint { get; }

        /// <summary>Consensus error.</summary>
        public ConsensusError Error { get; }

        /// <summary>Time for which peer should be banned.</summary>
        public int BanDurationSeconds { get; }
    }
}