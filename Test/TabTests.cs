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

        private Browser? _browser = null;
        private Tab? _tab = null;

        [TestInitialize]
        public async Task Setup()
        {
            var config = new Config
            {
                Headless = true,
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
        public async Task FindAsync_And_AllAsync_ShouldReturnElements()
        {
            // Act
            var single = await _tab!.FindAsync("Test Page");
            var multiple = await _tab!.FindAllAsync("Test");

            // Assert
            Assert.IsNotNull(single, "應該要能透過純文字找到元素");
            Assert.AreEqual(2, multiple.Count, "應該能找到包含 Test 文字的元素");
        }

        [TestMethod]
        public async Task SelectAsync_And_AllAsync_ShouldReturnElements()
        {
            // Act
            var single = await _tab!.SelectAsync("#title");
            var multiple = await _tab!.SelectAllAsync("p, h1, a, button");

            // Assert
            Assert.IsNotNull(single, "應該要能透過 CSS Selector 找到 id 為 title 的元素");
            Assert.AreEqual(4, multiple.Count, "應該找到多個標籤元素");
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
        public async Task QuerySelectorAsync_And_AllAsync_ShouldReturnElements()
        {
            // Act
            var single = await _tab!.QuerySelectorAsync("#btn");
            var multiple = await _tab.QuerySelectorAllAsync("p.content");

            // Assert
            Assert.IsNotNull(single, "應回傳單一元素");
            Assert.AreEqual("BUTTON", single.NodeName, "回傳的元素標籤應為 BUTTON");

            Assert.IsNotNull(multiple, "回傳值不應為 null");
            Assert.IsTrue(multiple.Count > 0, "使用選擇器 p.content 找不到任何元素");
        }

        [TestMethod]
        public async Task QuerySelectorAsync_WithParentNode_ShouldReturnElement()
        {
            // Arrange: 先抓取 body 作為父節點
            var body = await _tab!.QuerySelectorAsync("body");
            Assert.IsNotNull(body, "必須先找到 body 節點");

            // Act: 限制在 body 節點下進行 Selector 搜尋
            var btn = await _tab.QuerySelectorAsync("#btn", node: body);

            // Assert
            Assert.IsNotNull(btn, "在指定的父節點下，應能找到元素");
            Assert.AreEqual("BUTTON", btn.NodeName, "回傳的元素標籤應為 BUTTON");
        }

        [TestMethod]
        public async Task FindElementsByTextAsync_ShouldReturnElements()
        {
            // Act
            var elements = await _tab!.FindElementsByTextAsync("Click Me");

            // Assert
            Assert.IsTrue(elements.Count > 0, "應該能找到包含 Click Me 文字的 Button 元素");
        }

        [TestMethod]
        public async Task FindElementByTextAsync_WithBestMatch_ShouldReturnMostAccurateElement()
        {
            // Arrange: 動態加入兩個相似文字的元素來測試 bestMatch 邏輯
            await _tab!.EvaluateAsync(
                "document.body.innerHTML += '<div id=\"target1\">Hello World 123</div><div id=\"target2\">Hello World</div>';");
            await _tab.WaitAsync(0.5);

            // Act
            var exactMatch = await _tab.FindElementByTextAsync("Hello World", bestMatch: true);
            var firstMatch = await _tab.FindElementByTextAsync("Hello World", bestMatch: false);

            // Assert
            Assert.IsNotNull(exactMatch, "bestMatch=true 應該要找到元素");
            Assert.AreEqual("Hello World", exactMatch.TextAll?.Trim(), "開啟 bestMatch 應回傳字數長度最接近的元素");
            Assert.IsNotNull(firstMatch, "bestMatch=false 應該要找到元素");
            Assert.AreEqual("Hello World 123", firstMatch.TextAll?.Trim(), "應按照 DOM 順序回傳第一個包含關鍵字的元素");
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
        public async Task ResolveNodeAsync_ShouldReturnValidNode()
        {
            // Arrange
            var element = await _tab!.SelectAsync("#title");
            Assert.IsNotNull(element?.NodeId, "需要先取得一個帶有 NodeId 的元素");

            // Act
            var node = await _tab.ResolveNodeAsync(element.NodeId);

            // Assert
            Assert.IsNotNull(node, "應該成功解析出 Cdp.DOM.Node");
            Assert.AreEqual("H1", node.NodeName, "解析出的 Node 名稱應該是 H1");
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
        public async Task CloseAsync_ShouldCloseTarget()
        {
            // Arrange
            var newTab = await _tab!.GetAsync("chrome://version", newWindow: true);

            // Act & Assert: 只要 CDP 沒拋出例外即算通過
            await newTab.CloseAsync();
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
        public async Task GetWindowAsync_ShouldReturnWindowIdAndBounds()
        {
            // Act
            var result = await _tab!.GetWindowAsync();

            // Assert
            Assert.IsNotNull(result, "不應回傳 null");
            Assert.IsTrue(result.Value.Bounds.Width > 0, "視窗寬度應大於 0");
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
        public async Task GetScreenResolutionAsync_ShouldReturnValidDimensions()
        {
            // Act
            var resolution = await _tab!.GetScreenResolutionAsync();

            // Assert
            // 在 Headless 模式下可能會有預設解析度 (如 800x600)
            Assert.IsNotNull(resolution, "無法取得螢幕解析度");
            Assert.IsTrue(resolution.Value.Width > 0, "寬度應大於 0");
            Assert.IsTrue(resolution.Value.Height > 0, "高度應大於 0");
        }

        [TestMethod]
        public async Task Activate_And_BringToFront_ShouldExecuteWithoutError()
        {
            // Act & Assert: 驗證 CDP 呼叫正常無例外
            await _tab!.ActivateAsync();
            await _tab.BringToFrontAsync();
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
        public async Task ScrollBottomReachedAsync_ShouldReturnBoolean()
        {
            // Act
            var isReached = await _tab!.ScrollBottomReachedAsync();

            // Assert
            Assert.IsTrue(isReached, "因為測試網頁很短，通常一載入就到底了");
        }

        [TestMethod]
        public async Task SetDownloadPathAsync_ShouldCreateDirectory()
        {
            // Act
            var path = Path.Combine(AppContext.BaseDirectory, "test_downloads");
            await _tab!.SetDownloadPathAsync(path);

            // Assert
            Assert.IsTrue(Directory.Exists(path), "設定下載路徑後，應自動建立該目錄");

            // Cleanup
            if (Directory.Exists(path))
                Directory.Delete(path);
        }

        [TestMethod]
        public async Task DownloadFileAsync_ShouldDownloadFileToTargetDirectory()
        {
            // Arrange
            var path = Path.Combine(AppContext.BaseDirectory, "test_downloads");
            await _tab!.SetDownloadPathAsync(path);

            // 使用 data URI 模擬一個可以被 fetch 的檔案 (Base64 編碼的 "Hello")
            var dataUrl = "data:text/plain;base64,SGVsbG8=";
            var filename = "test_download.txt";

            // Act
            await _tab.DownloadFileAsync(dataUrl, filename);
            await Task.Delay(1000);

            // Assert
            var filePath = Path.Combine(path, filename);
            Assert.IsTrue(File.Exists(filePath), "檔案應該被成功下載並儲存");

            // Cleanup
            if (File.Exists(filePath)) 
                File.Delete(filePath);
            if (Directory.Exists(path)) 
                Directory.Delete(path);
        }

        [TestMethod]
        public async Task SaveScreenshotAsync_ShouldCreateImageFile()
        {
            // Act
            var filename = "test_screenshot.jpg";
            var filePath = await _tab!.SaveScreenshotAsync(filename, format: "jpg");

            // Assert
            Assert.IsTrue(File.Exists(filePath), "截圖檔案應該被建立");

            // Cleanup
            if (File.Exists(filePath)) 
                File.Delete(filePath);
        }

        [TestMethod]
        public async Task GetAllLinkedSourcesAsync_ShouldReturnElements()
        {
            // Act
            var elements = await _tab!.GetAllLinkedSourcesAsync();

            // Assert
            Assert.IsTrue(elements.Count > 0, "應該要抓到 HTML 內的 a, img, script 等元素");
            Assert.IsTrue(elements.Any(e => e.NodeName == "A"), "結果中應該包含 A 標籤");
        }

        private async Task SetupTestUrlsAsync()
        {
            // 先導航到一個有明確 Origin 的網址，確保 Target.Url 與 baseUrl 邏輯正常運作
            await _tab!.GetAsync("https://nowsecure.nl/test/sub/index.html");
            await _tab.WaitAsync(1);

            // 注入我們需要測試的所有 URL 格式
            var html = @"
                document.body.innerHTML = `
                    <a href='https://absolute.com/file.js'>Absolute HTTP</a>
                    <img src='/root-path/img.png' />
                    <script src='//cdn.com/lib.js'></script>
                    <link href='../parent-path/style.css' rel='stylesheet' />
                    <a href='sub-folder/page.html'>Sub Folder Link</a>
                    <a href='relative-file.html'>Relative</a>
                    <a href='https://www.google.com.tw/index.html#anchor'>With Hash</a>
                    <a href='javascript:void(0)'>JS Void</a>
                `;
            ";
            await _tab.EvaluateAsync(html);
            await _tab.WaitAsync(0.5);
        }

        [TestMethod]
        public async Task GetAllUrlsAsync_AbsoluteFalse_ShouldReturnRawUrls()
        {
            // Arrange
            await SetupTestUrlsAsync();

            // Act: absolute = false 應該只負責抓取屬性，不進行任何過濾或轉換
            var rawUrls = await _tab!.GetAllUrlsAsync(absolute: false);

            // Assert: 驗證是否原封不動地抓到了所有原始字串
            Assert.IsTrue(rawUrls.Contains("https://absolute.com/file.js"), "應包含完整的 HTTP 網址");
            Assert.IsTrue(rawUrls.Contains("/root-path/img.png"), "應包含 / 開頭的網址");
            Assert.IsTrue(rawUrls.Contains("//cdn.com/lib.js"), "應包含 // 開頭的網址");
            Assert.IsTrue(rawUrls.Contains("../parent-path/style.css"), "應包含 ../ 開頭的網址");
            Assert.IsTrue(rawUrls.Contains("relative-file.html"), "應包含無 / 開頭的相對網址");
            Assert.IsTrue(rawUrls.Contains("https://www.google.com.tw/index.html#anchor"), "應包含帶有 # 的網址");
            Assert.IsTrue(rawUrls.Contains("javascript:void(0)"), "應包含以 javascript: 開頭的行內腳本網址");
        }

        [TestMethod]
        public async Task GetAllUrlsAsync_AbsoluteTrue_ShouldResolveAndFilterUrls()
        {
            // Arrange
            await SetupTestUrlsAsync();

            // Act: absolute = true 會觸發您寫的過濾與 Uri.TryCreate 邏輯
            var absUrls = await _tab!.GetAllUrlsAsync(absolute: true);

            // Assert: 根據您目前 GetAllUrlsAsync 的程式碼邏輯進行驗證
            Assert.IsTrue(absUrls.Contains("https://absolute.com/file.js"), "完整的 HTTP 網址應正確保留");
            Assert.IsTrue(absUrls.Contains("https://nowsecure.nl/root-path/img.png"), "以 / 開頭的網址應被正確加上 Scheme 與 Host");
            Assert.IsTrue(absUrls.Contains("https://cdn.com/lib.js"), "以 // 開頭的網址應被補上 Scheme");
            Assert.IsTrue(absUrls.Contains("https://nowsecure.nl/test/parent-path/style.css"), "以 ../ 開頭的網址應正確被 Uri 類別解析合併");
            Assert.IsTrue(absUrls.Contains("https://nowsecure.nl/test/sub/sub-folder/page.html"), "中間帶有 / 的相對路徑應正確被 Uri 類別解析合併");

            Assert.IsFalse(absUrls.Any(it => it.Contains("relative-file.html")), "因程式碼的 validStarts 過濾邏輯，無 / 且無 http 的網址應被忽略");
            Assert.IsFalse(absUrls.Any(it => it.Contains("#anchor")), "因程式碼的 Contains('#') 邏輯，帶有錨點的網址應被忽略");
            Assert.IsFalse(absUrls.Any(it => it.Contains("javascript:void(0)")), "應排除 javascript: 開頭的非導航類連結");
        }

        [TestMethod]
        public async Task LocalStorage_SetAndGet_ShouldWorkCorrectly()
        {
            // Arrange
            var testData = new Dictionary<string, string> { { "TestKey", "TestValue" } };
            await _tab!.GetAsync("https://example.com");
            await _tab.WaitAsync(0.5);

            // Act
            await _tab!.SetLocalStorageAsync(testData);
            var result = await _tab.GetLocalStorageAsync();

            // Assert
            Assert.IsTrue(result.ContainsKey("TestKey"), "應包含剛設定的鍵名 TestKey");
            Assert.AreEqual("TestValue", result["TestKey"], "TestKey 的值應與 TestValue 相符");
        }

        [TestMethod]
        public async Task FrameTree_And_ResourceMethods_ShouldReturnData()
        {
            // Arrange
            var testHtml =
                "data:text/html,<html><head>" +
                "<link rel='stylesheet' href='data:text/css;base64,Ym9keSB7IGJhY2tncm91bmQ6IHdoaXRlIH0='>" +
                "<script src='data:text/javascript;base64,Y29uc29sZS5sb2coJ2hlbGxvJyk='></script>" +
                "</head><body>" +
                "<h1>Offline Test</h1>" +
                "</body></html>";

            await _tab!.GetAsync(testHtml);
            await _tab.WaitAsync(0.5);

            // Act: 依序測試與 Frame 相關的樹狀結構與資源
            var frameTree = await _tab!.GetFrameTreeAsync();
            var resourceTree = await _tab.GetFrameResourceTreeAsync();
            var resources = resourceTree.Resources;
            var urls = await _tab.GetFrameResourceUrlsAsync();
            var searchResults = await _tab.SearchFrameResourcesAsync("white");

            // Assert
            Assert.IsNotNull(frameTree, "GetFrameTreeAsync 不應為空");
            Assert.IsNotNull(resourceTree, "GetFrameResourceTreeAsync 不應為空");
            Assert.IsNotNull(searchResults, "SearchFrameResourcesAsync 不應為空");
            Assert.AreEqual(2, resources.Count, "預期應包含 CSS 與 JS 共 2 個資源");
            Assert.AreEqual(2, urls.Count, "預期應包含 CSS 與 JS 共 2 個 Url");
            Assert.AreEqual(1, searchResults.Count, "關鍵字 white 應匹配到 CSS 資源");
        }

        [TestMethod]
        public async Task MouseMove_And_Click_ShouldExecute()
        {
            // Act & Assert: 只要 CDP 沒拋出例外即算通過
            await _tab!.MouseMoveAsync(100, 100, steps: 5, flash: true);
            await _tab.MouseClickAsync(120, 120);
        }

        [TestMethod]
        public async Task MouseDragAsync_ShouldExecuteWithoutError()
        {
            // Act & Assert: 驗證在絕對位置與相對位置下都能正常發送 CDP 命令
            await _tab!.MouseDragAsync(
                sourcePoint: (10, 10), destPoint: (50, 50), relative: false, steps: 5);
            await _tab.MouseDragAsync(
                sourcePoint: (50, 50), destPoint: (10, 10), relative: true, steps: 1);
        }

        [TestMethod]
        public async Task FlashPointAsync_ShouldExecuteWithoutError()
        {
            // Act & Assert: 只要 CDP 沒拋出例外即算通過
            await _tab!.FlashPointAsync(100, 100, duration: 5, size: 50);
            await Task.Delay(6000);
        }

        [TestMethod]
        public async Task BypassInsecureConnectionWarningAsync_ShouldExecuteWithoutError()
        {
            // Act & Assert: 此方法是對 body 發送 keys，確保能順利執行不報錯
            await _tab!.BypassInsecureConnectionWarningAsync();
        }

        [TestMethod]
        public async Task TemplateLocationAsync_ShouldRunIfEnvironmentIsValid()
        {
            // Act & Assert: 如果環境沒有崩潰且能算出來，就過關
            var location = await _tab!.TemplateLocationAsync();
        }

        [TestMethod]
        public async Task VerifyCfAsync_ShouldExecuteWithoutError()
        {
            // Act & Assert: 沒出現異常即算通過
            await _tab!.VerifyCfAsync(flash: true);
        }

        [TestMethod]
        public void Equals_And_ToString_ShouldWorkCorrectly()
        {
            // Act & Assert
            var sameTab = _browser!.MainTab;
            Assert.IsTrue(_tab!.Equals(sameTab));
            Assert.IsTrue(_tab == sameTab);
            Assert.IsTrue(_tab.GetHashCode() == sameTab.GetHashCode());
            Assert.IsFalse(string.IsNullOrWhiteSpace(_tab.ToString()));
        }
    }
}
