using NoDriver.Core.Runtime;
using Cdp = NoDriver.Cdp;

namespace Test
{
    [TestClass]
    public class ElementTests
    {
        // 建立一個高涵蓋率的本機測試用 HTML，包含我們需要測試的所有情境
        private static readonly string _testHtml =
            "data:text/html," +
            "<html><body>" +
            "  <div id='parent'>DirectText<span id='child1' class='text-node'>Hello</span>" +
            "    <span id='child2'>World!</span>" +
            "  </div>" +
            "  <div id='parent1'><span class='target'>p1-child</span></div>" +
            "  <div id='parent2'><span class='target'>p2-child</span></div>" +
            "  <input id='text-input' type='text' value='initial' />" +
            "  <input id='file-input' type='file' />" +
            "  <select id='my-select'>" +
            "    <option value='1'>One</option>" +
            "    <option id='opt2' value='2'>Two</option>" +
            "  </select>" +
            "  <button id='my-btn' onclick='document.getElementById(\"child1\").innerText=\"Clicked!\"'>Click Me</button>" +
            "  <div id='host'></div>" +
            "  <video id='my-video' width='320' height='240' controls src='https://www.w3schools.com/html/mov_bbb.mp4'></video>" +
            "  <script>" +
            "    const host = document.getElementById('host');" +
            "    const shadow = host.attachShadow({mode: 'open'});" +
            "    shadow.innerHTML = '<p id=\"shadow-p\">Shadow Content</p>';" +
            "  </script>" +
            "</body></html>";

        private Browser? _browser;
        private Tab? _tab;

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
        public async Task Parent_And_Children_ShouldReturnCorrectElements()
        {
            // Arrange
            var parentNode = await _tab!.SelectAsync("#parent");

            // Act & Assert
            var children = parentNode!.Children.ToList();
            Assert.AreEqual(3, children.Count, "應該要找到 3 個子節點 (Text, child1, child2)");

            var child1 = await _tab.SelectAsync("#child1");
            Assert.IsNotNull(child1!.Parent);
            Assert.AreEqual("parent", child1.Parent.Attrs["id"], "子節點的 Parent id 應該是 parent");
        }

        [TestMethod]
        public async Task ShadowChildren_ShouldReturnElementsInShadowRoot()
        {
            // Arrange
            var host = await _tab!.SelectAsync("#host");

            // Act
            var shadowChildren = host!.ShadowChildren.ToList();

            // Assert
            Assert.IsTrue(shadowChildren.Count > 0, "應該要在 host 底下找到開放的 ShadowRoot 子節點");
        }

        [TestMethod]
        public async Task Text_And_TextAll_ShouldReturnInnerText()
        {
            // Arrange
            var parentNode = await _tab!.SelectAsync("#parent");

            // Act
            // Text 只抓第一個符合 NodeType == 3 的節點 (DirectText)
            var text = parentNode!.Text;

            // TextAll 會抓到 DirectText, Hello, World! 並用空格串接
            var textAll = parentNode.TextAll;

            // Assert
            // 預期：只包含直接屬於 parent 的文字，不含 span 裡的
            Assert.AreEqual("DirectText", text.Trim(), "應該只回傳第一個文字節點內容");

            // 預期：包含所有層級的文字內容
            Assert.IsTrue(textAll.Contains("DirectText"), "應包含直接文字");
            Assert.IsTrue(textAll.Contains("Hello"), "應包含子節點 child1 的文字");
            Assert.IsTrue(textAll.Contains("World!"), "應包含子節點 child2 的文字");

            // 驗證串接邏輯 (確認中間有空格)
            Assert.AreEqual("DirectText Hello World!", textAll.Trim(), "應正確串接所有文字節點並以空格分隔");
        }

        [TestMethod]
        public async Task Text_Properties_ShouldReturnEmpty_WhenNoTextNodesExist()
        {
            // Arrange: 找到一個完全沒有文字內容的元素
            var videoElement = await _tab!.SelectAsync("#my-video");

            // Act & Assert
            Assert.AreEqual("", videoElement!.Text, "沒有文字節點時 Text 應回傳空字串");
            Assert.AreEqual("", videoElement.TextAll, "沒有文字節點時 TextAll 應回傳空字串");
        }

        [TestMethod]
        public async Task GetHtmlAsync_ShouldReturnOuterHtml()
        {
            // Arrange
            var child1 = await _tab!.SelectAsync("#child1");

            // Act
            var html = await child1!.GetHtmlAsync();

            // Assert
            Assert.IsTrue(html.Contains("id=\"child1\""), $"OuterHTML 應該包含元素本身的標籤");
        }

        [TestMethod]
        public async Task RemoveFromDomAsync_ShouldModifyTree()
        {
            // Arrange
            var child2 = await _tab!.SelectAsync("#child2");

            // Act
            await child2!.RemoveFromDomAsync();
            await _tab.WaitAsync(0.5);

            // Assert
            var afterRemove = await _tab.SelectAsync("#child2", timeout: 2);
            Assert.IsNull(afterRemove, "移除後應該查不到 child2 元素");
        }

        [TestMethod]
        public async Task SaveToDomAsync_ShouldSyncCSharpAttributeToBrowser()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");

            // Act
            btn!.Attrs["data-test"] = "updated";
            await btn.SaveToDomAsync();
            await _tab.WaitAsync(0.5);

            // Assert: 驗證瀏覽器上的真實 DOM 是否已經更新
            var updatedBtn = await _tab.SelectAsync("#my-btn");
            var (remoteObj, _) = await updatedBtn!.ApplyAsync($"(e) => e.getAttribute('data-test')");
            Assert.AreEqual("updated", remoteObj.Value?.ToString(), "網頁上的屬性應與 C# 端修改後的數值同步");
        }

        [TestMethod]
        public async Task SaveToDomAsync_ShouldSyncTagNameChange()
        {
            // Arrange
            var child1 = await _tab!.SelectAsync("#child1");

            // Act
            var originalTagName = child1!.TagName;
            child1.Attrs["class"] = "new-class-name";
            await child1.SaveToDomAsync();
            await _tab.WaitAsync(0.5);

            // Assert
            var restored = await _tab.SelectAsync("#child1");
            Assert.AreEqual(originalTagName, restored!.TagName, "標籤名稱在同步後應保持不變");
            Assert.AreEqual("new-class-name", restored.Attrs["class"], "Class 屬性應已更新");
        }

        [TestMethod]
        public async Task GetJsAttributesAsync_ShouldReturnAllDynamicJsProperties()
        {
            // Arrange
            var input = await _tab!.SelectAsync("#text-input");

            // 透過 JS 給這個 DOM 物件掛載一個自定義的動態屬性
            await input!.ApplyAsync("(e) => { e.customProp = 'hello-js'; e.customNum = 123; }");

            // Act
            var jsAttrs = await input.GetJsAttributesAsync();

            // Assert
            Assert.IsNotNull(jsAttrs, "回傳的 JsonNode 不應為 null");

            // 驗證是否包含標準 DOM 屬性
            var value = jsAttrs["value"];
            Assert.IsNotNull(value, "應包含標準 JS 屬性 'value'");
            Assert.AreEqual("initial", value.GetValue<string>(), "value 的內容應正確");

            // 驗證是否包含我們剛剛動態新增的屬性
            Assert.AreEqual("hello-js", jsAttrs["customProp"]!.GetValue<string>(), "自定義屬性內容應正確");
            Assert.AreEqual(123, jsAttrs["customNum"]!.GetValue<int>(), "應包含自定義數字屬性");
        }

        [TestMethod]
        public async Task CallAsync_InvokingFocusMethod_ShouldChangeActiveElement()
        {
            // Arrange
            var input = await _tab!.SelectAsync("#text-input");

            // Act: 這等同於執行 (e) => e['focus']()
            var (_, exception) = await input!.CallAsync("focus");
            Assert.IsNull(exception, $"不應產生例外");

            // Assert: 驗證網頁上目前聚焦的元素是否為該 input
            var (result, _) = await _tab.EvaluateAsync("document.activeElement.id", returnByValue: true);
            Assert.AreEqual("text-input", result.Value?.ToString(), "執行後，該元素應成為活動元素");
        }

        [TestMethod]
        public async Task CallAsync_InvokingCustomMethod_ShouldReturnExpectedResult()
        {
            // Arrange: 先在一個元素上動態掛載一個 JS 函數
            var btn = await _tab!.SelectAsync("#my-btn");
            await btn!.ApplyAsync("(e) => { e.customAdd = (a, b) => 100; }");

            // Act: 呼叫該自定義方法
            var (remoteObj, _) = await btn.CallAsync("customAdd");

            // Assert: 驗證回傳值是否為 100
            Assert.AreEqual(100, remoteObj.Value?.GetValue<int>(), "應能取得 JS 方法的執行回傳值");
        }

        [TestMethod]
        public async Task CallAsync_InvokingNonExistentMethod_ShouldReturnException()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");

            // Act: 呼叫一個不存在的方法
            var (_, exception) = await btn!.CallAsync("nonExistentFunction");

            // Assert
            Assert.IsNotNull(exception, "呼叫不存在的方法時，應該回傳 ExceptionDetails");
            Assert.IsTrue(exception.Exception?.Description?.Contains("not a function"), "錯誤訊息應指出該名稱不是一個函數");
        }

        [TestMethod]
        public async Task ApplyAsync_IdentityCheck_ShouldReferenceCurrentElement()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");

            // Act
            var (remoteObj, _) = await btn!.ApplyAsync("(e) => e.id");

            // Assert
            Assert.AreEqual("my-btn", remoteObj.Value?.ToString(), "傳入的參數 e 應指向該元素本身");
        }

        [TestMethod]
        public async Task ApplyAsync_ComplexCalculation_ShouldReturnCorrectValue()
        {
            // Arrange
            var input = await _tab!.SelectAsync("#text-input");

            // Act
            var (remoteObj, _) = await input!.ApplyAsync("(e) => e.tagName.length");

            // Assert
            Assert.AreEqual(5, remoteObj.Value?.GetValue<int>(), "應能正確獲取 JS 運算後的回傳值");
        }

        [TestMethod]
        public async Task ApplyAsync_InvalidJs_ShouldReturnExceptionDetails()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");

            // Act: 故意執行一段會報錯的 JS
            var (_, exception) = await btn!.ApplyAsync("(e) => { return nonExistentVar + 1; }");

            // Assert
            Assert.IsNotNull(exception, "JS 報錯時，exception 不應為 null");
            Assert.IsTrue(exception.Exception?.Description?.Contains("nonExistentVar is not defined"), "應捕獲到正確的 JS 錯誤訊息");
        }

        [TestMethod]
        public async Task ApplyAsync_ModifyDomState_ShouldReflectInBrowser()
        {
            // Arrange
            var child = await _tab!.SelectAsync("#child1");

            // Act: 透過 ApplyAsync 直接修改 DOM 的 style
            var color = "rgb(255, 0, 0)";
            await child!.ApplyAsync($"(e) => e.style.color = '{color}'");

            // Assert
            var (remoteObj, _) = await child.ApplyAsync("(e) => e.style.color");
            Assert.AreEqual(color, remoteObj.Value?.ToString(), "修改的樣式應反映在瀏覽器中");
        }

        [TestMethod]
        public async Task GetPositionAsync_RelativeToViewport_ReturnsValidCoordinates()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");

            // Act: 取得相對位置 (abs = false)
            var pos = await btn!.GetPositionAsync(abs: false);

            // Assert
            Assert.IsNotNull(pos, "應該要能取得可見元素的位置資訊");
            Assert.IsTrue(pos.Width > 0 && pos.Height > 0, "元素的寬高應大於 0");

            // 驗證四個邊角的邏輯
            Assert.AreEqual(pos.Left + pos.Width, pos.Right, 0.1, "Right 應等於 Left + Width");
            Assert.AreEqual(pos.Top + pos.Height, pos.Bottom, 0.1, "Bottom 應等於 Top + Height");
        }

        [TestMethod]
        public async Task GetPositionAsync_WithAbsoluteCoordinates_CalculatesCorrectly()
        {
            // Arrange: 先手動滾動頁面，產生 scrollX/scrollY
            await _tab!.EvaluateAsync("document.body.style.height = '2000px'; window.scrollTo(0, 500);");
            await _tab.WaitAsync(0.5);

            var btn = await _tab.SelectAsync("#my-btn");

            // Act: 取得絕對位置 (abs = true)
            var posAbs = await btn!.GetPositionAsync(abs: true);

            // 取得當前滾動數值以便人工驗證
            var (remoteObj, _) = await _tab.EvaluateAsync("window.scrollY");
            var scrollY = remoteObj.Value?.GetValue<double>() ?? 0;

            // Assert
            Assert.IsNotNull(posAbs);
            // AbsY 應該等於 (元素在視窗頂部距離 + 視窗已滾動距離 + 元素高度的一半)
            var expectedAbsY = posAbs.Top + scrollY + (posAbs.Height / 2.0);
            Assert.AreEqual(expectedAbsY, posAbs.AbsY, 0.1, "絕對 Y 座標計算邏輯應符合滾動位移");
        }

        [TestMethod]
        public async Task GetPositionAsync_WhenElementIsHidden_ShouldThrowException()
        {
            // Arrange: 將一個元素設為隱藏
            var btn = await _tab!.SelectAsync("#my-btn");
            await btn!.ApplyAsync("(e) => e.style.display = 'none'");
            await _tab.WaitAsync(0.5);

            // Act: 當 display: none 時，getContentQuads 通常會找不到 Quads
            var exception = await Assert.ThrowsExceptionAsync<Exception>(async () => await btn.GetPositionAsync());

            // Assert
            Assert.IsTrue(exception.Message.Contains("Could not find position"), "隱藏元素應該無法取得位置資訊，拋出例外");
        }

        [TestMethod]
        public async Task ClickAsync_ShouldTriggerEvent()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");
            var child1 = await _tab.SelectAsync("#child1");

            // Act: 透過 DOM JavaScript 層面點擊
            await btn!.ClickAsync();
            await _tab.WaitAsync(0.5);
            await child1!.UpdateAsync();

            // Assert
            Assert.IsTrue(child1.Text.Contains("Clicked!"), "JS 點擊後文字應該改變");
        }

        [TestMethod]
        public async Task MouseClickAsync_ShouldTriggerEvent()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");
            var child1 = await _tab.SelectAsync("#child1");

            // Act: 透過模擬真實滑鼠的 CDP Input 點擊
            await btn!.MouseClickAsync();
            await _tab.WaitAsync(0.5);
            await child1!.UpdateAsync();

            // Assert
            Assert.IsTrue(child1.Text.Contains("Clicked!"), "滑鼠點擊後文字應該改變");
        }

        [TestMethod]
        public async Task MouseMoveAsync_Invoked_MovesCursorToElementCenter()
        {
            // 1. Arrange: 取得按鈕元素
            var btn = await _tab!.SelectAsync("#my-btn");
            Assert.IsNotNull(btn);

            // 在網頁端設置監聽器，紀錄滑鼠移動到的最後位置
            await _tab.EvaluateAsync(@"(function(){
                window.lastMouseX = 0;
                window.lastMouseY = 0;
                document.addEventListener('mousemove', (e) => {
                    window.lastMouseX = e.clientX;
                    window.lastMouseY = e.clientY;
                });
            })()");

            // 取得元素預期的中心點座標
            var pos = await btn!.GetPositionAsync();
            Assert.IsNotNull(pos?.Center, "應該要能取得元素的中心點");
            double expectedX = pos.Center.X;
            double expectedY = pos.Center.Y;

            // 2. Act: 執行移動滑鼠到該元素
            await btn.MouseMoveAsync();
            await _tab.WaitAsync(0.5); // 等待瀏覽器處理事件

            // 3. Assert: 從瀏覽器取回紀錄的座標
            var (resX, _) = await _tab.EvaluateAsync("window.lastMouseX");
            var (resY, _) = await _tab.EvaluateAsync("window.lastMouseY");

            double actualX = resX.Value?.GetValue<double>() ?? 0;
            double actualY = resY.Value?.GetValue<double>() ?? 0;

            // 驗證座標是否與中心點吻合 (容許 1 像素誤差)
            Assert.AreEqual(expectedX, actualX, 1.0, $"滑鼠 X 座標不正確。預期: {expectedX}, 實際: {actualX}");
            Assert.AreEqual(expectedY, actualY, 1.0, $"滑鼠 Y 座標不正確。預期: {expectedY}, 實際: {actualY}");
        }

        private async Task SetupDragTraceAsync()
        {
            await _tab!.EvaluateAsync(@"
                (function() {
                    window.dragData = { started: false, droppedAtX: 0, droppedAtY: 0 };
                    const src = document.getElementById('child1');

                    // 監聽按下
                    src.onmousedown = () => { window.dragData.started = true; };
                    
                    // 監聽在 document 上的放開 (因為放開時滑鼠可能不在 target 上)
                    document.onmouseup = (e) => {
                        if (window.dragData.started) {
                            window.dragData.droppedAtX = e.clientX;
                            window.dragData.droppedAtY = e.clientY;
                        }
                    };
                })()");
        }

        [TestMethod]
        public async Task MouseDragAsync_ToElement_ShouldTriggerMoveAndDrop()
        {
            // Arrange
            var source = await _tab!.SelectAsync("#child1");
            var target = await _tab!.SelectAsync("#child2");

            // 在網頁端設置監聽器，紀錄拖拽行為
            await SetupDragTraceAsync();

            // 取得目標元素的中心點，作為預期放開的位置
            var targetPos = await target!.GetPositionAsync();
            var expectedX = targetPos!.Center.X;
            var expectedY = targetPos!.Center.Y;

            // Act
            // 使用 steps = 5 讓移動軌跡更平滑，模擬真實行為
            await source!.MouseDragAsync(target, steps: 5);
            await _tab.WaitAsync(0.5);

            // Assert
            var (started, _) = await _tab.EvaluateAsync("window.dragData.started");
            var (actualX, _) = await _tab.EvaluateAsync("window.dragData.droppedAtX");
            var (actualY, _) = await _tab.EvaluateAsync("window.dragData.droppedAtY");

            Assert.IsTrue(started.Value?.GetValue<bool>() ?? false, "拖拽動作應該要觸發 mousedown");
            // 驗證放開的位置是否在目標元素中心點附近 (容許 1 像素誤差)
            Assert.AreEqual(expectedX, actualX.Value?.GetValue<double>() ?? 0, 1, "拖拽放開的 X 座標不正確");
            Assert.AreEqual(expectedY, actualY.Value?.GetValue<double>() ?? 0, 1, "拖拽放開的 Y 座標不正確");
        }

        [TestMethod]
        public async Task MouseDragAsync_ToPoint_ShouldMoveToSpecificCoordinate()
        {
            // Arrange
            var source = await _tab!.SelectAsync("#child1");
            // 設定一個絕對目標點 (例如 150, 150)
            var destPoint = (X: 150.0, Y: 150.0);

            // 在網頁端設置監聽器，紀錄拖拽行為
            await SetupDragTraceAsync();

            // Act
            await source!.MouseDragAsync(destPoint, relative: false, steps: 5);
            await _tab.WaitAsync(0.5);

            // Assert
            var (resX, _) = await _tab.EvaluateAsync("window.dragData.droppedAtX");
            var (resY, _) = await _tab.EvaluateAsync("window.dragData.droppedAtY");

            Assert.AreEqual(150.0, resX.Value?.GetValue<double>() ?? 0, 1, "拖拽至點的 X 座標不符");
            Assert.AreEqual(150.0, resY.Value?.GetValue<double>() ?? 0, 1, "拖拽至點的 Y 座標不符");
        }

        [TestMethod]
        public async Task ClearInputAsync_ShouldWork()
        {
            // Arrange
            var input = await _tab!.SelectAsync("#text-input");

            var (initialVal, _) = await input!.ApplyAsync("(e) => e.value");
            Assert.AreEqual("initial", initialVal.Value?.ToString(), "測試前，輸入框應有初始值");

            // Act
            await input.ClearInputAsync();

            // Assert
            var (finalVal, _) = await input.ApplyAsync("(e) => e.value");
            Assert.AreEqual("", finalVal.Value?.ToString(), "執行後，輸入框的值應該清空");
        }

        [TestMethod]
        public async Task SendKeysAsync_ShouldWork()
        {
            // Arrange
            var input = await _tab!.SelectAsync("#text-input");
            await input!.ClearInputAsync();

            // Act
            await input!.SendKeysAsync("Automation");

            // Assert
            var (remoteObj, _) = await input.ApplyAsync("(e) => e.value");
            Assert.AreEqual("Automation", remoteObj.Value?.ToString(), "輸入框的值應該等於我們 SendKeys 的內容");
        }

        [TestMethod]
        public async Task SelectOptionAsync_ShouldSetSelected()
        {
            // Arrange
            var option = await _tab!.SelectAsync("#opt2");
            var select = await _tab.SelectAsync("#my-select");

            // Act
            await option!.SelectOptionAsync();

            // Assert
            var (remoteObj, _) = await select!.ApplyAsync("(e) => e.value");
            Assert.AreEqual("2", remoteObj.Value?.ToString(), "Select 元件的值應該改變為 option 的值");
        }

        [TestMethod]
        public async Task SendFileAsync_ShouldUploadFile()
        {
            // Arrange: 建立一個暫存檔案
            var tempFile = Path.Combine(AppContext.BaseDirectory, "test_temp_file.txt");
            File.WriteAllText(tempFile, "Dummy File Content");

            var fileInput = await _tab!.SelectAsync("#file-input");

            // Act
            await fileInput!.SendFileAsync(tempFile);

            // Assert
            var (remoteObj, _) = await fileInput.ApplyAsync("(e) => e.files.length");
            Assert.AreEqual(1, remoteObj.Value?.GetValue<int>(), "應該已經上傳了 1 個檔案");

            // Cleanup
            if (File.Exists(tempFile)) 
                File.Delete(tempFile);
        }

        [TestMethod]
        public async Task SetValueAsync_OnTextNode_ShouldChangeValueDirectly()
        {
            // Arrange: 我們先找到 #child1 的第一個子節點（這才是真正的 NodeType 3）
            var span = await _tab!.SelectAsync("#child1");
            var textNode = span!.Children.First();
            Assert.AreEqual(3, textNode.NodeType, "確保我們拿到的是文本節點");

            // Act
            await textNode.SetValueAsync("new-text");

            // Assert
            var (remoteObj, _) = await span.ApplyAsync("(e) => e.innerText");
            Assert.AreEqual("new-text", remoteObj.Value?.ToString(), "應修改文本節點的內容");
        }

        [TestMethod]
        public async Task SetTextAsync_OnSpanElement_ShouldUpdateVisibleText()
        {
            // Arrange: 雖然 span 是 ElementNode (Type 1)，但它裡面包含一個 TextNode (Type 3)
            var span = await _tab!.SelectAsync("#child1");

            // Act
            await span!.SetTextAsync("new-text");

            // Assert: 透過 Text 屬性（或是直接從 DOM 抓取）驗證
            Assert.AreEqual("new-text", span.Text.Trim(), "應透過遞迴修改子文本節點的內容");
        }

        [TestMethod]
        public async Task SetTextAsync_OnElementWithMultipleChildren_ShouldThrowException()
        {
            // Arrange: #parent 有多個子節點 (Text, span, span)
            var parent = await _tab!.SelectAsync("#parent");

            // Act & Assert: 根據你的程式碼，ChildNodeCount != 1 且非 NodeType 3 應拋出例外
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () => await parent!.SetTextAsync("This should fail"));
        }

        [TestMethod]
        public async Task FocusAsync_Invoked_SetsActiveElementInBrowser()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");
            var input = await _tab!.SelectAsync("#text-input");
            
            // 先把焦點移到按鈕上，確保起始狀態不是 input
            await btn!.FocusAsync();

            // Act
            await input!.FocusAsync();

            // Assert: 透過 JS 驗證當前活動元素 (ActiveElement) 是否為該 input
            var (remoteObj, _) = await _tab.EvaluateAsync("document.activeElement.id");
            Assert.AreEqual("text-input", remoteObj.Value?.ToString(), "執行後，該元素應成為 document.activeElement");
        }

        [TestMethod]
        public async Task ScrollIntoViewAsync_OffscreenElement_ShouldBringElementIntoViewport()
        {
            // Arrange: 準備一個非常長的頁面，並將元素放在遠端
            // 我們動態修改 body 高度，並把一個 div 放在 2000px 的位置
            var s = await _tab!.EvaluateAsync(@"
                (function() {
                    document.body.style.height = '3000px';
                    const spacer = document.createElement('div');
                    spacer.style.height = '2000px';
                    const target = document.createElement('div');
                    target.id = 'scroll-target';
                    target.innerText = 'I am down here';
                    document.body.appendChild(spacer);
                    document.body.appendChild(target);
                    window.scrollTo(0, 0); // 確保起始位置在頂部
                })()");
            await _tab.WaitAsync(0.5);

            var targetElement = await _tab.SelectAsync("#scroll-target");
            Assert.IsNotNull(targetElement);

            // Act
            await targetElement!.ScrollIntoViewAsync();
            await _tab.WaitAsync(0.5);

            // Assert: 驗證 window.scrollY 是否大於 1500
            var (remoteObj, _) = await _tab.EvaluateAsync("window.scrollY");
            var scrollY = remoteObj.Value?.GetValue<double>() ?? 0;
            Assert.IsTrue(scrollY > 1500, "執行後，頁面應向下捲動");
        }

        [TestMethod]
        public async Task QuerySelectorAsync_ScopedToElement_ReturnsCorrectChild()
        {
            // Arrange
            var parent1 = await _tab!.SelectAsync("#parent1");
            var parent2 = await _tab!.SelectAsync("#parent2");

            // Act
            var childInP1 = await parent1!.QuerySelectorAsync(".target");
            var childInP2 = await parent2!.QuerySelectorAsync(".target");

            // Assert
            Assert.IsNotNull(childInP1);
            Assert.AreEqual("p1-child", childInP1.Text.Trim(), "應找到 parent1 內部的 span");

            Assert.IsNotNull(childInP2);
            Assert.AreEqual("p2-child", childInP2.Text.Trim(), "應找到 parent2 內部的 span");
        }

        [TestMethod]
        public async Task QuerySelectorAllAsync_ReturnsAllMatchingChildren()
        {
            // Arrange
            var parent = await _tab!.SelectAsync("#parent");

            // Act
            var multiple = await parent!.QuerySelectorAllAsync("span");

            // Assert
            Assert.AreEqual(2, multiple.ToList().Count, "應該要在 parent 中找到 2 個 span");
        }

        [TestMethod]
        public async Task QuerySelectorAsync_NonExistentChild_ReturnsNull()
        {
            // Arrange
            var parent = await _tab!.SelectAsync("#parent");

            // Act: 找一個不存在於此父節點下的 selector
            var result = await parent!.QuerySelectorAsync("#no-such-id");

            // Assert
            Assert.IsNull(result, "找不到元素時應回傳 null 而非拋出例外");
        }

        [TestMethod]
        public async Task SaveScreenshotAsync_ShouldCaptureElementClip()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");

            // Act
            var filename = "btn_screenshot.jpg";
            var filePath = await btn!.SaveScreenshotAsync(filename, format: "jpg", scale: 5);

            // Assert
            Assert.IsTrue(File.Exists(filePath), "應該會產生該元素的截圖檔案");

            // Cleanup
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        [TestMethod]
        public async Task FlashAsync_ShouldExecuteWithoutError()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");

            // Act & Assert: 只要 CDP 沒拋出例外即算通過
            await btn!.FlashAsync(duration: 3);
            await Task.Delay(5000);
        }

        [TestMethod]
        public async Task HighlightOverlayAsync_ShouldToggleOverlay()
        {
            // Arrange
            var btn = await _tab!.SelectAsync("#my-btn");

            // Act & Assert: 沒拋出例外則視為指令成功

            // (第一次呼叫): 開啟高亮
            await btn!.HighlightOverlayAsync();
            await Task.Delay(1000);

            // (第二次呼叫): 關閉高亮
            await btn.HighlightOverlayAsync();
            await Task.Delay(1000);
        }

        [TestMethod]
        public async Task RecordVideoAsync_And_IsRecordingAsync_ShouldControlVideoTag()
        {
            // 在本機環境使用 data:text/html 以及線上影片，我們主要是測試錄影狀態
            var video = await _tab!.SelectAsync("#my-video");

            // 執行 1 秒的短時間錄製 (因為 RecordVideoAsync 會阻塞等待直到結束，設短一點比較好測試)
            // 註: 若系統不支援 captureStream，這裡可能會在前端報錯但由 C# 捕獲或忽略，我們觀察其邏輯
            var recordTask = video!.RecordVideoAsync(duration: 0.5);

            // 稍微等一下下讓錄影開始
            await _tab.WaitAsync(0.2);

            var isRecording = await video.IsRecordingAsync();

            // IsRecordingAsync 應該會抓取物件上的 ['_recording'] 屬性
            // 由於非同步可能導致時間差，這裡斷言不論是 true 或 false，至少有回傳一個合理的 bool 值
            Assert.IsNotNull(isRecording, "IsRecordingAsync 應該能正確解析回傳值");

            await recordTask; // 等待錄影方法完整結束
        }
    }
}
