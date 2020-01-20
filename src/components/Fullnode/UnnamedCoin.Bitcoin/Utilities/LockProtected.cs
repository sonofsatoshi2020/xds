using System;

namespace UnnamedCoin.Bitcoin.Utilities
{
    /// <summary>
    ///     Supports object-level locking and allows external work in the context of the locks.
    ///     Requires "lock (this.lockObject) { ...}" inside all of the object's public methods.
    /// </summary>
    public interface ILockProtected
    {
        /// <summary>Allows external work within lock context.</summary>
        T Synchronous<T>(Func<T> action);

        /// <summary>Allows external work within lock context.</summary>
        void Synchronous(Action action);
    }

    public class LockProtected : ILockProtected
    {
        public LockProtected()
        {
            this.lockObject = new object();
        }

        protected object lockObject { get; }

        /// <inheritdoc />
        public void Synchronous(Action action)
        {
            lock (this.lockObject)
            {
                action();
            }
        }

        /// <inheritdoc />
        public T Synchronous<T>(Func<T> action)
        {
            lock (this.lockObject)
            {
                var res = action();

                return res;
            }
        }
    }
}