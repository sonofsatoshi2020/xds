using System;

namespace UnnamedCoin.Bitcoin.Builder.Feature
{
    /// <summary>
    ///     Exception thrown when feature dependencies are missing.
    /// </summary>
    public class MissingDependencyException : Exception
    {
        /// <inheritdoc />
        public MissingDependencyException()
        {
        }

        /// <inheritdoc />
        public MissingDependencyException(string message)
            : base(message)
        {
        }

        /// <inheritdoc />
        public MissingDependencyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}