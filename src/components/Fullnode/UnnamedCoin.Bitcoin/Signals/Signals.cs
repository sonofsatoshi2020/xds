using Microsoft.Extensions.Logging;
using UnnamedCoin.Bitcoin.EventBus;

namespace UnnamedCoin.Bitcoin.Signals
{
    public interface ISignals : IEventBus
    {
    }

    public class Signals : InMemoryEventBus, ISignals
    {
        public Signals(ILoggerFactory loggerFactory, ISubscriptionErrorHandler subscriptionErrorHandler) : base(
            loggerFactory, subscriptionErrorHandler)
        {
        }
    }
}