using NoDriver.Core.Runtime;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Test
{
    [TestClass]
    public class HTTPApiTests
    {
        private Browser? _browser;
        private HTTPApiProxy? _http;

        [TestInitialize]
        public async Task Setup()
        {
            var config = new Config
            {
                Headless = true,
                AutodiscoverTargets = false
            };
            _browser = await Browser.CreateAsync(config);
            _http = new HTTPApiProxy(config.Host!, config.Port!.Value);
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_browser != null)
                await _browser.DisposeAsync();
        }

        [TestMethod]
        public async Task GetVersionAsync_ShouldReturnValidBrowserMetadata()
        {
            // Act
            var response = await _http!.GetAsync("version");

            // Assert
            Assert.IsNotNull(response);
            Assert.IsNotNull(response["Browser"], "回應應包含 Browser 節點");
            Assert.IsNotNull(response["Protocol-Version"], "應包含 CDP 協定版本");
            Assert.IsNotNull(response["webSocketDebuggerUrl"], "應包含用於建立 WebSocket 的 Debugger URL");
        }

        [TestMethod]
        public async Task GetListAsync_ShouldReturnArrayOfTargets()
        {
            // Act
            var response = await _http!.GetAsync("list");

            // Assert
            Assert.IsNotNull(response, "API 回應不應為 null");
            Assert.IsTrue(response.AsArray().Count > 0, "應能成功解析 Target 陣列數量");
        }

        [TestMethod]
        public async Task GetAsync_InvalidEndpoint_ShouldThrowHttpRequestException()
        {
            // Act & Assert
            // 測試我們封裝的 HTTP 客戶端是否有正確處理非 2xx 回應
            await Assert.ThrowsAsync<HttpRequestException>(
                async () => await _http!.GetAsync("this_endpoint_does_not_exist"));
        }
    }

    internal class HTTPApiProxy
    {
        private readonly object _instance;
        private readonly MethodInfo _getAsyncMethod;

        public HTTPApiProxy(string host, int port)
        {
            var type = typeof(Browser).Assembly.GetType("NoDriver.Core.Runtime.HTTPApi");
            if (type == null)
                throw new Exception("找不到 HTTPApi 型別");

            // 建立執行個體
            var instance = Activator.CreateInstance(type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { host, port }, null);
            if (instance == null)
                throw new Exception("無法建立 HTTPApi 執行個體");
            _instance = instance;

            // 取得 GetAsync 方法
            var getAsyncMethod = type.GetMethod("GetAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getAsyncMethod == null)
                throw new Exception("找不到 GetAsync 方法");
            _getAsyncMethod = getAsyncMethod;
        }

        public async Task<JsonNode?> GetAsync(string endpoint)
        {
            var task = (Task<JsonNode?>)_getAsyncMethod.Invoke(_instance, new object[] { endpoint, CancellationToken.None })!;
            return await task;
        }
    }
}
