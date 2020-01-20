using System;

namespace UnnamedCoin.Bitcoin.EventBus
{
    /// <summary>
    ///     Represents a subscription token.
    /// </summary>
    public class SubscriptionToken : IDisposable
    {
        internal SubscriptionToken(IEventBus bus, Type eventType)
        {
            this.Bus = bus;
            this.Token = Guid.NewGuid();
            this.EventType = eventType;
        }

        public IEventBus Bus { get; }

        public Guid Token { get; }

        public Type EventType { get; }

        public void Dispose()
        {
            this.Bus.Unsubscribe(this);
        }
    }
}