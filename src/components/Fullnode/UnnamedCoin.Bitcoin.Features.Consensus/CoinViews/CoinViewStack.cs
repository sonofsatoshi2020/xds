using System.Collections.Generic;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.Features.Consensus.CoinViews
{
    /// <summary>
    ///     Stack of coinview layers. All classes in the stack have to be based on <see cref="CoinView" /> class
    ///     and all classes except for the stack bottom class have to implement <see cref="IBackedCoinView" />
    ///     interface.
    /// </summary>
    public class CoinViewStack
    {
        /// <summary>
        ///     Initializes an instance of the stack using existing coinview.
        /// </summary>
        /// <param name="top">Coinview at the top of the stack.</param>
        public CoinViewStack(ICoinView top)
        {
            Guard.NotNull(top, nameof(top));

            this.Top = top;
            var current = top;
            while (current is IBackedCoinView) current = ((IBackedCoinView) current).Inner;
            this.Bottom = current;
        }

        /// <summary>Coinview class at the top of the stack.</summary>
        public ICoinView Top { get; }

        /// <summary>Coinview class at the bottom of the stack.</summary>
        public ICoinView Bottom { get; }

        /// <summary>
        ///     Enumerates coinviews in the stack ordered from the top to the bottom.
        /// </summary>
        /// <returns>Enumeration of coin views in the stack ordered from the top to the bottom.</returns>
        public IEnumerable<ICoinView> GetElements()
        {
            var current = this.Top;
            while (current is IBackedCoinView)
            {
                yield return current;

                current = ((IBackedCoinView) current).Inner;
            }

            if (current != null)
                yield return current;
        }

        /// <summary>
        ///     Finds a coinview of specific type in the stack.
        /// </summary>
        /// <typeparam name="T">Type of the coinview to search for.</typeparam>
        /// <returns>Coinview of the specific type from the stack or <c>null</c> if such a coinview is not in the stack.</returns>
        public T Find<T>()
        {
            var current = this.Top;
            if (current is T)
                return (T) current;

            while (current is IBackedCoinView)
            {
                current = ((IBackedCoinView) current).Inner;
                if (current is T)
                    return (T) current;
            }

            return default;
        }
    }
}