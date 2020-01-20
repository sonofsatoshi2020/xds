using System;
using System.Diagnostics;

namespace NBitcoin
{
    public class TraceCorrelationScope : IDisposable
    {
        TraceSource _Source;

        readonly bool _Transfered;

        public TraceCorrelationScope(Guid activity, TraceSource source, bool traceTransfer)
        {
            // NETSTDCONV
            // this.old = Trace.CorrelationManager.ActivityId;

            this._Transfered = this.OldActivity != activity && traceTransfer;
            if (this._Transfered)
                this._Source = source;
            // _Source.TraceTransfer(0, "t", activity);
            // Trace.CorrelationManager.ActivityId = activity;
        }

        public Guid OldActivity { get; private set; }


        #region IDisposable Members

        public void Dispose()
        {
            if (this._Transfered)
            {
                // NETSTDCONV
                //_Source.TraceTransfer(0, "transfer", old);
            }

            // Trace.CorrelationManager.ActivityId = old;
        }

        #endregion
    }

    public class TraceCorrelation
    {
        readonly string _ActivityName;

        volatile bool _First = true;
        readonly TraceSource _Source;

        public TraceCorrelation(TraceSource source, string activityName)
            : this(Guid.NewGuid(), source, activityName)
        {
        }

        public TraceCorrelation(Guid activity, TraceSource source, string activityName)
        {
            this._Source = source;
            this._ActivityName = activityName;
            this.Activity = activity;
        }

        public Guid Activity { get; }

        public TraceCorrelationScope Open(bool traceTransfer = true)
        {
            var scope = new TraceCorrelationScope(this.Activity, this._Source, traceTransfer);
            if (this._First)
            {
                this._First = false;
                // NETSTDCONV
                // _Source.TraceEvent(TraceEventType.Start, 0, _ActivityName);
                this._Source.TraceEvent(TraceEventType.Critical, 0, this._ActivityName);
            }

            return scope;
        }

        public void LogInside(Action act, bool traceTransfer = true)
        {
            using (Open(traceTransfer))
            {
                act();
            }
        }


        public override string ToString()
        {
            return this._ActivityName;
        }
    }
}