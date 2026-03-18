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
            Assert.IsNotNull(_browser);
            Assert.IsFalse(_browser.Stopped, "瀏覽器不應該是停止狀態");
            Assert.IsFalse(string.IsNullOrWhiteSpace(_browser.WebSocketUrl), "WebSocketUrl 不應為空");
            Assert.IsNotNull(_browser.Info, "瀏覽器 Info 不應為 null");
            Assert.IsNotNull(_browser.Connection, "連線不應為 null");
            Assert.IsNotNull(_browser.Cookies, "Cookies 不應為 null");
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
        public async Task CreateContextAsync_ShouldCreateIsolatedContext()
        {
            // Act
            var newTab = await _browser!.CreateContextAsync(exampleUrl, newWindow: true);

            // Assert
            Assert.IsNotNull(newTab);
            Assert.IsTrue(_browser.Tabs.Contains(newTab), "新建立的 Tab 應該存在於瀏覽器的 Tabs 列表中");
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

        //[TestMethod]
        //public async Task GrantAllPermissionsAsync_ShouldExecuteWithoutError()
        //{
        //    // Act & Assert
        //    // 在 MSTest 中，如果非同步方法沒有拋出例外，就代表測試通過
        //    await _browser!.GrantAllPermissionsAsync();
        //}

        [TestMethod]
        public async Task GetScreenResolutionAsync_ShouldReturnValidDimensions()
        {
            // Act
            var resolution = await _browser!.GetScreenResolutionAsync();

            // Assert
            // 在 Headless 模式下可能會有預設解析度 (如 800x600)
            Assert.IsNotNull(resolution, "無法取得螢幕解析度");
            Assert.IsTrue(resolution.Value.Width > 0, "寬度應大於 0");
            Assert.IsTrue(resolution.Value.Height > 0, "高度應大於 0");
        }

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
        public async Task DisposeAsync_ShouldKillProcessAndClearTargets()
        {
            // Arrange
            var config = new Config { Headless = true };
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
            var config = new Config { Headless = true };
            var browserToDispose = await Browser.CreateAsync(config);

            // Act
            browserToDispose.Dispose();

            // Assert
            Assert.IsTrue(browserToDispose.Stopped, "執行 Dispose 後瀏覽器狀態應為已停止");
            Assert.AreEqual(0, browserToDispose.Targets.Count, "執行 Dispose 後 Targets 應被清空");
        }
    }
}
