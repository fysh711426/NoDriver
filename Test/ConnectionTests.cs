using NoDriver.Core;
using NoDriver.Core.Runtime;
using System.Net.WebSockets;
using Cdp = NoDriver.Cdp;

namespace Test
{
    [TestClass]
    public class ConnectionTests
    {
        private Browser? _browser = null;
        private Connection? _connection = null;

        [TestInitialize]
        public async Task Setup()
        {
            var config = new Config
            {
                Headless = true,
                AutodiscoverTargets = false
            };
            _browser = await Browser.CreateAsync(config);
            _connection = new Connection(_browser.WebSocketUrl, browser: _browser);
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_connection != null)
                await _connection.DisposeAsync();

            if (_browser != null)
                await _browser.DisposeAsync();
        }

        [TestMethod]
        public async Task ConnectAsync_ShouldEstablishWebSocketConnection()
        {
            // Act
            await _connection!.ConnectAsync();

            // Assert
            Assert.IsFalse(_connection.Closed, "連線建立後，Closed 屬性應為 false");
            Assert.IsNotNull(_connection.WebSocket, "WebSocket 實例不應為 null");
            Assert.AreEqual(WebSocketState.Open, _connection.WebSocket.State, "WebSocket 狀態應該是 Open");
        }

        [TestMethod]
        public async Task DisconnectAsync_ShouldCloseWebSocketConnection()
        {
            // Arrange
            await _connection!.ConnectAsync();

            // Act
            await _connection.DisconnectAsync();

            // Assert
            Assert.IsTrue(_connection.Closed, "呼叫中斷連線後，Closed 屬性應為 true");
            Assert.IsNull(_connection.WebSocket, "中斷連線後，WebSocket 實例應被清空為 null");
        }

        [TestMethod]
        public void AddHandler_AsyncDelegate_ShouldRegisterInHandlersDictionary()
        {
            // Arrange
            var count = _connection!.Handlers.Count;

            // Act
            _connection.AddHandler<Cdp.Target.TargetCreated>(async (e, _) => await Task.Yield());

            // Assert
            // 簡單起見，我們驗證 Handlers 字典裡面有新增資料即可
            Assert.IsTrue(_connection.Handlers.Count == count + 1, "加入非同步 Handler 後，字典不應為空");
        }

        [TestMethod]
        public void AddHandler_SyncDelegate_ShouldRegisterInHandlersDictionary()
        {
            // Arrange
            var count = _connection!.Handlers.Count;

            // Act
            _connection.AddHandler<Cdp.Target.TargetDestroyed>((e, _) => { });

            // Assert
            Assert.IsTrue(_connection.Handlers.Count == count + 1, "加入同步 Handler 後，字典不應為空");
        }

        [TestMethod]
        public void RemoveHandler_AsyncDelegate_ShouldRemoveFromHandlersDictionary()
        {
            // Arrange
            AsyncEventHandler<Cdp.Target.TargetCreated> handler = async (e, _) => await Task.Yield();

            _connection!.AddHandler(handler);
            var count = _connection.Handlers.Values.SelectMany(v => v).Count();

            // Act
            _connection.RemoveHandler(handler);
            var finalCount = _connection.Handlers.Values.SelectMany(v => v).Count();

            // Assert
            Assert.AreEqual(1, count, "新增後應該有 1 個 handler");
            Assert.AreEqual(0, finalCount, "移除後應該剩下 0 個 handler");
        }

        [TestMethod]
        public void RemoveHandler_SyncDelegate_ShouldRemoveFromHandlersDictionary()
        {
            // Arrange
            SyncEventHandler<Cdp.Target.TargetDestroyed> handler = (e, _) => { };
            _connection!.AddHandler(handler);
            var count = _connection.Handlers.Values.SelectMany(v => v).Count();

            // Act
            _connection.RemoveHandler(handler);
            var finalCount = _connection.Handlers.Values.SelectMany(v => v).Count();

            // Assert
            Assert.AreEqual(1, count, "新增後應該有 1 個 handler");
            Assert.AreEqual(0, finalCount, "移除後應該剩下 0 個 handler");
        }

        [TestMethod]
        public async Task SendAsync_ShouldExecuteCommandAndReturnResponse()
        {
            // Arrange
            await _connection!.ConnectAsync();

            // Act: 建立一個真實的 CDP 測試指令
            var response = await _connection.SendAsync(Cdp.Target.GetTargets());

            // Assert
            Assert.IsNotNull(response, "傳送指令後應收到有效回覆");
            Assert.IsNotNull(response.TargetInfos, "TargetInfos 屬性不應為 null");
            Assert.IsTrue(response.TargetInfos.Count > 0, "瀏覽器啟動時應該至少有一個預設 Target");
        }

        [TestMethod]
        public async Task DisposeAsync_ShouldCleanUpResources()
        {
            // Arrange
            await _connection!.ConnectAsync();

            // Act
            await _connection.DisposeAsync();

            // Assert
            Assert.IsTrue(_connection.Closed, "執行 DisposeAsync 後，連線應關閉");
        }

        [TestMethod]
        public async Task Dispose_ShouldCleanUpResourcesSync()
        {
            // Arrange
            await _connection!.ConnectAsync();

            // Act
            _connection.Dispose();

            // Assert
            Assert.IsTrue(_connection.Closed, "執行 Dispose 後，連線應關閉");

            // 防止重複 Dispose
            _connection = null;
        }
    }
}
