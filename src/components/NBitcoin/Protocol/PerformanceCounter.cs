using System;
using System.Threading;

namespace NBitcoin.Protocol
{
    public class PerformanceSnapshot
    {
        public PerformanceSnapshot(long readen, long written)
        {
            this.TotalWrittenBytes = written;
            this.TotalReadBytes = readen;
        }

        public long TotalWrittenBytes { get; }

        public long TotalReadBytes { get; set; }

        public TimeSpan Elapsed => this.Taken - this.Start;

        public ulong ReadBytesPerSecond => (ulong) (this.TotalReadBytes / this.Elapsed.TotalSeconds);

        public ulong WrittenBytesPerSecond => (ulong) (this.TotalWrittenBytes / this.Elapsed.TotalSeconds);

        public DateTime Start { get; set; }

        public DateTime Taken { get; set; }

        public static PerformanceSnapshot operator -(PerformanceSnapshot end, PerformanceSnapshot start)
        {
            if (end.Start != start.Start)
                throw new InvalidOperationException("Performance snapshot should be taken from the same point of time");

            if (end.Taken < start.Taken)
                throw new InvalidOperationException("The difference of snapshot can't be negative");

            return new PerformanceSnapshot(end.TotalReadBytes - start.TotalReadBytes,
                end.TotalWrittenBytes - start.TotalWrittenBytes)
            {
                Start = start.Taken,
                Taken = end.Taken
            };
        }

        public override string ToString()
        {
            return "Read : " + ToKBSec(this.ReadBytesPerSecond) + ", Write : " + ToKBSec(this.WrittenBytesPerSecond);
        }

        string ToKBSec(ulong bytesPerSec)
        {
            var speed = bytesPerSec / 1024.0;
            return speed.ToString("0.00") + " KB/S)";
        }
    }

    public class PerformanceCounter
    {
        long readBytes;

        long writtenBytes;

        public PerformanceCounter()
        {
            this.Start = DateTime.UtcNow;
        }

        public DateTime Start { get; }

        public TimeSpan Elapsed => DateTime.UtcNow - this.Start;

        public long WrittenBytes => this.writtenBytes;
        public long ReadBytes => this.readBytes;

        public void AddWritten(long count)
        {
            Interlocked.Add(ref this.writtenBytes, count);
        }

        public void AddRead(long count)
        {
            Interlocked.Add(ref this.readBytes, count);
        }

        public PerformanceSnapshot Snapshot()
        {
            var snap = new PerformanceSnapshot(this.ReadBytes, this.WrittenBytes)
            {
                Start = this.Start,
                Taken = DateTime.UtcNow
            };
            return snap;
        }

        public override string ToString()
        {
            return Snapshot().ToString();
        }

        public void Add(PerformanceCounter counter)
        {
            AddWritten(counter.WrittenBytes);
            AddRead(counter.ReadBytes);
        }
    }
}