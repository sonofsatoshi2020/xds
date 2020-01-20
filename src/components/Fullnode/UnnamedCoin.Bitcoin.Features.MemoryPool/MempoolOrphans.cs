using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.EventBus.CoreEvents;
using UnnamedCoin.Bitcoin.Features.Consensus.CoinViews;
using UnnamedCoin.Bitcoin.Features.MemoryPool.Interfaces;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.Signals;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.MemoryPool
{
    /// <summary>
    ///     Manages memory pool orphan transactions.
    /// </summary>
    public class MempoolOrphans
    {
        /// <summary>Expiration time for orphan transactions in seconds.</summary>
        const long OrphanTxExpireTime = 20 * 60;

        /// <summary>Default for -maxorphantx, maximum number of orphan transactions kept in memory.</summary>
        public const int DefaultMaxOrphanTransactions = 100;

        /// <summary>Minimum time between orphan transactions expire time checks in seconds.</summary>
        public const int OrphanTxExpireInterval = 5 * 60;

        /// <summary>Thread safe access to the best chain of block headers (that the node is aware of) from genesis.</summary>
        readonly ChainIndexer chainIndexer;

        /// <summary>Coin view of the memory pool.</summary>
        readonly ICoinView coinView;

        /// <summary>Date and time information provider.</summary>
        readonly IDateTimeProvider dateTimeProvider;

        /// <summary>Lock object for locking access to local collections.</summary>
        readonly object lockObject;

        /// <summary>Instance logger for the memory pool.</summary>
        readonly ILogger logger;

        /// <summary>Dictionary of orphan transactions keyed by transaction hash.</summary>
        readonly Dictionary<uint256, OrphanTx> mapOrphanTransactions;

        /// <summary>Dictionary of orphan transactions keyed by transaction output.</summary>
        readonly Dictionary<OutPoint, List<OrphanTx>> mapOrphanTransactionsByPrev;

        /// <summary>Manages the memory pool transactions.</summary>
        readonly MempoolManager mempoolManager;

        /// <summary>Settings from the memory pool.</summary>
        readonly MempoolSettings mempoolSettings;

        /// <summary> Object for generating random numbers used for randomly purging orphans.</summary>
        readonly Random random = new Random();

        /// <summary>Dictionary of recent transaction rejects keyed by transaction hash</summary>
        readonly Dictionary<uint256, uint256> recentRejects;

        /// <summary>Node notifications available to subscribe to.</summary>
        readonly ISignals signals;

        /// <summary>Location on chain when rejects are validated.</summary>
        uint256 hashRecentRejectsChainTip;

        /// <summary>Time of next sweep to purge expired orphan transactions.</summary>
        long nNextSweep;

        public MempoolOrphans(
            ChainIndexer chainIndexer,
            ISignals signals,
            IMempoolValidator validator,
            ICoinView coinView,
            IDateTimeProvider dateTimeProvider,
            MempoolSettings mempoolSettings,
            ILoggerFactory loggerFactory,
            MempoolManager mempoolManager)
        {
            this.chainIndexer = chainIndexer;
            this.signals = signals;
            this.coinView = coinView;
            this.dateTimeProvider = dateTimeProvider;
            this.mempoolSettings = mempoolSettings;
            this.mempoolManager = mempoolManager;
            this.Validator = validator;

            this.mapOrphanTransactions = new Dictionary<uint256, OrphanTx>();
            this.mapOrphanTransactionsByPrev =
                new Dictionary<OutPoint, List<OrphanTx>>(); // OutPoint already correctly implements equality compare
            this.recentRejects = new Dictionary<uint256, uint256>();
            this.hashRecentRejectsChainTip = uint256.Zero;
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            this.lockObject = new object();
        }

        /// <summary>Memory pool validator for validating transactions.</summary>
        public IMempoolValidator Validator { get; }

        /// <summary>
        ///     Gets a list of all the orphan transactions.
        /// </summary>
        /// <returns>A list of orphan transactions.</returns>
        public List<OrphanTx> OrphansList() // for testing
        {
            List<OrphanTx> result;
            lock (this.lockObject)
            {
                result = this.mapOrphanTransactions.Values.ToList();
            }

            return result;
        }

        /// <summary>
        ///     Orphan list count.
        /// </summary>
        public int OrphansCount()
        {
            int result;
            lock (this.lockObject)
            {
                result = this.mapOrphanTransactions.Count;
            }

            return result;
        }

        /// <summary>
        ///     Remove transactions form the orphan list.
        /// </summary>
        public void RemoveForBlock(List<Transaction> transactionsToRemove)
        {
            lock (this.lockObject)
            {
                foreach (var transaction in transactionsToRemove) EraseOrphanTxLock(transaction.GetHash());
            }
        }

        /// <summary>
        ///     Whether the transaction id is already present in the list of orphans.
        /// </summary>
        /// <param name="trxid">transaction id to search for.</param>
        /// <returns>Whether the transaction id is present.</returns>
        public async Task<bool> AlreadyHaveAsync(uint256 trxid)
        {
            // Use pcoinsTip->HaveCoinsInCache as a quick approximation to exclude
            // requesting or processing some txs which have already been included in a block
            var isTxPresent = false;
            lock (this.lockObject)
            {
                if (this.chainIndexer.Tip.HashBlock != this.hashRecentRejectsChainTip)
                {
                    // If the chain tip has changed previously rejected transactions
                    // might be now valid, e.g. due to a nLockTime'd tx becoming valid,
                    // or a double-spend. Reset the rejects filter and give those
                    // txs a second chance.
                    this.logger.LogDebug("Executing task to clear rejected transactions.");
                    this.hashRecentRejectsChainTip = this.chainIndexer.Tip.HashBlock;
                    this.recentRejects.Clear();
                }

                isTxPresent = this.recentRejects.ContainsKey(trxid) || this.mapOrphanTransactions.ContainsKey(trxid);
            }

            if (!isTxPresent) isTxPresent = await this.mempoolManager.ExistsAsync(trxid).ConfigureAwait(false);

            return isTxPresent;
        }

        /// <summary>
        ///     Processes orphan transactions.
        ///     Executed when receive a new transaction through MempoolBehavior.
        /// </summary>
        /// <param name="behavior">Memory pool behavior that received new transaction.</param>
        /// <param name="tx">The new transaction received.</param>
        public async Task ProcessesOrphansAsync(MempoolBehavior behavior, Transaction tx)
        {
            var workQueue = new Queue<OutPoint>();
            var eraseQueue = new List<uint256>();

            var trxHash = tx.GetHash();
            for (var index = 0; index < tx.Outputs.Count; index++)
                workQueue.Enqueue(new OutPoint(trxHash, index));

            // Recursively process any orphan transactions that depended on this one
            var setMisbehaving = new List<ulong>();
            while (workQueue.Any())
            {
                List<OrphanTx> itByPrev = null;
                lock (this.lockObject)
                {
                    var prevOrphans = this.mapOrphanTransactionsByPrev.TryGet(workQueue.Dequeue());

                    if (prevOrphans != null)
                        // Create a copy of the list so we can manage it outside of the lock.
                        itByPrev = prevOrphans.ToList();
                }

                if (itByPrev == null)
                    continue;

                foreach (var mi in itByPrev)
                {
                    var orphanTx = mi.Tx;
                    var orphanHash = orphanTx.GetHash();
                    var fromPeer = mi.NodeId;

                    if (setMisbehaving.Contains(fromPeer))
                        continue;

                    // Use a dummy CValidationState so someone can't setup nodes to counter-DoS based on orphan
                    // resolution (that is, feeding people an invalid transaction based on LegitTxX in order to get
                    // anyone relaying LegitTxX banned)
                    var stateDummy = new MempoolValidationState(true);
                    if (await this.Validator.AcceptToMemoryPool(stateDummy, orphanTx))
                    {
                        this.logger.LogInformation("accepted orphan tx {0}", orphanHash);

                        behavior.RelayTransaction(orphanTx.GetHash());

                        this.signals.Publish(new TransactionReceived(orphanTx));

                        for (var index = 0; index < orphanTx.Outputs.Count; index++)
                            workQueue.Enqueue(new OutPoint(orphanHash, index));

                        eraseQueue.Add(orphanHash);
                    }
                    else if (!stateDummy.MissingInputs)
                    {
                        var nDos = 0;

                        if (stateDummy.IsInvalid && nDos > 0)
                        {
                            // Punish peer that gave us an invalid orphan tx
                            //Misbehaving(fromPeer, nDos);
                            setMisbehaving.Add(fromPeer);
                            this.logger.LogInformation("invalid orphan tx {0}", orphanHash);
                        }

                        // Has inputs but not accepted to mempool
                        // Probably non-standard or insufficient fee/priority
                        this.logger.LogInformation("removed orphan tx {0}", orphanHash);
                        eraseQueue.Add(orphanHash);
                        if (!orphanTx.HasWitness && !stateDummy.CorruptionPossible)
                            // Do not use rejection cache for witness transactions or
                            // witness-stripped transactions, as they can have been malleated.
                            // See https://github.com/bitcoin/bitcoin/issues/8279 for details.

                            AddToRecentRejects(orphanHash);
                    }

                    // TODO: implement sanity checks.
                    //this.memPool.Check(new MempoolCoinView(this.coinView, this.memPool, this.MempoolLock, this.Validator));
                }
            }

            if (eraseQueue.Count > 0)
                lock (this.lockObject)
                {
                    foreach (var hash in eraseQueue) EraseOrphanTxLock(hash);
                }
        }

        /// <summary>
        ///     Adds transaction hash to recent rejects.
        /// </summary>
        /// <param name="orphanHash">Hash to add.</param>
        public void AddToRecentRejects(uint256 orphanHash)
        {
            lock (this.lockObject)
            {
                this.recentRejects.TryAdd(orphanHash, orphanHash);
            }
        }

        /// <summary>
        ///     Adds transaction to orphan list after checking parents and inputs.
        ///     Executed if new transaction has been validated to having missing inputs.
        ///     If parents for this transaction have all been rejected than reject this transaction.
        /// </summary>
        /// <param name="from">Source node for transaction.</param>
        /// <param name="tx">Transaction to add.</param>
        /// <returns>Whether the transaction was added to orphans.</returns>
        public bool ProcessesOrphansMissingInputs(INetworkPeer from, Transaction tx)
        {
            // It may be the case that the orphans parents have all been rejected
            bool rejectedParents;
            lock (this.lockObject)
            {
                rejectedParents = tx.Inputs.Any(txin => this.recentRejects.ContainsKey(txin.PrevOut.Hash));
            }

            if (rejectedParents)
            {
                this.logger.LogInformation("not keeping orphan with rejected parents {0}", tx.GetHash());
                this.logger.LogTrace("(-)[REJECT_PARENTS_ORPH]:false");
                return false;
            }

            foreach (var txin in tx.Inputs)
            {
                // TODO: this goes in the RelayBehaviour
                //CInv _inv(MSG_TX | nFetchFlags, txin.prevout.hash);
                //behavior.AttachedNode.Behaviors.Find<RelayBehaviour>() pfrom->AddInventoryKnown(_inv);
                //if (!await this.AlreadyHave(txin.PrevOut.Hash))
                //  from. pfrom->AskFor(_inv);
            }

            var ret = AddOrphanTx(from.PeerVersion.Nonce, tx);

            // DoS prevention: do not allow mapOrphanTransactions to grow unbounded
            var nMaxOrphanTx = this.mempoolSettings.MaxOrphanTx;
            var nEvicted = LimitOrphanTxSize(nMaxOrphanTx);
            if (nEvicted > 0)
                this.logger.LogInformation("mapOrphan overflow, removed {0} tx", nEvicted);

            return ret;
        }

        /// <summary>
        ///     Limit the orphan transaction list by a max size.
        ///     First prune expired orphan pool entries within the sweep period.
        ///     If further pruning is required to get to limit, then evict randomly.
        /// </summary>
        /// <param name="maxOrphanTx">Size to limit the orphan transactions to.</param>
        /// <returns>The number of transactions evicted.</returns>
        public int LimitOrphanTxSize(int maxOrphanTx)
        {
            var nEvicted = 0;
            var nNow = this.dateTimeProvider.GetTime();
            if (this.nNextSweep <= nNow)
            {
                // Sweep out expired orphan pool entries:
                var nErased = 0;
                var nMinExpTime = nNow + OrphanTxExpireTime - OrphanTxExpireInterval;

                List<OrphanTx> orphansValues;
                lock (this.lockObject)
                {
                    orphansValues = this.mapOrphanTransactions.Values.ToList();
                }

                foreach (var maybeErase in orphansValues
                ) // create a new list as this will be removing items from the dictionary
                    if (maybeErase.TimeExpire <= nNow)
                        lock (this.lockObject)
                        {
                            nErased += EraseOrphanTxLock(maybeErase.Tx.GetHash()) ? 1 : 0;
                        }
                    else
                        nMinExpTime = Math.Min(maybeErase.TimeExpire, nMinExpTime);

                // Sweep again 5 minutes after the next entry that expires in order to batch the linear scan.
                this.nNextSweep = nMinExpTime + OrphanTxExpireInterval;

                if (nErased > 0)
                    this.logger.LogInformation("Erased {0} orphan tx due to expiration", nErased);
            }

            lock (this.lockObject)
            {
                this.logger.LogDebug("Executing task to prune orphan txs to max limit.");
                while (this.mapOrphanTransactions.Count > maxOrphanTx)
                {
                    // Evict a random orphan:
                    var randomCount = this.random.Next(this.mapOrphanTransactions.Count);
                    var erase = this.mapOrphanTransactions.ElementAt(randomCount).Key;
                    EraseOrphanTxLock(erase);
                    ++nEvicted;
                }
            }

            return nEvicted;
        }

        /// <summary>
        ///     Add an orphan transaction to the orphan pool.
        /// </summary>
        /// <param name="nodeId">Node id of the source node.</param>
        /// <param name="tx">The transaction to add.</param>
        /// <returns>Whether the orphan transaction was added.</returns>
        public bool AddOrphanTx(ulong nodeId, Transaction tx)
        {
            lock (this.lockObject)
            {
                var hash = tx.GetHash();
                if (this.mapOrphanTransactions.ContainsKey(hash))
                {
                    this.logger.LogTrace("(-)[DUP_ORPH]:false");
                    return false;
                }

                // Ignore big transactions, to avoid a
                // send-big-orphans memory exhaustion attack. If a peer has a legitimate
                // large transaction with a missing parent then we assume
                // it will rebroadcast it later, after the parent transaction(s)
                // have been mined or received.
                // 100 orphans, each of which is at most 99,999 bytes big is
                // at most 10 megabytes of orphans and somewhat more byprev index (in the worst case):
                var sz = MempoolValidator.GetTransactionWeight(tx, this.Validator.ConsensusOptions);
                if (sz >= this.chainIndexer.Network.Consensus.Options.MaxStandardTxWeight)
                {
                    this.logger.LogInformation("ignoring large orphan tx (size: {0}, hash: {1})", sz, hash);
                    this.logger.LogTrace("(-)[LARGE_ORPH]:false");
                    return false;
                }

                var orphan = new OrphanTx
                {
                    Tx = tx,
                    NodeId = nodeId,
                    TimeExpire = this.dateTimeProvider.GetTime() + OrphanTxExpireTime
                };

                if (this.mapOrphanTransactions.TryAdd(hash, orphan))
                    foreach (var txin in tx.Inputs)
                    {
                        var prv = this.mapOrphanTransactionsByPrev.TryGet(txin.PrevOut);
                        if (prv == null)
                        {
                            prv = new List<OrphanTx>();
                            this.mapOrphanTransactionsByPrev.Add(txin.PrevOut, prv);
                        }

                        prv.Add(orphan);
                    }

                var orphanSize = this.mapOrphanTransactions.Count;
                this.logger.LogInformation("stored orphan tx {0} (mapsz {1} outsz {2})", hash, orphanSize,
                    this.mapOrphanTransactionsByPrev.Count);
                this.Validator.PerformanceCounter.SetMempoolOrphanSize(orphanSize);
            }

            return true;
        }

        /// <summary>
        ///     Erase an specific transaction from orphan pool.
        /// </summary>
        /// <param name="hash">hash of the transaction.</param>
        /// <returns>Whether erased.</returns>
        bool EraseOrphanTxLock(uint256 hash)
        {
            var orphTx = this.mapOrphanTransactions.TryGet(hash);

            if (orphTx == null)
            {
                this.logger.LogTrace("(-)[NOTFOUND_ORPH]:false");
                return false;
            }

            foreach (var txin in orphTx.Tx.Inputs)
            {
                var prevOrphTxList = this.mapOrphanTransactionsByPrev.TryGet(txin.PrevOut);

                if (prevOrphTxList == null)
                    continue;

                prevOrphTxList.Remove(orphTx);

                if (!prevOrphTxList.Any())
                    this.mapOrphanTransactionsByPrev.Remove(txin.PrevOut);
            }

            this.mapOrphanTransactions.Remove(hash);

            var orphanSize = this.mapOrphanTransactions.Count;
            this.Validator.PerformanceCounter.SetMempoolOrphanSize(orphanSize);

            return true;
        }

        /// <summary>
        ///     Erase all orphans for a specific peer node.
        /// </summary>
        /// <param name="peerId">Peer node id</param>
        public void EraseOrphansFor(ulong peerId)
        {
            lock (this.lockObject)
            {
                this.logger.LogDebug("Executing task to erase orphan transactions.");

                var erased = 0;

                var orphansToErase = this.mapOrphanTransactions.Values.ToList();
                foreach (var erase in orphansToErase)
                    if (erase.NodeId == peerId)
                        erased += EraseOrphanTxLock(erase.Tx.GetHash()) ? 1 : 0;

                if (erased > 0)
                    this.logger.LogInformation("Erased {0} orphan tx from peer {1}", erased, peerId);
            }
        }

        /// <summary>
        ///     Object representing an orphan transaction information.
        ///     When modifying, adapt the copy of this definition in tests/DoS_tests.
        /// </summary>
        public class OrphanTx
        {
            /// <summary>The id of the node that sent this transaction.</summary>
            public ulong NodeId;

            /// <summary>The time when this orphan transaction will expire.</summary>
            public long TimeExpire;

            /// <summary>The orphan transaction.</summary>
            public Transaction Tx;
        }
    }
}