using System.Diagnostics;

namespace NBitcoin.Protocol
{
    public static class NodeServerTrace
    {
        public static TraceSource Trace { get; } = new TraceSource("NBitcoin.NodeServer");
    }
}