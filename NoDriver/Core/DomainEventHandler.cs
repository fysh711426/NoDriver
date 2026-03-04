using NoDriver.Core.Messaging;
using NoDriver.Core.Runtime;
using NoDriver.Core.Tools;
using System.Text.Json;

namespace NoDriver.Core
{
    public delegate void SyncDomainEventHandler<in TEvent>(TEvent @event, Connection sender) where TEvent : IEvent;

    public delegate Task AsyncDomainEventHandler<in TEvent>(TEvent @event, Connection sender) where TEvent : IEvent;

    public interface IDomainEventHandlerWrapper
    {
        Delegate RawHandler { get; }
        Task HandleAsync(ProtocolEvent rawEvent, Connection sender);
    }

    public class DomainEventHandlerWrapper<TEvent> : IDomainEventHandlerWrapper where TEvent : IEvent
    {
        public Delegate RawHandler { get; }

        private readonly AsyncDomainEventHandler<TEvent> _handler;

        public DomainEventHandlerWrapper(AsyncDomainEventHandler<TEvent> handler)
        {
            RawHandler = handler;
            _handler = handler;
        }
        public DomainEventHandlerWrapper(SyncDomainEventHandler<TEvent> handler)
        {
            RawHandler = handler;
            _handler = (e, sender) =>
            {
                handler(e, sender);
                return Task.CompletedTask;
            };
        }
        public Task HandleAsync(ProtocolEvent rawEvent, Connection sender)
        {
            var @event = rawEvent.Params.Deserialize<TEvent>(JsonProtocolSerialization.Settings);
            if (@event == null)
                throw new InvalidOperationException($"Failed to deserialize event to {typeof(TEvent).Name}.");
            return _handler(@event, sender);
        }
    }
}
