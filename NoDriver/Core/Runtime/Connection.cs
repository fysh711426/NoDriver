using Microsoft.Extensions.Logging;
using NoDriver.Core.Messaging;
using NoDriver.Core.Tools;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NoDriver.Core.Runtime
{
    public class Connection : IDisposable, IAsyncDisposable
    {
        private static readonly int _receiveBufferSize = 8192 * 4;

        private int _messageIdCounter = 0;
        private CancellationTokenSource? _cts = null;
        private Task _listenerTask = Task.CompletedTask;

        protected readonly ILogger? _logger;

        public string WebSocketUrl { get; } = "";
        public Browser? Browser { get; set; } = null;
        public ClientWebSocket? WebSocket { get; private set; } = null;
        public Cdp.Target.TargetInfo? Target { get; set; } = null;
        
        public ConcurrentDictionary<int, Transaction<ICommand>> Mapper { get; } = new();
        public ConcurrentDictionary<string, List<IEventHandlerWrapper>> Handlers { get; } = new();
        public ConcurrentDictionary<string, byte> EnabledDomains { get; } = new();

        public bool Closed => 
            WebSocket == null || WebSocket.State != WebSocketState.Open;

        public Connection(string webSocketUrl, Cdp.Target.TargetInfo? target = null, Browser? browser = null, ILogger? logger = null)
        {
            _logger = logger;
            WebSocketUrl = webSocketUrl;
            Target = target;
            Browser = browser;
        }

        /// <summary>
        /// Opens the websocket connection. should not be called manually by users.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
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
                _listenerTask = ListenLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"Exception during opening of WebSocket: {ex.Message}");
                throw;
            }

            await RegisterHandlersAsync(token);
        }

        /// <summary>
        /// Closes the websocket connection. should not be called manually by users.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
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
                        await _listenerTask.WhenWaitAsync(token);
                    }
                    catch { }
                }

                _cts?.Cancel();
                await _listenerTask;
                WebSocket.Dispose();
                WebSocket = null;
                _cts?.Dispose();
                _cts = null;
                foreach (var (_, tx) in Mapper)
                    tx.Cancel(new ObjectDisposedException("Connection closed."));
                _logger?.LogDebug($"Closed WebSocket connection to {WebSocketUrl}");
            }
        }

        private static string GetMethodName(MemberInfo type) =>
            type.GetCustomAttribute<MethodNameAttribute>()?.MethodName ??
                throw new Exception($"{nameof(MethodNameAttribute)} is required on type {type.Name} but it is not presented.");

        /// <summary>
        /// Add a handler for given event.<br/>
        /// <br/>
        /// If you want to receive event updates (network traffic are also 'events') you can add handlers for those events.<br/>
        /// handlers can be regular callback functions or async coroutine functions (and also just lamba's).<br/>
        /// for example, you want to check the network traffic:<br/>
        /// <br/>
        /// .. code-block::<br/>
        /// <br/>
        ///     tab.AddHandler&lt;Cdp.Network.RequestWillBeSent&gt;((e, _) =&gt; Console.WriteLine($"network event =&gt; {e.Request}"))<br/>
        /// <br/>
        /// The next time you make network traffic you will see your console print like crazy.
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="handler"></param>
        /// <exception cref="Exception"></exception>
        public void AddHandler<TEvent>(AsyncEventHandler<TEvent> handler) where TEvent : IEvent
        {
            if (handler == null) 
                throw new Exception("The event handler cannot be null.");
            AddHandlerInternal(new EventHandlerWrapper<TEvent>(handler));
        }

        /// <summary>
        /// Add a handler for given event.<br/>
        /// <br/>
        /// If you want to receive event updates (network traffic are also 'events') you can add handlers for those events.<br/>
        /// handlers can be regular callback functions or async coroutine functions (and also just lamba's).<br/>
        /// for example, you want to check the network traffic:<br/>
        /// <br/>
        /// .. code-block::<br/>
        /// <br/>
        ///     tab.AddHandler&lt;Cdp.Network.RequestWillBeSent&gt;((e, _) =&gt; Console.WriteLine($"network event =&gt; {e.Request}"))<br/>
        /// <br/>
        /// The next time you make network traffic you will see your console print like crazy.
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="handler"></param>
        /// <exception cref="Exception"></exception>
        public void AddHandler<TEvent>(SyncEventHandler<TEvent> handler) where TEvent : IEvent
        {
            if (handler == null)
                throw new Exception("The event handler cannot be null.");
            AddHandlerInternal(new EventHandlerWrapper<TEvent>(handler));
        }

        private void AddHandlerInternal<TEvent>(EventHandlerWrapper<TEvent> wrapper) where TEvent : IEvent
        {
            var eventName = GetMethodName(typeof(TEvent));
            var list = Handlers.GetOrAdd(eventName, _ => new());
            lock (list)
            {
                list.Add(wrapper);
            }
        }

        /// <summary>
        /// Remove a handler for given event.
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="handler"></param>
        public void RemoveHandler<TEvent>(AsyncEventHandler<TEvent> handler) where TEvent : IEvent
        {
            RemoveHandlerInternal<TEvent>(handler);
        }

        /// <summary>
        /// Remove a handler for given event.
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="handler"></param>
        public void RemoveHandler<TEvent>(SyncEventHandler<TEvent> handler) where TEvent : IEvent
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

        /// <summary>
        /// Ensure that for current (event) handlers, the corresponding<br/>
        /// domain is enabled in the protocol.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="TypeLoadException"></exception>
        private async Task RegisterHandlersAsync(CancellationToken token)
        {
            // 可優化: 將 Domain 的啟用邏輯從 SendAsync 移到 AddHandler
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
                            _logger?.LogDebug($"Registered domain: {domainName}.");

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
                        _logger?.LogDebug($"Failed to enable domain {domainName}. Error: {ex.Message}");
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

        /// <summary>
        /// Send a protocol command. the commands are made using any of the Cdp.&lt;Domain&gt;.&lt;Method&gt;()'s<br/>
        /// and is used to send custom cdp commands as well.
        /// </summary>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="command">The generator object created by a cdp method.</param>
        /// <param name="isUpdate">Internal flag<br/>
        /// Prevents infinite loop by skipping the registeration of handlers<br/>
        /// when multiple calls to connection.SendAsync() are made.</param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
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

            if (!Mapper.TryAdd(id, tx))
                throw new InvalidOperationException($"Failed to create transaction. Duplicate message ID: {id}");

            try
            {
                var request = new ProtocolRequest
                {
                    Id = id,
                    Method = methodName,
                    Params = command
                };

                try
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonProtocolSerialization.Settings);

                    await WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);

                    await tx.Task.WhenWaitAsync(token);
                }
                catch
                {
                    tx.Cancel(new ObjectDisposedException("Transaction canceled."));
                    throw;
                }

                var responseRaw = await tx.Task;
                if (responseRaw == null)
                    throw new InvalidOperationException("Protocol response cannot be null.");

                var response = responseRaw.Deserialize<TResponse>(JsonProtocolSerialization.Settings);
                if (response == null)
                    throw new InvalidOperationException("Failed to parse protocol response.");
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
            var buffer = ArrayPool<byte>.Shared.Rent(_receiveBufferSize);
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

                        // 可優化: 直接將 ms 轉為 JsonNode
                        var message = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

                        ms.Position = 0;
                        ms.SetLength(0);

                        _ = Task.Run(() => ProcessMessage(message, token));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception ex)
            {
                _logger?.LogInformation($"Error receiving websocket response: {ex.Message}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task ProcessMessage(string message, CancellationToken token)
        {
            try
            {
                using (var doc = JsonDocument.Parse(message, JsonProtocolSerialization.DocSettings))
                {
                    var node = doc.RootElement;
                    if (node.TryGetProperty("id", out _))
                    {
                        var response = node.Deserialize<ProtocolResponse>(JsonProtocolSerialization.Settings);
                        if (response == null)
                            throw new JsonException("ProtocolResponse is null or invalid.");

                        if (Mapper.TryRemove(response.Id, out var tx))
                        {
                            tx.ProcessResponse(response);
                            _logger?.LogTrace($"Got answer for (message_id:{tx.Id}) => {message}");
                        }
                    }
                    else
                    {
                        var @event = node.Deserialize<ProtocolEvent>(JsonProtocolSerialization.Settings);
                        if (@event == null)
                            throw new JsonException("ProtocolEvent is null or invalid.");

                        var eventName = @event.Method;
                        if (Handlers.TryGetValue(eventName, out var list))
                        {
                            var handlers = new List<IEventHandlerWrapper>();
                            lock (list)
                            {
                                handlers = list.ToList();
                            }
                            var tasks = handlers
                                .Select(it => it.HandleAsync(@event, this))
                                .ToList();

                            foreach (var task in tasks)
                            {
                                try
                                {
                                    await task;
                                }
                                catch (OperationCanceledException) { }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning($"Exception in callback for event {eventName} => {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (JsonException ex)
            {
                _logger?.LogInformation($"Failed to parse protocol message. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger?.LogInformation($"Failed to processing protocol message. Error: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            try
            {
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    await DisconnectAsync(timeoutCts.Token);
                }
            }
            catch { }
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
                _cts = null;
                WebSocket?.Dispose();
                foreach (var (_, tx) in Mapper)
                    tx.Cancel(new ObjectDisposedException("Connection disposed."));
            }
        }
    }
}
