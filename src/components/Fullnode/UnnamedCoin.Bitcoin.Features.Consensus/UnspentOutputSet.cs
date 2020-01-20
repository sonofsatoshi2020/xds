﻿using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Consensus
{
    public class UnspentOutputSet
    {
        Dictionary<uint256, UnspentOutputs> unspents;

        public TxOut GetOutputFor(TxIn txIn)
        {
            var unspent = this.unspents.TryGet(txIn.PrevOut.Hash);
            if (unspent == null)
                return null;

            return unspent.TryGetOutput(txIn.PrevOut.N);
        }

        public bool HaveInputs(Transaction tx)
        {
            return tx.Inputs.All(txin => GetOutputFor(txin) != null);
        }

        public UnspentOutputs AccessCoins(uint256 uint256)
        {
            return this.unspents.TryGet(uint256);
        }

        public Money GetValueIn(Transaction tx)
        {
            return tx.Inputs.Select(txin => GetOutputFor(txin).Value).Sum();
        }

        /// <summary>
        ///     Adds transaction's outputs to unspent coins list and removes transaction's inputs from it.
        /// </summary>
        /// <param name="transaction">Transaction which inputs and outputs are used for updating unspent coins list.</param>
        /// <param name="height">Height of a block that contains target transaction.</param>
        public void Update(Transaction transaction, int height)
        {
            if (!transaction.IsCoinBase)
                foreach (var input in transaction.Inputs)
                {
                    var c = AccessCoins(input.PrevOut.Hash);

                    c.Spend(input.PrevOut.N);
                }

            this.unspents.AddOrReplace(transaction.GetHash(), new UnspentOutputs((uint) height, transaction));
        }

        public void SetCoins(UnspentOutputs[] coins)
        {
            this.unspents = new Dictionary<uint256, UnspentOutputs>(coins.Length);
            foreach (var coin in coins)
                if (coin != null)
                    this.unspents.Add(coin.TransactionId, coin);
        }

        public void TrySetCoins(UnspentOutputs[] coins)
        {
            this.unspents = new Dictionary<uint256, UnspentOutputs>(coins.Length);
            foreach (var coin in coins)
                if (coin != null)
                    this.unspents.TryAdd(coin.TransactionId, coin);
        }

        public IList<UnspentOutputs> GetCoins()
        {
            return this.unspents.Select(u => u.Value).ToList();
        }
    }
}