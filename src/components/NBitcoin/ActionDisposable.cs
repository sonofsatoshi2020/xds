using System;

namespace NBitcoin
{
    class ActionDisposable : IDisposable
    {
        Action onEnter;
        readonly Action onLeave;

        public ActionDisposable(Action onEnter, Action onLeave)
        {
            this.onEnter = onEnter;
            this.onLeave = onLeave;
            onEnter();
        }

        #region IDisposable Members

        public void Dispose()
        {
            this.onLeave();
        }

        #endregion
    }
}