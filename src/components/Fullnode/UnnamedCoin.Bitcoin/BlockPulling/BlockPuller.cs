using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Base;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.BlockPulling
{
    /// <summary>
    ///     Thread-safe block puller which allows downloading blocks from all chains that the node is aware of.
    /// </summary>
    /// <remarks>
    ///     It implements relative quality scoring for peers that are used for delivering requested blocks.
    ///     <para>
    ///         If peer that was assigned an important download fails to deliver in maximum allowed time, all his assignments
    ///         will be reassigned.
    ///         Reassigned downloads are processed with high priority comparing to regular requests.
    ///         Blocks that are close to the node's consensus tip or behind it are considered to be important.
    ///     </para>
    ///     <para>
    ///         Maximum amount of blocks that can be simultaneously downloaded depends on total speed of all peers that are
    ///         capable of delivering blocks.
    ///     </para>
    ///     <para>
    ///         We never wait for the same block to be delivered from more than 1 peer at once, so in case peer was removed
    ///         from the assignment
    ///         and delivered after that we will discard delivered block from this peer.
    ///     </para>
    /// </remarks>
    public interface IBlockPuller : IDisposable
    {
        void Initialize(BlockPuller.OnBlockDownloadedCallback callback);

        /// <summary>
        ///     Adds required services to list of services that are required from all peers.
        /// </summary>
        /// <remarks>
        ///     In case some of the peers that we are already requesting block from don't support new
        ///     service requirements those peers will be released from their assignments.
        /// </remarks>
        void RequestPeerServices(NetworkPeerServices services);

        /// <summary>Gets the average size of a block based on sizes of blocks that were previously downloaded.</summary>
        double GetAverageBlockSizeBytes();

        /// <summary>Updates puller behaviors when IDB state is changed.</summary>
        /// <remarks>Should be called when IBD state was changed or first calculated.</remarks>
        void OnIbdStateChanged(bool isIbd);

        /// <summary>Updates puller's view of peer's tip.</summary>
        /// <remarks>Should be called when a peer claims a new tip.</remarks>
        /// <param name="peer">The peer.</param>
        /// <param name="newTip">New tip.</param>
        void NewPeerTipClaimed(INetworkPeer peer, ChainedHeader newTip);

        /// <summary>Removes information about the peer from the inner structures.</summary>
        /// <remarks>Adds download jobs that were assigned to this peer to reassign queue.</remarks>
        /// <param name="peerId">Unique peer identifier.</param>
        void PeerDisconnected(int peerId);

        /// <summary>Requests the blocks for download.</summary>
        /// <remarks>Doesn't support asking for the same hash twice before getting a response.</remarks>
        /// <param name="headers">Collection of consecutive headers (but gaps are ok: a1=a2=a3=a4=a8=a9).</param>
        /// <param name="highPriority">
        ///     If <c>true</c> headers will be assigned to peers before the headers that were asked
        ///     normally.
        /// </param>
        void RequestBlocksDownload(List<ChainedHeader> headers, bool highPriority = false);

        /// <summary>Removes assignments for the block which has been delivered by the peer assigned to it and calls the callback.</summary>
        /// <remarks>
        ///     This method is called for all blocks that were delivered. It is possible that block that wasn't requested
        ///     from that peer or from any peer at all is delivered, in that case the block will be ignored.
        ///     It is possible that block was reassigned from a peer who delivered it later, in that case it will be ignored from
        ///     this peer.
        /// </remarks>
        /// <param name="blockHash">The block hash.</param>
        /// <param name="block">The block.</param>
        /// <param name="peerId">ID of a peer that delivered a block.</param>
        void PushBlock(uint256 blockHash, Block block, int peerId);
    }

    public class BlockPuller : IBlockPuller
    {
        /// <param name="blockHash">Hash of the delivered block.</param>
        /// <param name="block">The block.</param>
        /// <param name="peerId">The ID of a peer that delivered the block.</param>
        public delegate void OnBlockDownloadedCallback(uint256 blockHash, Block block, int peerId);

        /// <summary>Interval between checking if peers that were assigned important blocks didn't deliver the block.</summary>
        const int StallingLoopIntervalMs = 500;

        /// <summary>The minimum empty slots percentage to start processing <see cref="downloadJobsQueue" />.</summary>
        const double MinEmptySlotsPercentageToStartProcessingTheQueue = 0.1;

        /// <summary>
        ///     Defines which blocks are considered to be important.
        ///     If requested block height is less than out consensus tip height plus this value then the block is considered to be
        ///     important.
        /// </summary>
        const int ImportantHeightMargin = 10;

        /// <summary>The maximum time in seconds in which peer should deliver an assigned block.</summary>
        /// <remarks>If peer fails to deliver in that time his assignments will be released and the peer penalized.</remarks>
        const int MaxSecondsToDeliverBlock = 30; // TODO change to target spacing / 3

        /// <summary>
        ///     This affects quality score only. If the peer is too fast don't give him all the assignments in the world when
        ///     not in IBD.
        /// </summary>
        const int PeerSpeedLimitWhenNotInIbdBytesPerSec = 1024 * 1024;

        /// <summary>Amount of samples that should be used for average block size calculation.</summary>
        const int AverageBlockSizeSamplesCount = 1000;

        /// <summary>The minimal count of blocks that we can ask for simultaneous download.</summary>
        const int MinimalCountOfBlocksBeingDownloaded = 10;

        /// <summary>
        ///     The maximum blocks being downloaded multiplier. Value of <c>1.1</c> means that we will ask for 10% more than
        ///     we estimated peers can deliver.
        /// </summary>
        const double MaxBlocksBeingDownloadedMultiplier = 1.1;

        /// <summary>Collection of all download assignments to the peers sorted by block height.</summary>
        /// <remarks>This object has to be protected by <see cref="assignedLock" />.</remarks>
        readonly Dictionary<uint256, AssignedDownload> assignedDownloadsByHash;

        /// <summary>Assigned downloads sorted by block height.</summary>
        /// <remarks>This object has to be protected by <see cref="assignedLock" />.</remarks>
        readonly LinkedList<AssignedDownload> assignedDownloadsSorted;

        /// <summary>Assigned headers mapped by peer ID.</summary>
        /// <remarks>This object has to be protected by <see cref="assignedLock" />.</remarks>
        readonly Dictionary<int, List<ChainedHeader>> assignedHeadersByPeerId;

        /// <summary>
        ///     Locks access to <see cref="assignedDownloadsByHash" />, <see cref="assignedHeadersByPeerId" />,
        ///     <see cref="assignedDownloadsSorted" />.
        /// </summary>
        readonly object assignedLock;

        /// <summary>
        ///     The average block size in bytes calculated used up to <see cref="AverageBlockSizeSamplesCount" /> most recent
        ///     samples.
        /// </summary>
        /// <remarks>Write access to this object has to be protected by <see cref="queueLock" />.</remarks>
        readonly AverageCalculator averageBlockSizeBytes;

        /// <summary>The cancellation source that indicates that component's shutdown was triggered.</summary>
        readonly CancellationTokenSource cancellationSource;

        /// <inheritdoc cref="IChainState" />
        readonly IChainState chainState;

        /// <inheritdoc cref="IDateTimeProvider" />
        readonly IDateTimeProvider dateTimeProvider;

        /// <summary>Queue of download jobs which should be assigned to peers.</summary>
        /// <remarks>This object has to be protected by <see cref="queueLock" />.</remarks>
        readonly Queue<DownloadJob> downloadJobsQueue;

        /// <inheritdoc cref="ILogger" />
        readonly ILogger logger;

        /// <inheritdoc cref="NetworkPeerRequirement" />
        /// <remarks>This object has to be protected by <see cref="peerLock" />.</remarks>
        readonly NetworkPeerRequirement networkPeerRequirement;

        /// <summary>Locks access to <see cref="pullerBehaviorsByPeerId" /> and <see cref="networkPeerRequirement" />.</summary>
        readonly object peerLock;

        /// <summary>
        ///     Signaler that triggers <see cref="reassignedJobsQueue" /> and <see cref="downloadJobsQueue" /> processing when
        ///     set.
        /// </summary>
        /// <remarks>This object has to be protected by <see cref="queueLock" />.</remarks>
        readonly AsyncManualResetEvent processQueuesSignal;

        /// <summary>Block puller behaviors mapped by peer ID.</summary>
        /// <remarks>This object has to be protected by <see cref="peerLock" />.</remarks>
        readonly Dictionary<int, IBlockPullerBehavior> pullerBehaviorsByPeerId;

        /// <summary>
        ///     Locks access to <see cref="processQueuesSignal" />, <see cref="downloadJobsQueue" />,
        ///     <see cref="reassignedJobsQueue" />,
        ///     <see cref="maxBlocksBeingDownloaded" />, <see cref="nextJobId" />, <see cref="averageBlockSizeBytes" />.
        /// </summary>
        readonly object queueLock;

        /// <inheritdoc cref="Random" />
        readonly Random random;

        /// <summary>Queue of download jobs which were released from the peers that failed to deliver in time or were disconnected.</summary>
        /// <remarks>This object has to be protected by <see cref="queueLock" />.</remarks>
        readonly Queue<DownloadJob> reassignedJobsQueue;

        /// <summary>Loop that assigns download jobs to the peers.</summary>
        Task assignerLoop;

        /// <summary><c>true</c> if node is in IBD.</summary>
        /// <remarks>This object has to be protected by <see cref="peerLock" />.</remarks>
        bool isIbd;

        /// <summary>
        ///     The maximum blocks that can be downloaded simultaneously.
        ///     Given that all peers are on the same chain they will deliver that amount of blocks in 1 seconds.
        /// </summary>
        /// <remarks>This object has to be protected by <see cref="queueLock" />.</remarks>
        int maxBlocksBeingDownloaded;

        /// <summary>Unique identifier which will be set to the next created download job.</summary>
        /// <remarks>This object has to be protected by <see cref="queueLock" />.</remarks>
        int nextJobId;

        /// <summary>Callback which is called when puller received a block which it was asked for.</summary>
        /// <remarks>Provided by the component that creates the block puller.</remarks>
        OnBlockDownloadedCallback onDownloadedCallback;

        /// <summary>Loop that checks if peers failed to deliver important blocks in given time and penalizes them if they did.</summary>
        Task stallingLoop;

        public BlockPuller(IChainState chainState, NodeSettings nodeSettings, IDateTimeProvider dateTimeProvider,
            INodeStats nodeStats, ILoggerFactory loggerFactory)
        {
            this.reassignedJobsQueue = new Queue<DownloadJob>();
            this.downloadJobsQueue = new Queue<DownloadJob>();

            this.assignedDownloadsByHash = new Dictionary<uint256, AssignedDownload>();
            this.assignedDownloadsSorted = new LinkedList<AssignedDownload>();
            this.assignedHeadersByPeerId = new Dictionary<int, List<ChainedHeader>>();

            this.averageBlockSizeBytes = new AverageCalculator(AverageBlockSizeSamplesCount);

            this.pullerBehaviorsByPeerId = new Dictionary<int, IBlockPullerBehavior>();

            this.processQueuesSignal = new AsyncManualResetEvent(false);
            this.queueLock = new object();
            this.peerLock = new object();
            this.assignedLock = new object();
            this.nextJobId = 0;

            this.networkPeerRequirement = new NetworkPeerRequirement
            {
                MinVersion = nodeSettings.MinProtocolVersion ?? nodeSettings.ProtocolVersion,
                RequiredServices = NetworkPeerServices.Network
            };

            this.cancellationSource = new CancellationTokenSource();
            this.random = new Random();

            this.maxBlocksBeingDownloaded = MinimalCountOfBlocksBeingDownloaded;

            this.chainState = chainState;
            this.dateTimeProvider = dateTimeProvider;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);

            nodeStats.RegisterStats(AddComponentStats, StatsType.Component, GetType().Name);
        }

        /// <inheritdoc />
        public void Initialize(OnBlockDownloadedCallback callback)
        {
            this.onDownloadedCallback = callback;

            this.assignerLoop = AssignerLoopAsync();
            this.stallingLoop = StallingLoopAsync();
        }

        /// <inheritdoc />
        public void RequestPeerServices(NetworkPeerServices services)
        {
            var peerIdsToRemove = new List<int>();

            lock (this.peerLock)
            {
                this.networkPeerRequirement.RequiredServices |= services;

                foreach (var peerIdToBehavior in this.pullerBehaviorsByPeerId)
                {
                    var peer = peerIdToBehavior.Value.AttachedPeer;
                    var reason = string.Empty;

                    if (peer == null || !this.networkPeerRequirement.Check(peer.PeerVersion, peer.Inbound, out reason))
                    {
                        this.logger.LogDebug("Peer Id {0} does not meet requirements, reason: {1}",
                            peerIdToBehavior.Key, reason);
                        peerIdsToRemove.Add(peerIdToBehavior.Key);
                    }
                }
            }

            foreach (var peerId in peerIdsToRemove)
                PeerDisconnected(peerId);
        }

        /// <inheritdoc />
        public double GetAverageBlockSizeBytes()
        {
            return this.averageBlockSizeBytes.Average;
        }

        /// <inheritdoc />
        public void OnIbdStateChanged(bool isIbd)
        {
            lock (this.peerLock)
            {
                foreach (var blockPullerBehavior in this.pullerBehaviorsByPeerId.Values)
                    blockPullerBehavior.OnIbdStateChanged(isIbd);

                this.isIbd = isIbd;
            }
        }

        /// <inheritdoc />
        public void NewPeerTipClaimed(INetworkPeer peer, ChainedHeader newTip)
        {
            lock (this.peerLock)
            {
                var peerId = peer.Connection.Id;

                if (this.pullerBehaviorsByPeerId.TryGetValue(peerId, out var behavior))
                {
                    behavior.Tip = newTip;
                    this.logger.LogDebug("Tip for peer with ID {0} was changed to '{1}'.", peerId, newTip);
                }
                else
                {
                    var supportsRequirments =
                        this.networkPeerRequirement.Check(peer.PeerVersion, peer.Inbound, out var reason);

                    if (supportsRequirments)
                    {
                        behavior = peer.Behavior<IBlockPullerBehavior>();
                        behavior.Tip = newTip;
                        this.pullerBehaviorsByPeerId.Add(peerId, behavior);

                        this.logger.LogDebug("New peer with ID {0} and tip '{1}' was added.", peerId, newTip);
                    }
                    else
                    {
                        this.logger.LogDebug(
                            "Peer ID {0} was discarded since he doesn't support the requirements, reason: {1}", peerId,
                            reason);
                    }
                }
            }
        }

        /// <inheritdoc />
        public void PeerDisconnected(int peerId)
        {
            lock (this.peerLock)
            {
                this.pullerBehaviorsByPeerId.Remove(peerId);
            }

            ReleaseAndReassignAssignments(peerId);
        }

        /// <inheritdoc />
        public void RequestBlocksDownload(List<ChainedHeader> headers, bool highPriority = false)
        {
            Guard.Assert(headers.Count != 0);

            lock (this.queueLock)
            {
                // Enqueue new download job.
                var jobId = this.nextJobId++;

                var queue = highPriority ? this.reassignedJobsQueue : this.downloadJobsQueue;

                queue.Enqueue(new DownloadJob
                {
                    Headers = new List<ChainedHeader>(headers),
                    Id = jobId
                });

                this.logger.LogDebug("{0} blocks were requested from puller. Job ID {1} was created.", headers.Count,
                    jobId);

                this.processQueuesSignal.Set();
            }
        }

        /// <inheritdoc />
        public void PushBlock(uint256 blockHash, Block block, int peerId)
        {
            AssignedDownload assignedDownload;

            lock (this.assignedLock)
            {
                if (!this.assignedDownloadsByHash.TryGetValue(blockHash, out assignedDownload))
                {
                    this.logger.LogTrace("(-)[BLOCK_NOT_REQUESTED]");
                    return;
                }

                this.logger.LogDebug("Assignment '{0}' for peer ID {1} was delivered by peer ID {2}.", blockHash,
                    assignedDownload.PeerId, peerId);

                if (assignedDownload.PeerId != peerId)
                {
                    this.logger.LogTrace("(-)[WRONG_PEER_DELIVERED]");
                    return;
                }

                RemoveAssignedDownloadLocked(assignedDownload);
            }

            var deliveredInSeconds = (this.dateTimeProvider.GetUtcNow() - assignedDownload.AssignedTime).TotalSeconds;
            this.logger.LogDebug("Peer {0} delivered block '{1}' in {2} seconds.", assignedDownload.PeerId, blockHash,
                deliveredInSeconds);

            lock (this.peerLock)
            {
                // Add peer sample.
                if (this.pullerBehaviorsByPeerId.TryGetValue(peerId, out var behavior))
                {
                    behavior.AddSample(block.BlockSize.Value, deliveredInSeconds);

                    // Recalculate quality score.
                    RecalculateQualityScoreLocked(behavior, peerId);
                }
            }

            lock (this.queueLock)
            {
                this.averageBlockSizeBytes.AddSample(block.BlockSize.Value);

                RecalculateMaxBlocksBeingDownloadedLocked();

                this.processQueuesSignal.Set();
            }

            this.onDownloadedCallback(blockHash, block, peerId);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.cancellationSource.Cancel();

            this.assignerLoop?.GetAwaiter().GetResult();
            this.stallingLoop?.GetAwaiter().GetResult();

            this.cancellationSource.Dispose();
        }

        long GetTotalSpeedOfAllPeersBytesPerSec()
        {
            lock (this.peerLock)
            {
                try
                {
                    return this.pullerBehaviorsByPeerId.Sum(x => x.Value.SpeedBytesPerSecond);
                }
                catch (OverflowException)
                {
                    return long.MaxValue;
                }
            }
        }

        /// <summary>Loop that assigns download jobs to the peers.</summary>
        async Task AssignerLoopAsync()
        {
            while (!this.cancellationSource.IsCancellationRequested)
            {
                try
                {
                    await this.processQueuesSignal.WaitAsync(this.cancellationSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    this.logger.LogTrace("(-)[CANCELLED]");
                    return;
                }

                await AssignDownloadJobsAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Loop that continuously checks if peers failed to deliver important blocks in given time and penalizes them if
        ///     they did.
        /// </summary>
        async Task StallingLoopAsync()
        {
            while (!this.cancellationSource.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(StallingLoopIntervalMs, this.cancellationSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    this.logger.LogTrace("(-)[CANCELLED]");
                    return;
                }

                CheckStalling();
            }
        }

        /// <summary>
        ///     Assigns downloads from <see cref="reassignedJobsQueue" /> and <see cref="downloadJobsQueue" /> to the peers
        ///     that are capable of delivering blocks.
        /// </summary>
        async Task AssignDownloadJobsAsync()
        {
            var failedHashes = new List<uint256>();
            var newAssignments = new List<AssignedDownload>();

            lock (this.queueLock)
            {
                // First process reassign queue ignoring slots limitations.
                ProcessQueueLocked(this.reassignedJobsQueue, newAssignments, failedHashes);

                // Process regular queue.
                int emptySlots;
                lock (this.assignedLock)
                {
                    emptySlots = this.maxBlocksBeingDownloaded - this.assignedDownloadsByHash.Count;
                }

                var slotsThreshold =
                    (int) (this.maxBlocksBeingDownloaded * MinEmptySlotsPercentageToStartProcessingTheQueue);

                if (emptySlots >= slotsThreshold)
                    ProcessQueueLocked(this.downloadJobsQueue, newAssignments, failedHashes, emptySlots);
                else
                    this.logger.LogDebug(
                        "Slots threshold is not met, queue will not be processed. There are {0} empty slots, threshold is {1}.",
                        emptySlots, slotsThreshold);

                this.processQueuesSignal.Reset();
            }

            if (newAssignments.Count != 0)
            {
                this.logger.LogDebug("Total amount of downloads assigned in this iteration is {0}.",
                    newAssignments.Count);
                await AskPeersForBlocksAsync(newAssignments).ConfigureAwait(false);
            }

            // Call callbacks with null since puller failed to deliver requested blocks.
            if (failedHashes.Count != 0)
                this.logger.LogDebug("{0} jobs partially or fully failed.", failedHashes.Count);

            foreach (var failedJob in failedHashes)
            {
                // Avoid calling callbacks on shutdown.
                if (this.cancellationSource.IsCancellationRequested)
                {
                    this.logger.LogDebug("Callbacks won't be called because component is being disposed.");
                    break;
                }

                // The choice of peerId does not matter here as the callback should not attempt any validation/banning for a null block.
                this.onDownloadedCallback(failedJob, null, 0);
            }
        }

        /// <summary>Processes specified queue of download jobs.</summary>
        /// <param name="jobsQueue">Queue of download jobs to be processed.</param>
        /// <param name="newAssignments">Collection of new assignments to be populated.</param>
        /// <param name="failedHashes">List of failed hashes to be populated if some of jobs hashes can't be assigned to any peer.</param>
        /// <param name="emptySlots">Max number of assignments that can be made.</param>
        /// <remarks>Have to be locked by <see cref="queueLock" />.</remarks>
        void ProcessQueueLocked(Queue<DownloadJob> jobsQueue, List<AssignedDownload> newAssignments,
            List<uint256> failedHashes, int emptySlots = int.MaxValue)
        {
            while (jobsQueue.Count > 0 && emptySlots > 0)
            {
                var jobToAssign = jobsQueue.Peek();
                var jobHeadersCount = jobToAssign.Headers.Count;

                var assignments = DistributeHeadersLocked(jobToAssign, failedHashes, emptySlots);

                emptySlots -= assignments.Count;

                this.logger.LogDebug("Assigned {0} headers out of {1} for job {2}.", assignments.Count, jobHeadersCount,
                    jobToAssign.Id);

                lock (this.assignedLock)
                {
                    foreach (var assignment in assignments)
                    {
                        newAssignments.Add(assignment);
                        AddAssignedDownloadLocked(assignment);
                    }
                }

                // Remove job from the queue if it was fully consumed.
                if (jobToAssign.Headers.Count == 0)
                    jobsQueue.Dequeue();
            }
        }

        /// <summary>
        ///     Adds assigned download to <see cref="assignedDownloadsByHash" /> and helper structures
        ///     <see cref="assignedDownloadsSorted" /> and <see cref="assignedHeadersByPeerId" />.
        /// </summary>
        /// <remarks>Have to be locked by <see cref="assignedLock" />.</remarks>
        /// <param name="assignment">The assignment.</param>
        void AddAssignedDownloadLocked(AssignedDownload assignment)
        {
            this.assignedDownloadsByHash.Add(assignment.Header.HashBlock, assignment);

            // Add to assignedHeadersByPeerId.
            if (!this.assignedHeadersByPeerId.TryGetValue(assignment.PeerId, out var headersForIds))
            {
                headersForIds = new List<ChainedHeader>();
                this.assignedHeadersByPeerId.Add(assignment.PeerId, headersForIds);
            }

            headersForIds.Add(assignment.Header);

            // Add to assignedDownloadsSorted.
            var lastDownload = this.assignedDownloadsSorted.Last;

            if (lastDownload == null || lastDownload.Value.Header.Height <= assignment.Header.Height)
            {
                assignment.LinkedListNode = this.assignedDownloadsSorted.AddLast(assignment);
            }
            else
            {
                var current = lastDownload;

                while (current.Previous != null && current.Previous.Value.Header.Height > assignment.Header.Height)
                    current = current.Previous;

                assignment.LinkedListNode = this.assignedDownloadsSorted.AddBefore(current, assignment);
            }
        }

        /// <summary>
        ///     Removes assigned download from <see cref="assignedDownloadsByHash" /> and helper structures
        ///     <see cref="assignedDownloadsSorted" /> and <see cref="assignedHeadersByPeerId" />.
        /// </summary>
        /// <remarks>Have to be locked by <see cref="assignedLock" />.</remarks>
        /// <param name="assignment">Assignment that should be removed.</param>
        void RemoveAssignedDownloadLocked(AssignedDownload assignment)
        {
            this.assignedDownloadsByHash.Remove(assignment.Header.HashBlock);

            var headersForId = this.assignedHeadersByPeerId[assignment.PeerId];
            headersForId.Remove(assignment.Header);
            if (headersForId.Count == 0)
                this.assignedHeadersByPeerId.Remove(assignment.PeerId);

            this.assignedDownloadsSorted.Remove(assignment.LinkedListNode);
        }

        /// <summary>Asks peer behaviors in parallel to deliver blocks.</summary>
        /// <param name="assignments">Assignments given to peers.</param>
        async Task AskPeersForBlocksAsync(List<AssignedDownload> assignments)
        {
            // Form batches in order to ask for several blocks from one peer at once.
            var hashesToPeerId = new Dictionary<int, List<uint256>>();
            foreach (var assignedDownload in assignments)
            {
                if (!hashesToPeerId.TryGetValue(assignedDownload.PeerId, out var hashes))
                {
                    hashes = new List<uint256>();
                    hashesToPeerId.Add(assignedDownload.PeerId, hashes);
                }

                hashes.Add(assignedDownload.Header.HashBlock);
            }

            foreach (var hashesPair in hashesToPeerId)
            {
                var hashes = hashesPair.Value;
                var peerId = hashesPair.Key;

                IBlockPullerBehavior peerBehavior;

                lock (this.peerLock)
                {
                    this.pullerBehaviorsByPeerId.TryGetValue(peerId, out peerBehavior);
                }

                var success = false;

                if (peerBehavior != null)
                    try
                    {
                        await peerBehavior.RequestBlocksAsync(hashes).ConfigureAwait(false);
                        success = true;
                    }
                    catch (OperationCanceledException)
                    {
                    }

                if (!success)
                {
                    this.logger.LogDebug("Failed to ask peer {0} for {1} blocks.", peerId, hashes.Count);
                    PeerDisconnected(peerId);
                }
            }
        }

        /// <summary>Distributes download job's headers to peers that can provide blocks represented by those headers.</summary>
        /// <remarks>
        ///     If some of the blocks from the job can't be provided by any peer those headers will be added to a
        ///     <param name="failedHashes"></param>
        ///     .
        ///     <para>
        ///         Have to be locked by <see cref="queueLock" />.
        ///     </para>
        ///     <para>
        ///         Node's quality score is being considered as a weight during the random distribution of the hashes to download
        ///         among the nodes.
        ///     </para>
        /// </remarks>
        /// <param name="downloadJob">Download job to be partially of fully consumed.</param>
        /// <param name="failedHashes">
        ///     List of failed hashes which will be extended in case there is no peer to claim required
        ///     hash.
        /// </param>
        /// <param name="emptySlots">Number of empty slots. This is the maximum number of assignments that can be created.</param>
        /// <returns>List of downloads that were distributed between the peers.</returns>
        List<AssignedDownload> DistributeHeadersLocked(DownloadJob downloadJob, List<uint256> failedHashes,
            int emptySlots)
        {
            var newAssignments = new List<AssignedDownload>();

            HashSet<IBlockPullerBehavior> peerBehaviors;

            lock (this.peerLock)
            {
                peerBehaviors = new HashSet<IBlockPullerBehavior>(this.pullerBehaviorsByPeerId.Values);
            }

            var jobFailed = false;

            if (peerBehaviors.Count == 0)
            {
                this.logger.LogDebug(
                    "There are no peers that can participate in download job distribution! Job ID {0} failed.",
                    downloadJob.Id);
                jobFailed = true;
            }

            var lastSucceededIndex = -1;
            for (var index = 0; index < downloadJob.Headers.Count && index < emptySlots && !jobFailed; index++)
            {
                var header = downloadJob.Headers[index];

                while (!jobFailed)
                {
                    // Weighted random selection based on the peer's quality score.
                    var sumOfQualityScores = peerBehaviors.Sum(x => x.QualityScore);
                    var scoreToReachPeer = this.random.NextDouble() * sumOfQualityScores;

                    var selectedBehavior = peerBehaviors.First();

                    foreach (var peerBehavior in peerBehaviors)
                    {
                        if (peerBehavior.QualityScore >= scoreToReachPeer)
                        {
                            selectedBehavior = peerBehavior;
                            break;
                        }

                        scoreToReachPeer -= peerBehavior.QualityScore;
                    }

                    var attachedPeer = selectedBehavior.AttachedPeer;

                    // Behavior's tip can't be null because we only have behaviors inserted in the behaviors structure after the tip is set.
                    if (attachedPeer != null && selectedBehavior.Tip.FindAncestorOrSelf(header) != null)
                    {
                        var peerId = attachedPeer.Connection.Id;

                        // Assign to this peer.
                        newAssignments.Add(new AssignedDownload
                        {
                            PeerId = peerId,
                            JobId = downloadJob.Id,
                            AssignedTime = this.dateTimeProvider.GetUtcNow(),
                            Header = header
                        });

                        lastSucceededIndex = index;

                        this.logger.LogDebug("Block '{0}' was assigned to peer ID {1}.", header.HashBlock, peerId);
                        break;
                    }

                    // Peer doesn't claim this header.
                    peerBehaviors.Remove(selectedBehavior);

                    if (peerBehaviors.Count != 0)
                        continue;

                    jobFailed = true;
                    this.logger.LogDebug("Job {0} failed because there is no peer claiming header '{1}'.",
                        downloadJob.Id, header);
                }
            }

            if (!jobFailed)
            {
                downloadJob.Headers.RemoveRange(0, lastSucceededIndex + 1);
            }
            else
            {
                var removeFrom = lastSucceededIndex == -1 ? 0 : lastSucceededIndex + 1;

                var failed = downloadJob.Headers.GetRange(removeFrom, downloadJob.Headers.Count - removeFrom)
                    .Select(x => x.HashBlock);
                failedHashes.AddRange(failed);

                downloadJob.Headers.Clear();
            }

            return newAssignments;
        }

        /// <summary>Checks if peers failed to deliver important blocks and penalizes them if they did.</summary>
        void CheckStalling()
        {
            var lastImportantHeight = this.chainState.ConsensusTip.Height + ImportantHeightMargin;
            this.logger.LogDebug("Blocks up to height {0} are considered to be important.", lastImportantHeight);

            var allReleasedAssignments = new List<Dictionary<int, List<ChainedHeader>>>();

            lock (this.assignedLock)
            {
                var current = this.assignedDownloadsSorted.First;

                var peerIdsToReassignJobs = new HashSet<int>();

                while (current != null)
                {
                    // Since the headers in the linked list are sorted by height after we found first that is
                    // not important we can assume that the rest of them are not important.
                    if (current.Value.Header.Height > lastImportantHeight)
                        break;

                    var secondsPassed = (this.dateTimeProvider.GetUtcNow() - current.Value.AssignedTime).TotalSeconds;

                    // Peer failed to deliver important block.
                    var peerId = current.Value.PeerId;
                    current = current.Next;

                    if (secondsPassed < MaxSecondsToDeliverBlock)
                        continue;

                    // Peer already added to the collection of peers to release and reassign.
                    if (peerIdsToReassignJobs.Contains(peerId))
                        continue;

                    peerIdsToReassignJobs.Add(peerId);

                    var assignedCount = this.assignedHeadersByPeerId[peerId].Count;

                    this.logger.LogDebug("Peer {0} failed to deliver {1} blocks from which some were important.",
                        peerId, assignedCount);

                    lock (this.peerLock)
                    {
                        var pullerBehavior = this.pullerBehaviorsByPeerId[peerId];
                        pullerBehavior.Penalize(secondsPassed, assignedCount);

                        RecalculateQualityScoreLocked(pullerBehavior, peerId);
                    }
                }

                // Release downloads for selected peers.
                foreach (var peerId in peerIdsToReassignJobs)
                {
                    var reassignedAssignmentsByJobId = ReleaseAssignmentsLocked(peerId);
                    allReleasedAssignments.Add(reassignedAssignmentsByJobId);
                }
            }

            if (allReleasedAssignments.Count > 0)
                lock (this.queueLock)
                {
                    // Reassign all released jobs.
                    foreach (var released in allReleasedAssignments)
                        ReassignAssignmentsLocked(released);

                    // Trigger queue processing in case anything was reassigned.
                    this.processQueuesSignal.Set();
                }
        }

        /// <summary>Recalculates quality score of a peer or all peers if given peer has the best upload speed.</summary>
        /// <remarks>This method has to be protected by <see cref="peerLock" />.</remarks>
        /// <param name="pullerBehavior">The puller behavior of a peer which quality score should be recalculated.</param>
        /// <param name="peerId">ID of a peer which behavior is passed.</param>
        void RecalculateQualityScoreLocked(IBlockPullerBehavior pullerBehavior, int peerId)
        {
            // Now decide if we need to recalculate quality score for all peers or just for this one.
            var bestSpeed = this.pullerBehaviorsByPeerId.Max(x => x.Value.SpeedBytesPerSecond);

            var adjustedBestSpeed = bestSpeed;
            if (!this.isIbd && adjustedBestSpeed > PeerSpeedLimitWhenNotInIbdBytesPerSec)
                adjustedBestSpeed = PeerSpeedLimitWhenNotInIbdBytesPerSec;

            if (pullerBehavior.SpeedBytesPerSecond != bestSpeed)
            {
                // This is not the best peer. Recalculate it's score only.
                pullerBehavior.RecalculateQualityScore(adjustedBestSpeed);
            }
            else
            {
                this.logger.LogDebug("Peer ID {0} is the fastest peer. Recalculating quality score of all peers.",
                    peerId);

                // This is the best peer. Recalculate quality score for everyone.
                foreach (var peerPullerBehavior in this.pullerBehaviorsByPeerId.Values)
                    peerPullerBehavior.RecalculateQualityScore(adjustedBestSpeed);
            }
        }

        /// <summary>
        ///     Recalculates the maximum number of blocks that can be simultaneously downloaded based
        ///     on the average blocks size and the total speed of all peers that can deliver blocks.
        /// </summary>
        /// <remarks>This object has to be protected by <see cref="queueLock" />.</remarks>
        void RecalculateMaxBlocksBeingDownloadedLocked()
        {
            // How many blocks we can download in 1 second.
            if (this.averageBlockSizeBytes.Average > 0)
                this.maxBlocksBeingDownloaded = (int) (GetTotalSpeedOfAllPeersBytesPerSec() *
                                                       MaxBlocksBeingDownloadedMultiplier /
                                                       this.averageBlockSizeBytes.Average);

            if (this.maxBlocksBeingDownloaded < MinimalCountOfBlocksBeingDownloaded)
                this.maxBlocksBeingDownloaded = MinimalCountOfBlocksBeingDownloaded;

            this.logger.LogDebug("Max number of blocks that can be downloaded at the same time is set to {0}.",
                this.maxBlocksBeingDownloaded);
        }

        /// <summary>
        ///     Finds all blocks assigned to a given peer, removes assignments from <see cref="assignedDownloadsByHash" />,
        ///     adds to <see cref="reassignedJobsQueue" /> and signals the <see cref="processQueuesSignal" />.
        /// </summary>
        /// <param name="peerId">The peer identifier.</param>
        void ReleaseAndReassignAssignments(int peerId)
        {
            Dictionary<int, List<ChainedHeader>> headersByJobId;

            lock (this.assignedLock)
            {
                headersByJobId = ReleaseAssignmentsLocked(peerId);
            }

            if (headersByJobId.Count != 0)
                lock (this.queueLock)
                {
                    ReassignAssignmentsLocked(headersByJobId);
                    this.processQueuesSignal.Set();
                }
        }

        /// <summary>
        ///     Finds all blocks assigned to a given peer, removes assignments from <see cref="assignedDownloadsByHash" /> and
        ///     returns removed assignments.
        /// </summary>
        /// <remarks>Have to be locked by <see cref="assignedLock" />.</remarks>
        Dictionary<int, List<ChainedHeader>> ReleaseAssignmentsLocked(int peerId)
        {
            var headersByJobId = new Dictionary<int, List<ChainedHeader>>();

            if (this.assignedHeadersByPeerId.TryGetValue(peerId, out var headers))
            {
                var assignmentsToRemove = new List<AssignedDownload>(headers.Count);

                foreach (var header in headers)
                {
                    var assignment = this.assignedDownloadsByHash[header.HashBlock];

                    if (!headersByJobId.TryGetValue(assignment.JobId, out var jobHeaders))
                    {
                        jobHeaders = new List<ChainedHeader>();
                        headersByJobId.Add(assignment.JobId, jobHeaders);
                    }

                    jobHeaders.Add(assignment.Header);

                    assignmentsToRemove.Add(assignment);

                    this.logger.LogDebug("Header '{0}' for job ID {1} was released from peer ID {2}.", header,
                        assignment.JobId, peerId);
                }

                foreach (var assignment in assignmentsToRemove)
                    RemoveAssignedDownloadLocked(assignment);
            }

            return headersByJobId;
        }

        /// <summary>Adds items from <paramref name="headersByJobId" /> to the <see cref="reassignedJobsQueue" />.</summary>
        /// <param name="headersByJobId">Block headers mapped by job IDs.</param>
        /// <remarks>Have to be locked by <see cref="queueLock" />.</remarks>
        void ReassignAssignmentsLocked(Dictionary<int, List<ChainedHeader>> headersByJobId)
        {
            foreach (var jobIdToHeaders in headersByJobId)
            {
                var newJob = new DownloadJob
                {
                    Id = jobIdToHeaders.Key,
                    Headers = jobIdToHeaders.Value
                };

                this.reassignedJobsQueue.Enqueue(newJob);
            }
        }


        void AddComponentStats(StringBuilder statsBuilder)
        {
            statsBuilder.AppendLine();
            statsBuilder.AppendLine("======Block Puller======");

            lock (this.assignedLock)
            {
                var pendingBlocks = this.assignedDownloadsByHash.Count;
                statsBuilder.AppendLine($"Blocks being downloaded: {pendingBlocks}");
            }

            lock (this.queueLock)
            {
                var unassignedDownloads = 0;

                foreach (var downloadJob in this.downloadJobsQueue)
                    unassignedDownloads += downloadJob.Headers.Count;

                statsBuilder.AppendLine($"Queued downloads: {unassignedDownloads}");
            }

            var avgBlockSizeBytes = GetAverageBlockSizeBytes();
            var averageBlockSizeKb = avgBlockSizeBytes / 1024.0;
            statsBuilder.AppendLine($"Average block size: {Math.Round(averageBlockSizeKb, 2)} KB");

            double totalSpeedBytesPerSec = GetTotalSpeedOfAllPeersBytesPerSec();
            var totalSpeedKbPerSec = totalSpeedBytesPerSec / 1024.0;
            statsBuilder.AppendLine($"Total download speed: {Math.Round(totalSpeedKbPerSec, 2)} KB/sec");

            var timeToDownloadBlockMs = Math.Round(avgBlockSizeBytes / totalSpeedBytesPerSec * 1000, 2);
            statsBuilder.AppendLine($"Average time to download a block: {timeToDownloadBlockMs} ms");

            var blocksPerSec = Math.Round(totalSpeedBytesPerSec / avgBlockSizeBytes, 2);
            statsBuilder.AppendLine($"Amount of blocks node can download in 1 second: {blocksPerSec}");

            // TODO: add logging per each peer
            // peer -- quality score -- assigned blocks -- speed  (SORT BY QualityScore)
        }
    }
}