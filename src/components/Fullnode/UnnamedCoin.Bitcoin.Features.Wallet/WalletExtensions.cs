﻿using System;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.Features.Wallet
{
    public static class WalletExtensions
    {
        /// <summary>
        ///     Determines whether the chain is downloaded and up to date.
        /// </summary>
        /// <param name="chainIndexer">The chain.</param>
        public static bool IsDownloaded(this ChainIndexer chainIndexer)
        {
            return chainIndexer.Tip.Header.BlockTime.ToUnixTimeSeconds() >
                   DateTimeOffset.UtcNow.ToUnixTimeSeconds() - TimeSpan.FromHours(1).TotalSeconds;
        }

        /// <summary>
        ///     Gets the height of the first block created at or after this date.
        /// </summary>
        /// <param name="chainIndexer">The chain of blocks.</param>
        /// <param name="date">The date.</param>
        /// <returns>The height of the first block created after the date.</returns>
        public static int GetHeightAtTime(this ChainIndexer chainIndexer, DateTime date)
        {
            var blockSyncStart = 0;
            var upperLimit = chainIndexer.Tip.Height;
            var lowerLimit = 0;
            var found = false;
            while (!found)
            {
                var check = lowerLimit + (upperLimit - lowerLimit) / 2;
                var blockTimeAtCheck = chainIndexer.GetHeader(check).Header.BlockTime.DateTime;

                if (blockTimeAtCheck > date)
                    upperLimit = check;
                else if (blockTimeAtCheck < date)
                    lowerLimit = check;
                else
                    return check;

                if (upperLimit - lowerLimit <= 1)
                {
                    blockSyncStart = upperLimit;
                    found = true;
                }
            }

            return blockSyncStart;
        }
    }
}