using System;

namespace UnnamedCoin.Bitcoin.EventBus
{
    class Subscription<TEventBase> : ISubscription where TEventBase : EventBase
    {
        /// <summary>
        ///     The action to invoke when a subscripted event type is published.
        /// </summary>
        readonly Action<TEventBase> action;

        public Subscription(Action<TEventBase> action, SubscriptionToken token)
        {
            this.action = action ?? throw new ArgumentNullException(nameof(action));
            this.SubscriptionToken = token ?? throw new ArgumentNullException(nameof(token));
        }

        /// <summary>
        ///     Token returned to the subscriber
        /// </summary>
        public SubscriptionToken SubscriptionToken { get; }

        public void Publish(EventBase eventItem)
        {
            if (!(eventItem is TEventBase))
                throw new ArgumentException("Event Item is not the correct type.");

            this.action.Invoke(eventItem as TEventBase);
        }
    }
}