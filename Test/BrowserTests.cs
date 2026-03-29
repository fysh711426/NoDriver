using NoDriver.Core.Runtime;
using System.Diagnostics;

namespace Test
{
    [TestClass]
    public sealed class BrowserTests
    {
        private static readonly string exampleUrl = "https://www.nowsecure.nl";

        private Browser? _browser = null;

        [TestInitialize]
        public async Task Setup()
        {
            var config = new Config
            {
                Headless = true,
                AutodiscoverTargets = true
            };
            _browser = await Browser.CreateAsync(config);
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_browser != null)
                await _browser.DisposeAsync();
        }

        [TestMethod]
        public void CreateAsync_And_StartAsync_ShouldInitializeBrowserSuccessfully()
        {
            // Assert: 確認啟動後的狀態
            Assert.IsNotNull(_browser, "瀏覽器不應為 null");
            Assert.IsFalse(_browser.Stopped, "瀏覽器不應該是停止狀態");
            Assert.IsFalse(string.IsNullOrWhiteSpace(_browser.WebSocketUrl), "WebSocketUrl 不應為空");
            Assert.IsNotNull(_browser.Info, "瀏覽器 Info 不應為 null");
            Assert.IsNotNull(_browser.Connection, "連線不應為 null");
            Assert.IsNotNull(_browser.Cookies, "Cookies 不應為 null");
        }

        [TestMethod]
        public async Task StartAsync_WhenAlreadyRunning_ShouldReturnSameInstance()
        {
            // Act: 嘗試再次呼叫 StartAsync
            var browser = await _browser!.StartAsync();

            // Assert
            Assert.AreSame(_browser, browser, "當瀏覽器已在運行時，再次啟動應該回傳同一個實例");
            Assert.IsFalse(_browser.Stopped, "瀏覽器不應該因此停止");
        }

        [TestMethod]
        public async Task GetAsync_NavigateCurrentTab_ShouldReturnTab()
        {
            // Act
            var tab = await _browser!.GetAsync(exampleUrl);

            // Assert
            Assert.IsNotNull(tab);
            Assert.IsTrue(_browser.Tabs.Count > 0, "應該至少有一個分頁");
            Assert.IsNotNull(_browser.MainTab, "MainTab 不應為 null");
        }

        [TestMethod]
        public async Task GetAsync_NewTab_ShouldIncreaseTabCount()
        {
            // Arrange
            var initialTabCount = _browser!.Tabs.Count;

            // Act
            var newTab = await _browser.GetAsync(exampleUrl, newTab: true);

            // Assert
            Assert.IsNotNull(newTab);
            Assert.AreEqual(initialTabCount + 1, _browser.Tabs.Count, "分頁數量應該增加 1");
        }

        [TestMethod]
        public async Task WaitAsync_ShouldDelayExecution()
        {
            // Act
            var sw = Stopwatch.StartNew();
            await _browser!.WaitAsync(0.5);
            sw.Stop();

            // Assert
            var elapsed = sw.Elapsed.TotalSeconds;
            Assert.IsTrue(elapsed >= 0.4, "執行時間應大約等於或超過 0.5 秒");
        }

        [TestMethod]
        public async Task CreateContextAsync_ShouldCreateIsolatedContext()
        {
            // Act
            var newTab = await _browser!.CreateContextAsync(exampleUrl, newWindow: true);

            // Assert
            Assert.IsNotNull(newTab);
            Assert.IsTrue(_browser.Tabs.Contains(newTab), "新建立的 Tab 應該存在於瀏覽器的 Tabs 列表中");
        }

        [TestMethod]
        public async Task CreateContextAsync_WithProxy_ShouldExecuteWithoutError()
        {
            // Arrange: 隨便給定一個本機假 Proxy，重點是觸發內部 ProxyForwarder 的建立邏輯
            var fakeProxy = "socks5://127.0.0.1:9999";

            // Act
            var newTab = await _browser!.CreateContextAsync(
                url: "chrome://version",
                newWindow: true,
                proxyServer: fakeProxy);

            // Assert
            Assert.IsNotNull(newTab, "即使帶有 Proxy 參數，也應成功建立新的 Context 與 Tab");
            Assert.IsTrue(_browser.Tabs.Contains(newTab), "新 Tab 應被加入列表中");

            // Cleanup
            await newTab.CloseAsync();
        }

        //[TestMethod]
        //public async Task GrantAllPermissionsAsync_ShouldExecuteWithoutError()
        //{
        //    // Act & Assert
        //    // 只要 CDP 沒拋出例外即算通過。此方法會略過已過時的權限
        //    await _browser!.GrantAllPermissionsAsync();
        //}

        [TestMethod]
        public async Task TileWindowsAsync_ShouldCalculateAndSetGrid()
        {
            // Arrange: 開啟兩個新視窗
            await _browser!.GetAsync("chrome://version", newWindow: true);
            await _browser.GetAsync("chrome://version", newWindow: true);

            // Act
            var grid = await _browser.TileWindowsAsync();

            // Assert
            Assert.IsNotNull(grid);
            Assert.IsTrue(grid.Count > 0, "網格分割結果不應為空");
            Assert.IsTrue(grid.First().Width > 0, "計算出的視窗寬度應大於 0");
        }

        [TestMethod]
        public async Task UpdateTargetsAsync_ShouldSyncTargetsList()
        {
            // Act
            await _browser!.UpdateTargetsAsync();

            // Assert
            Assert.IsTrue(_browser.Targets.Count > 0, "Targets 列表不應為空");
        }

        [TestMethod]
        public async Task TargetLifecycle_CloseTab_ShouldTriggerTargetDestroyedAndRemoveFromList()
        {
            // Arrange
            var newTab = await _browser!.GetAsync("chrome://version", newTab: true);
            await Task.Delay(1000);
            var count = _browser.Targets.Count;

            // Act
            await newTab.CloseAsync();
            // 等待 Chrome 發出 TargetDestroyed 事件並讓 Browser 內部處理
            await Task.Delay(1000);
            var finalCount = _browser.Targets.Count;

            // Assert
            Assert.IsTrue(finalCount < count, "關閉分頁後，Targets 列表的數量應該減少");
            Assert.IsFalse(_browser.Tabs.Contains(newTab), "被關閉的分頁不應再存在於 Tabs 列表中");
        }

        [TestMethod]
        public async Task Indexer_ByIndex_ShouldReturnCorrectTab()
        {
            // Arrange
            await _browser!.GetAsync(exampleUrl, newTab: true);

            // Act
            var firstTab = _browser[0];
            var secondTab = _browser[1];

            // Assert
            Assert.IsNotNull(firstTab);
            Assert.IsNotNull(secondTab);
            Assert.AreNotSame(firstTab, secondTab, "索引 0 和 1 應該回傳不同的 Tab 物件");
        }

        [TestMethod]
        public async Task Indexer_ByStringQuery_ShouldReturnMatchingTab()
        {
            // Arrange: 開啟一個包含特定字串的頁面
            var uniqueUrl = "chrome://dino/";
            await _browser!.GetAsync(uniqueUrl, newTab: true);
            // 等待 CDP 更新 target info 確保序列化後找得到
            await _browser.WaitAsync(1.0);

            // Act
            var tab = _browser["dino"];

            // Assert
            Assert.IsNotNull(tab, "應該要能透過字串查詢找到對應的分頁");
        }

        [TestMethod]
        public void Indexer_ByStringQuery_NotFound_ShouldReturnFirstTab()
        {
            // Arrange
            var firstTab = _browser!.Tabs.First();

            // Act
            // 故意搜尋一個絕對不存在於 TargetInfo 裡的奇怪字串
            var fallbackTab = _browser["this_definitely_does_not_exist_12345"];

            // Assert
            Assert.IsNotNull(fallbackTab);
            Assert.AreSame(firstTab, fallbackTab, "當找不到符合的字串時，依照邏輯應該回傳第一個 Tab");
        }

        [TestMethod]
        public async Task DisposeAsync_ShouldKillProcessAndClearTargets()
        {
            // Arrange
            var config = new Config
            {
                Headless = true,
                AutodiscoverTargets = true
            };
            var browserToDispose = await Browser.CreateAsync(config);

            // Act
            await browserToDispose.DisposeAsync();

            // Assert
            Assert.IsTrue(browserToDispose.Stopped, "執行 Dispose 後瀏覽器狀態應為已停止");
            Assert.AreEqual(0, browserToDispose.Targets.Count, "執行 Dispose 後 Targets 應被清空");
        }

        [TestMethod]
        public async Task Dispose_ShouldKillProcessAndClearTargets()
        {
            // Arrange
            var config = new Config
            {
                Headless = true,
                AutodiscoverTargets = true
            };
            var browserToDispose = await Browser.CreateAsync(config);

            // Act
            browserToDispose.Dispose();

            // Assert
            Assert.IsTrue(browserToDispose.Stopped, "執行 Dispose 後瀏覽器狀態應為已停止");
            Assert.AreEqual(0, browserToDispose.Targets.Count, "執行 Dispose 後 Targets 應被清空");
        }
    }
}
