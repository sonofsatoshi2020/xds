using System;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using UnnamedCoin.Bitcoin.Base;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Connection;
using UnnamedCoin.Bitcoin.Consensus;

namespace UnnamedCoin.Bitcoin.Controllers
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ActionDescription : Attribute
    {
        public ActionDescription(string description)
        {
            this.Description = description;
        }

        public string Description { get; }
    }

    public abstract class FeatureController : Controller
    {
        public FeatureController(
            IFullNode fullNode = null,
            Network network = null,
            NodeSettings nodeSettings = null,
            ChainIndexer chainIndexer = null,
            IChainState chainState = null,
            IConnectionManager connectionManager = null,
            IConsensusManager consensusManager = null)
        {
            this.FullNode = fullNode;
            this.Settings = nodeSettings;
            this.Network = network;
            this.ChainIndexer = chainIndexer;
            this.ChainState = chainState;
            this.ConnectionManager = connectionManager;
            this.ConsensusManager = consensusManager;
        }

        protected IFullNode FullNode { get; set; }

        protected NodeSettings Settings { get; set; }

        protected Network Network { get; set; }

        protected ChainIndexer ChainIndexer { get; set; }

        protected IChainState ChainState { get; set; }

        protected IConnectionManager ConnectionManager { get; set; }

        protected IConsensusManager ConsensusManager { get; }
    }
}