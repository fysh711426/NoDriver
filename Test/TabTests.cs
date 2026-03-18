using NoDriver.Core.Runtime;

namespace Test
{
    [TestClass]
    public class TabTests
    {
        // 建立一個穩定的本地測試用 HTML 內容
        private static readonly string _testHtml =
            "data:text/html,<html><body>" +
            "<h1 id='title'>Test Page</h1>" +
            "<p class='content'>This is a <b>test</b> paragraph.</p>" +
            "<a href='https://example.com'>External Link</a>" +
            "<button id='btn' onclick='document.body.style.backgroundColor=\"red\"'>Click Me</button>" +
            "</body></html>";

        //private static readonly string _testHtml =
        //    "data:text/html,<html><head>" +
        //    "<link rel='stylesheet' href='/css/main.css'>" +
        //    "<script src='https://cdn.example.com/library.js'></script>" +
        //    "</head><body>" +
        //    "<h1 id='title'>Test Page</h1>" +
        //    "<p class='content'>This is a <b>test</b> paragraph.</p>" +
        //    "<a href='https://example.com'>External Link</a>" +
        //    "<button id='btn' onclick='document.body.style.backgroundColor=\"red\"'>Click Me</button></br></br>" +
        //    "<a href='subpage/index.html'>Relative Link</a>" +
        //    "<a href='//google.com'>Protocol Relative</a>" +
        //    "<a href='/about/company'>Root Relative Link</a>" +
        //    "<a href='/home'>Home</a>" +
        //    "<img src='../images/logo.png' alt='Logo'>" +
        //    "<a href='javascript:void(0)' id='js-link'>JS Void</a>" +
        //    "<a href='#anchor'>Internal Anchor (Should be ignored)</a>" +
        //    "<script src='inline.js'></script>" +
        //    "</body></html>";

        private Browser? _browser = null;
        private Tab? _tab = null;

        [TestInitialize]
        public async Task Setup()
        {
            var config = new Config
            {
                //Headless = true,
                AutodiscoverTargets = false
            };
            _browser = await Browser.CreateAsync(config);
            _tab = _browser.MainTab;

            await _tab!.GetAsync(_testHtml);
            await _tab.WaitAsync(0.5);
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_browser != null)
                await _browser.DisposeAsync();
        }

        [TestMethod]
        public void InspectorUrl_ShouldReturnValidUrlString()
        {
            // Act & Assert
            Assert.IsFalse(string.IsNullOrWhiteSpace(_tab!.InspectorUrl));
            Assert.IsTrue(_tab.InspectorUrl.Contains("devtools/inspector.html"));
        }

        //[TestMethod]
        //public void OpenExternalInspector_ShouldNotCrash()
        //{
        //    // Act & Assert
        //    // 若本機沒有註冊對應的殼層應用程式可能會拋錯
        //    _tab!.OpenExternalInspector();
        //}

        [TestMethod]
        public async Task PrepareHeadlessAsync_ShouldExecuteWithoutError()
        {
            // Act
            await _tab!.PrepareHeadlessAsync();
            var (remoteObj, _) = await _tab.EvaluateAsync("navigator.userAgent", returnByValue: true);
            var ua = remoteObj.Value?.GetValue<string>();

            // Assert
            Assert.IsFalse(ua!.Contains("Headless"), "執行後 UserAgent 不應包含 Headless 字眼");
        }

        [TestMethod]
        public async Task PrepareExpertAsync_ShouldExecuteWithoutError()
        {
            // Act & Assert: 只要 CDP 沒拋出例外即算通過
            await _tab!.PrepareExpertAsync();
        }

        [TestMethod]
        public async Task GetAsync_NavigateCurrentTab_ShouldSuccess()
        {
            // Act
            var resultTab = await _tab!.GetAsync("chrome://version");

            // Assert
            Assert.IsNotNull(resultTab);
            Assert.AreEqual(_tab, resultTab, "同分頁導覽應回傳自身");
        }

        [TestMethod]
        public async Task GetAsync_NewWindow_ShouldReturnNewTab()
        {
            // Act
            var resultTab = await _tab!.GetAsync("chrome://version", newWindow: true);

            // Assert
            Assert.IsNotNull(resultTab);
            Assert.AreNotEqual(_tab, resultTab, "開啟新視窗時，應回傳新的 Tab 實例");
        }

        [TestMethod]
        public async Task FindAsync_ByText_ShouldReturnElement()
        {
            // Act
            var element = await _tab!.FindAsync("Test Page");

            // Assert
            Assert.IsNotNull(element, "應該要能透過純文字找到元素");
        }

        [TestMethod]
        public async Task SelectAsync_ById_ShouldReturnElement()
        {
            // Act
            var element = await _tab!.SelectAsync("#title");

            // Assert
            Assert.IsNotNull(element, "應該要能透過 CSS Selector 找到 id 為 title 的元素");
        }

        [TestMethod]
        public async Task SelectAllAsync_ByTag_ShouldReturnMultipleElements()
        {
            // Act
            var elements = await _tab!.SelectAllAsync("p, h1, a, button");

            // Assert
            Assert.IsTrue(elements.Count >= 4, "應該找到多個標籤元素");
        }

        [TestMethod]
        public async Task XPathAsync_ShouldReturnElement()
        {
            // Act
            var elements = await _tab!.XPathAsync("//h1[@id='title']");

            // Assert
            Assert.IsTrue(elements.Count > 0, "應該要能透過 XPath 找到元素");
        }

        [TestMethod]
        public async Task WaitForAsync_WithSelector_ShouldWaitUntilElementExists()
        {
            // Arrange: 動態在 1 秒後加入一個元素來測試等待機制
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                await _tab!.EvaluateAsync("document.body.innerHTML += '<div id=\"delayed\">Here</div>';");
            });

            // Act
            var element = await _tab!.WaitForAsync(selector: "#delayed", timeout: 5);

            // Assert
            Assert.IsNotNull(element, "WaitForAsync 應該要等到元素出現才回傳");
        }

        [TestMethod]
        public async Task WaitForAsync_WithText_ShouldWaitUntilElementExists()
        {
            // Arrange: 動態在 1 秒後加入一個元素來測試等待機制
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                await _tab!.EvaluateAsync("document.body.innerHTML += '<div id=\"delayed\">Here</div>';");
            });

            // Act
            var element = await _tab!.WaitForAsync(text: "Here", timeout: 5);

            // Assert
            Assert.IsNotNull(element, "WaitForAsync 應該要等到元素出現才回傳");
        }

        [TestMethod]
        public async Task BackAsync_And_ForwardAsync_ShouldExecute()
        {
            // Act
            await _tab!.GetAsync("chrome://version");

            // Assert: 只要 CDP 沒拋出例外即算通過
            await _tab.BackAsync();
            await _tab.ForwardAsync();
        }

        [TestMethod]
        public async Task ReloadAsync_ShouldNotThrowException()
        {
            // Act & Assert: 只要 CDP 沒拋出例外即算通過
            await _tab!.ReloadAsync();
            await _tab.WaitAsync(0.5);
        }

        [TestMethod]
        public async Task EvaluateAsync_ShouldReturnCalculatedResult()
        {
            // Act
            var (remoteObj, exception) = await _tab!.EvaluateAsync("10 + 20", returnByValue: true);

            // Assert
            Assert.IsNull(exception, "執行 JS 不應發生例外");
            Assert.AreEqual(30, remoteObj.Value?.GetValue<int>(), "JS 計算結果應為 30");
        }

        [TestMethod]
        public async Task JsDumpsAsync_ShouldDumpWindowObject()
        {
            // Act
            var (remoteObj, exception) = await _tab!.JsDumpsAsync("document.location");
            var json = remoteObj.Value?.ToString();

            // Assert
            Assert.IsNull(exception);
            Assert.IsNotNull(remoteObj);
            Assert.IsFalse(string.IsNullOrWhiteSpace(json), "Dump 出來的內容不應為空");
            Assert.IsTrue(json.Contains("href"), "Dump 結果應包含 href 屬性");
        }

        [TestMethod]
        public async Task JsDumpsAsync_DeepObject_ShouldObeyDepthLimit()
        {
            // Arrange
            // 在瀏覽器定義一個深層物件 a.b.c.d
            await _tab!.EvaluateAsync("window.deepObj = { a: { b: { c: { d: 1 } } } }");

            // Act
            var (remoteObj, _) = await _tab.JsDumpsAsync("window.deepObj");
            var json = remoteObj.Value?.ToString();

            // Assert
            Assert.IsTrue(json!.Contains(@"""a"""), "因為限制深度為 2，所以應該看得到 a");
            Assert.IsTrue(json!.Contains(@"""b"""), "因為限制深度為 2，所以應該看得到 b");
            Assert.IsFalse(json!.Contains(@"""c"""), "深度限制為 2，不應抓到第三層屬性 c");
        }

        [TestMethod]
        public async Task GetContentAsync_ShouldReturnHtmlString()
        {
            // Act
            var html = await _tab!.GetContentAsync();

            // Assert
            Assert.IsTrue(html.Contains("Test Page"), "取回的 HTML 應包含測試字串");
            Assert.IsTrue(html.Contains("</html>"), "取回的 HTML 應包含完整的結束標籤");
        }

        [TestMethod]
        public async Task SetWindowSizeAsync_And_Maximize_ShouldNotThrow()
        {
            // Act & Assert
            // 只要 CDP 沒拋出例外即算通過
            await _tab!.SetWindowSizeAsync(0, 0, 800, 600);
            await _tab.MaximizeAsync();
            await _tab.MinimizeAsync();
            await _tab.FullscreenAsync();
            await _tab.MedimizeAsync();
        }

        [TestMethod]
        public async Task ScrollDown_And_Up_ShouldExecute()
        {
            // Act & Assert
            // 只要 CDP 沒拋出例外即算通過
            await _tab!.ScrollDownAsync(50);
            await _tab.ScrollUpAsync(50);
        }

        [TestMethod]
        public async Task SetDownloadPathAsync_ShouldNotThrow()
        {

        }

        [TestMethod]
        public async Task SaveScreenshotAsync_ShouldCreateImageFile()
        {
            // Act
            var filename = "test_screenshot.jpg";
            var filePath = await _tab!.SaveScreenshotAsync(filename, format: "jpg");

            // Assert
            Assert.IsTrue(File.Exists(filePath), "截圖檔案應該被建立");

            // 清理測試產生的檔案
            if (File.Exists(filePath)) 
                File.Delete(filePath);
        }

        [TestMethod]
        public async Task GetAllUrlsAsync_ShouldExtractAbsoluteUrls()
        {
            // Act
            var urls = await _tab!.GetAllUrlsAsync(absolute: true);

            // Assert
            Assert.IsTrue(urls.Contains("https://example.com/"), "應該要提取到 http 開頭的連結");
            Assert.IsTrue(urls.Contains("https://example.com/about/company"), "應該要提取到 / 開頭的連結");
            Assert.IsTrue(urls.Contains("https://google.com"), "應該要提取到 // 開頭的連結");
            Assert.IsFalse(urls.Any(it => it.Contains("javascript")), "不應包含 javascript 協議");
            Assert.IsFalse(urls.Any(it => it.Contains("#anchor")), "不應包含內部錨點");
        }

        [TestMethod]
        public async Task GetAllUrlsAsync_ShouldReturnRawValues()
        {
            // Act
            var rawUrls = await _tab!.GetAllUrlsAsync(absolute: false);

            // Assert
            Assert.IsTrue(rawUrls.Any(it => it == "/home"), "應直接拿到 /home 而不是補全的網址");
        }

        [TestMethod]
        public async Task LocalStorage_SetAndGet_ShouldWorkCorrectly()
        {
            // Arrange
            var testData = new Dictionary<string, string> { { "TestKey", "TestValue" } };

            // Act
            await _tab!.SetLocalStorageAsync(testData);
            var result = await _tab.GetLocalStorageAsync();

            // Assert
            Assert.IsTrue(result.ContainsKey("TestKey"), "應包含剛設定的鍵名 TestKey");
            Assert.AreEqual("TestValue", result["TestKey"], "TestKey 的值應與 TestValue 相符");
        }

        [TestMethod]
        public async Task ScrollBottomReachedAsync_ShouldReturnBoolean()
        {
            // Act
            var isReached = await _tab!.ScrollBottomReachedAsync();

            // Assert
            Assert.IsTrue(isReached, "因為測試網頁很短，通常一載入就到底了");
        }

        [TestMethod]
        public async Task MouseMove_And_Click_ShouldExecute()
        {
            // Act & Assert: 只要 CDP 沒拋出例外即算通過
            await _tab!.MouseMoveAsync(100, 100, steps: 5, flash: true);
            await _tab.MouseClickAsync(120, 120);
        }

        [TestMethod]
        public async Task TemplateLocationAsync_ShouldRunIfEnvironmentIsValid()
        {
            // Act & Assert: 如果環境沒有崩潰且能算出來，就過關
            var location = await _tab!.TemplateLocationAsync();
        }

        [TestMethod]
        public void Equals_And_ToString_ShouldWorkCorrectly()
        {
            var sameTab = _browser!.MainTab;
            Assert.IsTrue(_tab!.Equals(sameTab));
            Assert.IsTrue(_tab == sameTab);
            Assert.IsTrue(_tab.GetHashCode() == sameTab.GetHashCode());
            Assert.IsFalse(string.IsNullOrWhiteSpace(_tab.ToString()));
        }
    }
}
