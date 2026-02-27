using NoDriver.Core.Helper;
using NoDriver.Core.Interface;
using NoDriver.Core.Message;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NoDriver.Core
{
    public class Connection : IDisposable, IAsyncDisposable
    {
        private static readonly int _receiveBufferSize = 8192 * 4;

        private Task? _listenerTask = null;
        private CancellationTokenSource? _cts = null;
        private int _messageIdCounter = 0;

        public string WebSocketUrl { get; } = "";
        public Browser? Browser { get; } = null;
        public ClientWebSocket? WebSocket { get; private set; } = null;
        public dynamic? Target { get; } = null;

        public ConcurrentDictionary<int, Transaction<ICommand>> Mapper { get; } = new();
        public ConcurrentDictionary<string, List<IDomainEventHandlerWrapper>> Handlers { get; } = new();
        public ConcurrentDictionary<string, byte> EnabledDomains { get; } = new();

        public bool Closed => 
            WebSocket == null || WebSocket.State != WebSocketState.Open;

        public Connection(string webSocketUrl, dynamic? target = null, Browser? browser = null)
        {
            WebSocketUrl = webSocketUrl;
            Target = target;
            Browser = browser;
        }

        public async Task ConnectAsync(CancellationToken token = default)
        {
            if (!Closed)
                return;

            await DisconnectAsync(token);

            _cts = new CancellationTokenSource();
            WebSocket = new ClientWebSocket();
            WebSocket.Options.SetBuffer(_receiveBufferSize, _receiveBufferSize);
            WebSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            
            try
            {
                await WebSocket.ConnectAsync(new Uri(WebSocketUrl), token);
                _listenerTask = Task.Run(async () => await ListenLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during opening of WebSocket: {ex.Message}");
                throw;
            }

            await RegisterHandlersAsync(token);
        }

        public async Task DisconnectAsync(CancellationToken token = default)
        {
            if (WebSocket != null)
            {
                EnabledDomains.Clear();

                if (WebSocket.State == WebSocketState.Open || WebSocket.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        await WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                        if (_listenerTask != null)
                            await _listenerTask.WhenWaitAsync(token);
                    }
                    catch { }
                }

                _cts?.Cancel();
                if (_listenerTask != null)
                    await _listenerTask;
                WebSocket.Dispose();
                WebSocket = null;
                _cts?.Dispose();
                _cts = null;
                foreach (var (_, tx) in Mapper)
                {
                    tx.Cancel(new ObjectDisposedException("Connection closed."));
                }
                Console.WriteLine($"Closed WebSocket connection to {WebSocketUrl}");
            }
        }

        private static string GetMethodName(MemberInfo type) =>
            type.GetCustomAttribute<MethodNameAttribute>()?.MethodName ??
                throw new Exception($"{nameof(MethodNameAttribute)} is required on type {type.Name} but it is not presented.");

        public void AddHandler<TEvent>(AsyncDomainEventHandler<TEvent> handler) where TEvent : IEvent
        {
            if (handler == null) 
                throw new Exception("The event handler cannot be null.");
            AddHandlerInternal(new DomainEventHandlerWrapper<TEvent>(handler));
        }

        public void AddHandler<TEvent>(SyncDomainEventHandler<TEvent> handler) where TEvent : IEvent
        {
            if (handler == null)
                throw new Exception("The event handler cannot be null.");
            AddHandlerInternal(new DomainEventHandlerWrapper<TEvent>(handler));
        }

        private void AddHandlerInternal<TEvent>(DomainEventHandlerWrapper<TEvent> wrapper) where TEvent : IEvent
        {
            var eventName = GetMethodName(typeof(TEvent));
            var list = Handlers.GetOrAdd(eventName, _ => new());
            lock (list)
            {
                list.Add(wrapper);
            }
        }

        public void RemoveHandler<TEvent>(AsyncDomainEventHandler<TEvent> handler) where TEvent : IEvent
        {
            RemoveHandlerInternal<TEvent>(handler);
        }

        public void RemoveHandler<TEvent>(SyncDomainEventHandler<TEvent> handler) where TEvent : IEvent
        {
            RemoveHandlerInternal<TEvent>(handler);
        }

        private void RemoveHandlerInternal<TEvent>(Delegate handler) where TEvent : IEvent
        {
            var eventName = GetMethodName(typeof(TEvent));
            if (Handlers.TryGetValue(eventName, out var list))
            {
                lock (list)
                {
                    if (handler != null)
                        list.RemoveAll(it => it.RawHandler.Equals(handler));
                    else
                        list.Clear();
                }
            }
        }

        private static bool IsDefaultDomain(string domainName)
        {
            return domainName == "Target" || domainName == "Storage" || domainName == "Input";
        }

        private async Task RegisterHandlersAsync(CancellationToken token)
        {
            var activeDomains = new HashSet<string>();

            foreach (var (eventName, list) in Handlers)
            {
                if (list == null || list.Count == 0)
                {
                    Handlers.TryRemove(eventName, out _);
                    continue;
                }

                var domainName = eventName.Split('.')[0];
                if (IsDefaultDomain(domainName))
                    continue;

                activeDomains.Add(domainName);

                if (!EnabledDomains.ContainsKey(domainName))
                {
                    try
                    {
                        if (EnabledDomains.TryAdd(domainName, 1))
                        {
                            Console.WriteLine($"Registered domain: {domainName}.");

                            var type = Type.GetType($"NoDriver.Cdp.{domainName}");
                            if (type == null)
                                throw new TypeLoadException($"Could not resolve type for domain.");

                            var invoker = DomainMethodInvoker.GetInvoker(type, "Enable");
                            var command = invoker();
                            await SendAsync((dynamic)command, true, token);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to enable domain {domainName}. Error: {ex.Message}");
                        EnabledDomains.TryRemove(domainName, out _);
                        activeDomains.Remove(domainName);
                    }
                }
            }

            var enabledDomains = EnabledDomains.ToList();
            foreach (var (domainName, _) in enabledDomains)
            {
                if (IsDefaultDomain(domainName))
                    continue;

                if (!activeDomains.Contains(domainName))
                    EnabledDomains.TryRemove(domainName, out _);
            }
        }

        public async Task<TResponse> SendAsync<TResponse>(
            ICommand<TResponse> command, bool isUpdate = false, CancellationToken token = default) where TResponse : IType
        {
            if (Closed)
                await ConnectAsync(token);

            if (WebSocket?.State != WebSocketState.Open)
                throw new InvalidOperationException("Failed to send command: WebSocket connection is not established.");

            if (!isUpdate)
                await RegisterHandlersAsync(token);

            var id = Interlocked.Increment(ref _messageIdCounter);

            var methodName = GetMethodName(command.GetType());

            var tx = new Transaction<ICommand>(id, methodName, command);

            Mapper[id] = tx;

            try
            {
                var request = new ProtocolRequest<ICommand>
                {
                    Id = id,
                    Method = methodName,
                    Params = command
                };

                var json = JsonSerializer.Serialize(request, JsonProtocolSerialization.Settings);
                var bytes = Encoding.UTF8.GetBytes(json);

                await WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);

                await tx.Task.WhenWaitAsync(token);

                var responseRaw = await tx.Task;
                var response = responseRaw.Deserialize<TResponse>(JsonProtocolSerialization.Settings);
                if (response == null)
                    throw new ArgumentException("Failed to parse protocol response.");
                return response;
            }
            finally
            {
                Mapper.TryRemove(id, out _);
            }
        }

        protected async Task<TResponse> SendOneshotAsync<TResponse>(
            ICommand<TResponse> command, CancellationToken token = default) where TResponse : IType
        {
            return await SendAsync(command, true, token);
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            var buffer = new byte[_receiveBufferSize];
            try
            {
                using (var ms = new MemoryStream())
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (WebSocket == null ||
                            WebSocket.State == WebSocketState.Aborted ||
                            WebSocket.State == WebSocketState.Closed)
                            break;

                        var result = null as WebSocketReceiveResult;
                        do
                        {
                            result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                            if (result.MessageType == WebSocketMessageType.Close)
                                return;

                            ms.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        var message = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

                        ms.Position = 0;
                        ms.SetLength(0);

                        _ = Task.Run(async () => await ProcessMessage(message, token));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving websocket response: {ex.Message}");
            }
        }

        private async Task ProcessMessage(string message, CancellationToken token)
        {
            try
            {
                var node = JsonNode.Parse(message);
                if (node == null)
                    throw new JsonException("Message is null or invalid.");

                if (node["id"] != null)
                {
                    var response = node.Deserialize<ProtocolResponse>(JsonProtocolSerialization.Settings);
                    if (response == null)
                        throw new JsonException("ProtocolResponse is null or invalid.");

                    if (Mapper.TryRemove(response.Id, out var tx))
                        tx.ProcessResponse(response);
                }
                else
                {
                    var @event = node.Deserialize<ProtocolEvent>(JsonProtocolSerialization.Settings);
                    if (@event == null)
                        throw new JsonException("ProtocolEvent is null or invalid.");

                    var eventName = @event.Method;
                    if (Handlers.TryGetValue(eventName, out var list))
                    {
                        var tasks = list.ToList()
                            .Select(it => it.HandleAsync(@event))
                            .ToList();

                        foreach (var task in tasks)
                        {
                            try
                            {
                                await task;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Exception in callback for event {eventName} => {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (JsonException ex)
            {
                Console.WriteLine($"Failed to parse protocol message. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to processing protocol message. Error: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    await DisconnectAsync(timeoutCts.Token);
                }
            }
            catch { }

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                WebSocket?.Dispose();
            }
        }
    }
}
