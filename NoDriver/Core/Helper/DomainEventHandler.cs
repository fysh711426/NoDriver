using NoDriver.Core.Interface;
using NoDriver.Core.Message;
using System.Text.Json;

namespace NoDriver.Core.Helper
{
    public delegate void SyncDomainEventHandler<in TEvent>(TEvent @event) where TEvent : IEvent;

    public delegate Task AsyncDomainEventHandler<in TEvent>(TEvent @event) where TEvent : IEvent;

    public interface IDomainEventHandlerWrapper
    {
        Delegate RawHandler { get; }
        Task HandleAsync(ProtocolEvent rawEvent);
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
            _handler = (e) =>
            {
                handler(e);
                return Task.CompletedTask;
            };
        }
        public Task HandleAsync(ProtocolEvent rawEvent)
        {
            var @event = rawEvent.Params.Deserialize<TEvent>(JsonProtocolSerialization.Settings);
            if (@event != null)
                return _handler(@event);
            return Task.CompletedTask;
        }
    }
}
