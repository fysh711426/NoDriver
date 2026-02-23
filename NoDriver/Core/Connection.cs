using NoDriver.Core.Message;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoDriver.Core
{
    public class ProtocolException : Exception
    {
        public int? Code { get; }

        public ProtocolException(string message, int? code = null) 
            : base(message)
        {
            Code = code;
        }

        public override string ToString()
        {
            if (Code != null)
                return $"{Message} [code: {Code}]";
            return Message;
        }
    }

    public class SettingClassVarNotAllowedException : Exception
    {
        public SettingClassVarNotAllowedException(string message)
            : base(message) { }
    }

    internal class Transaction
    {
        protected readonly TaskCompletionSource<object?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int? Id { get; } = null;
        public string? Method { get; } = null;
        public object? Params { get; } = null;

        public Task<object?> Task => _tcs.Task;

        protected Transaction() 
        {
        }

        public Transaction(string method, object? @params)
        {
            Method = method;
            Params = @params;
        }

        public string Message => JsonSerializer.Serialize(new
        {
            Method,
            Params,
            Id
        });

        public bool HasException => _tcs.Task.IsFaulted;
        
        public virtual void ProcessResponse(JsonElement response)
        {
            if (response.TryGetProperty("error", out var error))
            {
                _tcs.SetException(new ProtocolException(error));
                return;
            }

            try
            {
                if (response.TryGetProperty("result", out var result))
                {
                    _tcs.SetResult(result);
                }
                else
                {
                    _tcs.SetResult(null);
                }
            }
            catch (Exception ex)
            {
                _tcs.SetException(ex);
            }
        }

        public override string ToString()
        {
            var isDone = _tcs.Task.IsCompleted;
            var success = isDone && HasException ? false : true;

            var status = "";
            if (isDone)
                status = "finished";
            else
                status = "pending";
            
            return $"<{GetType().Name}\n\t" +
                   $"Method: {Method}\n\t" +
                   $"Status: {status}\n\t" +
                   $"Success: {success}>";
        }
    }

    internal class EventTransaction : Transaction
    {
        public object EventValue { get; }

        public EventTransaction(object eventObject) : base()
        {
            _tcs.SetResult(eventObject);
            EventValue = eventObject;
        }

        public override string ToString()
        {
            var status = "finished";
            var success = !HasException;
            var type = EventValue.GetType();

            return $"<{GetType().Name}\n\t" +
                   $"Event: {type.Namespace}.{type.Name}\n\t" +
                   $"Status: {status}\n\t" +
                   $"Success: {success}>";
        }
    }

    public class Connection : IDisposable, IAsyncDisposable
    {
        private static readonly int _receiveBufferSize = 8192 * 4;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private Task? _listenerTask = null;
        private CancellationTokenSource? _cts = null;
        private int _messageIdCounter = 0;

        public string WebSocketUrl { get; } = "";
        public Browser? Browser { get; } = null;
        public ClientWebSocket? WebSocket { get; private set; } = null;
        public dynamic? Target { get; } = null;

        public ConcurrentDictionary<int, ITransaction> Mapper { get; } = new();
        public ConcurrentDictionary<string, List<Delegate>> Handlers { get; } = new();
        public HashSet<string> EnabledDomains { get; } = new();

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
                _listenerTask = Task.Run(() => 
                    ListenLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during opening of WebSocket: {ex.Message}");
                throw;
            }

            //await self._register_handlers()
        }

        public async Task DisconnectAsync(CancellationToken token = default)
        {
            if (WebSocket != null)
            {
                _enabledDomains.Clear();

                if (WebSocket.State == WebSocketState.Open || WebSocket.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        await WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                        if (_listenerTask != null)
                            await Task.WhenAny(_listenerTask, Task.Delay(Timeout.Infinite, token));
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
                Console.WriteLine($"Closed WebSocket connection to {WebSocketUrl}");
            }
        }

        // --- 事件註冊機制 ---

        public void RegisterHandler<TEvent>(string eventName, Action<TEvent> handler)
        {
            var list = _handlers.GetOrAdd(eventName, _ => new List<Delegate>());
            lock (list) { list.Add(handler); }
        }

        public void RegisterHandler<TEvent>(string eventName, Func<TEvent, Task> asyncHandler)
        {
            var list = _handlers.GetOrAdd(eventName, _ => new List<Delegate>());
            lock (list) { list.Add(asyncHandler); }
        }

        public void RemoveHandler(string eventName, Delegate handler = null)
        {
            if (_handlers.TryGetValue(eventName, out var list))
            {
                lock (list)
                {
                    if (handler != null) list.Remove(handler);
                    else list.Clear();
                }
            }
        }

        // --- 核心發送與接收邏輯 ---

        // 動態支援 Python `send(cdp_obj)` 的封裝方式
        public async Task<dynamic> SendAsync(dynamic cdpCmd, bool isUpdate = false, CancellationToken token = default)
        {
            // 這裡假設 cdpCmd 是一個包含 Method 與 Params 的物件
            return await SendAsync<JsonElement>((string)cdpCmd.Method, (object)cdpCmd.Params, isUpdate, token);
        }

        // 強型別的發送邏輯
        public async Task<T> SendAsync<T>(string method, object parameters = null, bool isUpdate = false, CancellationToken token = default)
        {
            if (Closed)
                await ConnectAsync(token);

            if (WebSocket?.State != WebSocketState.Open)
                throw new InvalidOperationException("Failed to send command: WebSocket connection is not established.");

            //if (!isUpdate)
            //    await self._register_handlers()

            var id = Interlocked.Increment(ref _messageIdCounter);
            var tx = new Transaction<T> { Id = id, Method = method, Params = parameters };
            Mapper[id] = tx;

            var payload = new
            {
                id = id,
                method = method,
                @params = parameters
            };

            var jsonOptions = new JsonSerializerOptions()
            { 
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull 
            };
            var json = JsonSerializer.Serialize(payload, jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            await WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);

            var txTask = await Task.WhenAny(tx.Task, Task.Delay(Timeout.Infinite, token));
            return await txTask;
        }

        public async Task<T> SendOneshotAsync<T>(string method, object parameters = null, CancellationToken token = default)
        {
            return await SendAsync<T>(method, parameters, true, token);
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

                        await ProcessMessage(ms, token);

                        ms.Position = 0;
                        ms.SetLength(0);
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

        private async Task ProcessMessage(MemoryStream ms, CancellationToken token)
        {
            try
            {
                using (var doc = JsonDocument.Parse(ms.GetBuffer().AsMemory(0, (int)ms.Length)))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("id", out _))
                    {
                        var response = root.Deserialize<ProtocolResponse>(_jsonOptions);
                        if (response == null)
                            throw new JsonException("ProtocolResponse is null or invalid.");
                    }
                    else
                    {
                        var @event = root.Deserialize<ProtocolEvent>(_jsonOptions);
                        if (@event == null)
                            throw new JsonException("ProtocolEvent is null or invalid.");
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


            // 判斷是 Command Response 還是 Event
            if (message.TryGetProperty("id", out var idElement))
            {
                int id = idElement.GetInt32();
                if (_mapper.TryRemove(id, out var tx))
                {
                    if (message.TryGetProperty("error", out var errorElement))
                    {
                        tx.SetError(errorElement);
                    }
                    else if (message.TryGetProperty("result", out var resultElement))
                    {
                        tx.SetResult(resultElement);
                    }
                    else
                    {
                        // 某些沒有 Result 的空物件
                        tx.SetResult(JsonDocument.Parse("{}").RootElement);
                    }
                }
            }
            else if (message.TryGetProperty("method", out var methodElement))
            {
                // 這是一個事件 (Event)
                string eventName = methodElement.GetString();
                JsonElement paramsElement = message.TryGetProperty("params", out var p) ? p : default;

                if (_handlers.TryGetValue(eventName, out var callbacks))
                {
                    Delegate[] delegatesToInvoke;
                    lock (callbacks) { delegatesToInvoke = callbacks.ToArray(); }

                    foreach (var callback in delegatesToInvoke)
                    {
                        try
                        {
                            // 根據 Delegate 期待的參數型別進行反序列化並觸發
                            var paramType = callback.Method.GetParameters()[0].ParameterType;
                            object arg = paramType == typeof(JsonElement) ? paramsElement : paramsElement.Deserialize(paramType);

                            if (callback is Delegate d && d.Method.ReturnType == typeof(Task))
                            {
                                // Async 處理程序 (Fire and Forget，對應 Python create_task)
                                _ = ((Task)d.DynamicInvoke(arg)).ContinueWith(t =>
                                {
                                    if (t.IsFaulted) Console.WriteLine($"Event async callback error: {t.Exception}");
                                });
                            }
                            else
                            {
                                // Sync 處理程序
                                callback.DynamicInvoke(arg);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Exception in callback for event {eventName} => {ex}");
                        }
                    }
                }
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







    public class Connection : IDisposable
    {
        private ClientWebSocket _websocket;
        private readonly string _websocketUrl;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JToken>> _mapper = new();
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly List<object> _enabledDomains = new();
        private CancellationTokenSource _listenerCts;
        private int _count = 0;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public bool Attached { get; set; }
        public object Target { get; private set; } // 對應 cdp.target
        public object Browser { get; private set; } // 對應 _browser.Browser

        public Connection(string websocketUrl, object target = null, object browser = null)
        {
            _websocketUrl = websocketUrl;
            Target = target;
            Browser = browser;
        }

        public bool IsClosed => _websocket == null || _websocket.State != WebSocketState.Open;

        public void AddHandler<T>(Action<T, Connection> handler)
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type))
                _handlers[type] = new List<Delegate>();

            _handlers[type].Add(handler);
        }

        public void RemoveHandler(Type eventType)
        {
            if (_handlers.ContainsKey(eventType))
                _handlers.Remove(eventType);
        }
        public async Task ConnectAsync()
        {
            if (IsClosed)
            {
                _websocket = new ClientWebSocket();
                await _websocket.ConnectAsync(new Uri(_websocketUrl), CancellationToken.None);

                _listenerCts = new CancellationTokenSource();
                _ = Task.Run(() => ListenLoop(_listenerCts.Token)); // 啟動背景監聽

                await RegisterHandlersAsync();
            }
        }

        //public async Task DisconnectAsync()
        //{
        //    _listenerCts?.Cancel();
        //    if (_websocket != null)
        //    {
        //        await _websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        //        _websocket.Dispose();
        //        _websocket = null;
        //    }
        //    _enabledDomains.Clear();
        //}

        private async Task ListenLoop(CancellationToken token)
        {
            var buffer = new byte[1024 * 1024]; // 1MB buffer
            try
            {
                while (!token.IsCancellationRequested && _websocket.State == WebSocketState.Open)
                {
                    var result = await _websocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    var rawJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var message = JObject.Parse(rawJson);

                    if (message.ContainsKey("id"))
                    {
                        // 處理 Command 回傳
                        int id = message["id"].Value<int>();
                        if (_mapper.TryRemove(id, out var tcs))
                        {
                            tcs.SetResult(message);
                        }
                    }
                    else
                    {
                        // 處理事件 (Events)
                        await DispatchEvent(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket Error: {ex.Message}");
            }
        }

        private async Task RegisterHandlersAsync()
        {
            // 1. 複製目前的 domain 列表，用來比對哪些 domain 不再需要 (雖然 CDP 通常不建議隨便 disable)
            var currentEnabled = _enabledDomains.ToList();
            var domainsToKeep = new List<string>();

            // 2. 遍歷所有已註冊的 Handler
            // 注意：Python 的 self.handlers.copy() 是為了執行緒安全
            Dictionary<Type, List<Delegate>> handlersCopy;
            lock (_handlers)
            {
                handlersCopy = new Dictionary<Type, List<Delegate>>(_handlers);
            }

            foreach (var entry in handlersCopy)
            {
                Type eventType = entry.Key;
                if (entry.Value.Count == 0) continue;

                // 3. 獲取該事件所屬的 Domain 名稱
                // 假設你的 CDP 類別結構是 CDP.Network.RequestWillBeSent
                string domainName = GetDomainNameFromType(eventType);

                if (string.IsNullOrEmpty(domainName)) continue;

                // 預設啟用的 Domain (對應 Python 的 target, storage, input_)
                if (domainName == "Target" || domainName == "Storage" || domainName == "Input")
                    continue;

                if (!_enabledDomains.Contains(domainName))
                {
                    try
                    {
                        logger.Debug($"Registering domain: {domainName}");
                        _enabledDomains.Add(domainName);

                        // 4. 發送啟動指令，例如 "Network.enable"
                        // _is_update = true 避免遞迴呼叫
                        await SendAsync($"{domainName}.enable", null, isUpdate: true);
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Failed to enable domain {domainName}", ex);
                        _enabledDomains.Remove(domainName);
                    }
                }

                domainsToKeep.Add(domainName);
            }

            // 5. 清理不再有 Handler 訂閱的 Domain (可選，視你的需求而定)
            foreach (var domain in currentEnabled)
            {
                if (!domainsToKeep.Contains(domain))
                {
                    // 在某些自動化情境，我們會在這裡呼叫 domain.disable
                    _enabledDomains.Remove(domain);
                }
            }
        }

        /// <summary>
        /// 輔助方法：從類別型別推導 CDP Domain 名稱
        /// </summary>
        private string GetDomainNameFromType(Type type)
        {
            // 方案 A：從命名空間解析 (例如 MyProject.CDP.Network.Event -> Network)
            var nsParts = type.Namespace?.Split('.');
            if (nsParts?.Length >= 2)
            {
                return nsParts[nsParts.Length - 1];
            }

            // 方案 B：如果類別有名稱慣例 (例如 NetworkRequestEvent)
            return type.Name.Replace("Event", "");
        }
        public async Task<JToken> SendAsync(string method, object parameters = null, bool isUpdate = false)
        {
            if (IsClosed) await ConnectAsync();
            if (!isUpdate) await RegisterHandlersAsync();

            int id = Interlocked.Increment(ref _count);
            var tcs = new TaskCompletionSource<JToken>();
            _mapper[id] = tcs;

            var request = new
            {
                id,
                method,
        params = parameters
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(request);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await _websocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

            return await tcs.Task;
        }
        public void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
