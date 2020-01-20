using System;

namespace UnnamedCoin.Bitcoin.EventBus
{
    /// <summary>
    ///     Basic abstract implementation of <see cref="IEvent" />.
    /// </summary>
    /// <seealso cref="Stratis.Bitcoin.EventBus.IEvent" />
    public abstract class EventBase
    {
        public EventBase()
        {
            // Assigns an unique id to the event.
            this.CorrelationId = Guid.NewGuid();
        }

        /// <inheritdoc />
        public Guid CorrelationId { get; }

        public override string ToString()
        {
            return $"{this.CorrelationId.ToString()} - {GetType().Name}";
        }
    }
}