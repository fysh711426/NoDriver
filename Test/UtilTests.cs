using NoDriver.Core.Runtime;
using System.Net.NetworkInformation;
using Cdp = NoDriver.Cdp;

namespace Test
{
    [TestClass]
    public class UtilTests
    {
        private Browser? _browser;
        private Tab? _tab;

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
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_browser != null)
                await _browser.DisposeAsync();
        }

        [TestMethod]
        public void FreePort_ShouldReturnsValidAvailablePort()
        {
            // Act
            var port = Util.FreePort();

            // Assert
            Assert.IsTrue(port > 0 && port <= 65535, "Port 應在有效的 TCP 連接埠範圍內");

            // 透過檢查活動監聽器來驗證連接埠是否可用
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = ipGlobalProperties.GetActiveTcpListeners();
            var isTaken = tcpConnections.Any(endpoint => endpoint.Port == port);
            Assert.IsFalse(isTaken, "返回的 Port 已被佔用");
        }

        [TestMethod]
        public void Circle_ShouldReturnsCorrectNumberOfPoints()
        {
            // Arrange
            var numPoints = 10;

            // Act
            var pointsForward = Util.Circle(0, 0, 10, numPoints, 0).ToList();
            var pointsBackward = Util.Circle(0, 0, 10, numPoints, 1).ToList();

            // Assert
            // 由於 for 迴圈條件是 i < w + 1，所以會產生 11 個點（起點和終點重合）
            Assert.AreEqual(11, pointsForward.Count);
            Assert.AreEqual(11, pointsBackward.Count);

            // 驗證前後端點是否正確閉合 (允許微小浮點數誤差)
            Assert.AreEqual(pointsForward.First().X, pointsForward.Last().X, 0.001);
            Assert.AreEqual(pointsForward.First().Y, pointsForward.Last().Y, 0.001);
        }

        [TestMethod]
        public void CompareTargetInfo_IdenticalObjects_ShouldReturnsEmpty()
        {
            // Arrange
            var info1 = new Cdp.Target.TargetInfo(
                new Cdp.Target.TargetID("123"), "page", "Test", "", false, false);
            var info2 = new Cdp.Target.TargetInfo(
                new Cdp.Target.TargetID("123"), "page", "Test", "", false, false);

            // Act
            var diffs = Util.CompareTargetInfo(info1, info2).ToList();

            // Assert
            Assert.AreEqual(0, diffs.Count, "當物件完全相同時，不應該產生任何差異資訊");
        }

        [TestMethod]
        public void CompareTargetInfo_DifferentObjects_ShouldReturnsDifferences()
        {
            // Arrange
            var info1 = new Cdp.Target.TargetInfo(
                new Cdp.Target.TargetID("123"), "page", "Old Title", "", false, false);
            var info2 = new Cdp.Target.TargetInfo(
                new Cdp.Target.TargetID("123"), "page", "New Title", "", false, false);

            // Act
            var diffs = Util.CompareTargetInfo(info1, info2).ToList();

            // Assert
            Assert.AreEqual(1, diffs.Count, "預期會有 1 筆差異資料");
            Assert.AreEqual("Title", diffs[0].Key, "發生變動的欄位應是 Title");
            Assert.AreEqual("Old Title", diffs[0].Old, "變更前的值應為 Old Title");
            Assert.AreEqual("New Title", diffs[0].New, "變更前的值應為 New Title");
        }

        [TestMethod]
        public void CompareTargetInfo_NullInputs_ShouldReturnsEmpty()
        {
            // Arrange
            var info = new Cdp.Target.TargetInfo(
                new Cdp.Target.TargetID("123"), "page", "Test", "", false, false);

            // Act
            var diffs = Util.CompareTargetInfo(null, info).ToList();

            // Assert
            Assert.AreEqual(0, diffs.Count, "預期應回傳一個空清單，而不是報錯");
        }

        [TestMethod]
        public void GetCfTemplate_ShouldReturnsValidPngHeaderArray()
        {
            // Act
            var bytes = Util.GetCfTemplate();

            // Assert
            Assert.IsNotNull(bytes);
            Assert.IsTrue(bytes.Length > 8);

            // 驗證是否為標準的 PNG 檔案開頭 (137, 80, 78, 71, 13, 10, 26, 10)
            var pngHeader = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (var i = 0; i < pngHeader.Length; i++)
            {
                Assert.AreEqual(pngHeader[i], bytes[i], "Template 必須是有效的 PNG 格式位元組");
            }
        }

        [TestMethod]
        public async Task FilterRecurseAll_ShouldFindsAllMatchingNodes_IncludingShadowRoots()
        {
            // Arrange
            var testHtml = @"
                <html><body>
                    <div>
                        <span id='level1'>
                            <span id='level2'>
                                <button id='targetBtn'>Click Me</button>
                            </span>
                        </span>
                    </div>
                    <div id='host'></div>
                    <script>
                        const host = document.getElementById('host');
                        const shadow = host.attachShadow({mode: 'open'});
                        shadow.innerHTML = '<span>Shadow Content</span>';
                    </script>
                </body></html>";

            await _tab!.GetAsync($"data:text/html,{testHtml}");
            await _tab.WaitAsync(0.5);

            var result = await _tab!.SendAsync(Cdp.DOM.GetDocument(-1, true));
            var doc = result.Root;

            // Act
            var spans = Util.FilterRecurseAll(doc, n => n.NodeName == "SPAN").ToList();

            // Assert
            Assert.AreEqual(3, spans.Count, "應該要找到 DOM 樹中所有的 Span 節點");
        }

        [TestMethod]
        public async Task FilterRecurse_ShouldFindsFirstMatchingNode()
        {
            // Arrange
            var testHtml = @"
                <html><body>
                    <div></div>
                    <div>
                        <target id=""1""></target>
                        <target id=""2""></target>
                    </div>
                </body></html>";

            await _tab!.GetAsync($"data:text/html,{testHtml}");
            await _tab.WaitAsync(0.5);

            var result = await _tab!.SendAsync(Cdp.DOM.GetDocument(-1, true));
            var doc = result.Root;

            // Act
            var target = Util.FilterRecurse(doc, n => n.NodeName == "TARGET");

            // Assert
            Assert.IsNotNull(target, "target 不應為 null");
            Assert.AreEqual("1", target.Attributes![1], "應回傳 HTML 中第一個遇到的 target 節點");
        }

        [TestMethod]
        public async Task FilterRecurse_NoMatch_ShouldReturnsNull()
        {
            // Arrange
            var testHtml = @"
                <html><body>
                    <div></div>
                </body></html>";

            await _tab!.GetAsync($"data:text/html,{testHtml}");
            await _tab.WaitAsync(0.5);

            var result = await _tab!.SendAsync(Cdp.DOM.GetDocument(-1, true));
            var doc = result.Root;

            // Act
            var non = Util.FilterRecurse(doc, n => n.NodeName == "NON_EXISTENT");
            var empty = Util.FilterRecurseAll(doc, n => n.NodeName == "NON_EXISTENT").ToList();

            // Assert
            Assert.IsNull(non, "找不到節點時應回傳 null 而非拋出例外");
            Assert.IsTrue(empty.Count == 0, "找不到節點時應回傳空列表而非拋出例外");
        }

        [TestMethod]
        public async Task FlattenFrameTree_ShouldReturnAllNestedFrames()
        {
            // Arrange
            var testHtml = @"
                <html><body>
                    <iframe id='frame1' src='data:text/html,<html><body>frame1_content</body></html>'></iframe>
                    <iframe id='frame2' src='data:text/html,<html><body>frame2_content</body></html>'></iframe>
                </body></html>";

            await _tab!.GetAsync($"data:text/html,{testHtml}");
            await _tab.WaitAsync(0.5);

            var result = await _tab.SendAsync(Cdp.Page.GetFrameTree());
            var frameTree = result.FrameTree;

            // Act
            var flattened = Util.FlattenFrameTree(frameTree).ToList();

            // Assert
            Assert.AreEqual(3, flattened.Count, "應該找到 1 個主 Frame 和 2 個子 Frame");

            var mainFrame = flattened.FirstOrDefault(it => it.ParentId == null);
            Assert.IsNotNull(mainFrame, "應該包含主 Frame");

            var subFrames = flattened.Where(it => it.ParentId != null).ToList();
            Assert.AreEqual(2, subFrames.Count, "應該包含 2 個子 Frame");
        }

        [TestMethod]
        public async Task FlattenFrameTree_ResourceTree_ShouldReturnAllFrames()
        {
            // Arrange
            var testHtml = @"
                <html><body>
                    <iframe id='frame1' src='data:text/html,<html><body>frame1_content</body></html>'></iframe>
                    <iframe id='frame2' src='data:text/html,<html><body>frame2_content</body></html>'></iframe>
                </body></html>";

            await _tab!.GetAsync($"data:text/html,{testHtml}");
            await _tab.WaitAsync(0.5);

            var result = await _tab.SendAsync(Cdp.Page.GetResourceTree());
            var resourceTree = result.FrameTree;

            // Act
            var flattened = Util.FlattenFrameTree(resourceTree).ToList();

            // Assert
            Assert.AreEqual(3, flattened.Count, "應該找到 1 個主 Frame 和 2 個子 Frame");

            var mainFrame = flattened.FirstOrDefault(it => it.ParentId == null);
            Assert.IsNotNull(mainFrame, "應該包含主 Frame");

            var subFrames = flattened.Where(it => it.ParentId != null).ToList();
            Assert.AreEqual(2, subFrames.Count, "應該包含 2 個子 Frame");
        }

        [TestMethod]
        public async Task FlattenFrameTreeResources_ShouldReturnAllResourcesWithFrames()
        {
            // Arrange
            var testHtml = @"
                <html><head>
                    <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/mocha/10.2.0/mocha.min.css'>
                </head><body>
                    <img src='https://www.google.com/images/branding/googlelogo/1x/googlelogo_color_272x92dp.png'>
                    <iframe src='data:text/html,<html><body><script src=""https://cdnjs.cloudflare.com/ajax/libs/jquery/3.7.1/jquery.min.js""></script></body></html>'></iframe>
                </body></html>";

            await _tab!.GetAsync($"data:text/html,{testHtml}");
            await _tab.WaitAsync(0.5);

            var result = await _tab.SendAsync(Cdp.Page.GetResourceTree());
            var resourceTree = result.FrameTree;

            // Act
            var flattened = Util.FlattenFrameTreeResources(resourceTree).ToList();

            // Assert
            Assert.AreEqual(3, flattened.Count, "應包含主框架 css、img 和子框架 script");

            // 驗證資源與 Frame 的關聯性
            foreach (var (frame, resource) in flattened)
            {
                Assert.IsNotNull(frame.Id, "Frame Id 不應為空");
                Assert.IsNotNull(resource.Url, "Resource URL 不應為空");
            }

            var resources = flattened.Where(it => it.Frame.ParentId != null).ToList();
            Assert.AreEqual(1, resources.Count, "應包含來自子框架的 script 資源");
        }

        [TestMethod]
        public async Task HtmlFromTreeAsync_ShouldConcatenateAllChildrenHtml()
        {
            // Arrange
            // 建立一個三層嵌套結構：div > p > span
            var testHtml = @"
                <html><body>
                    <div id='root'>
                        <p id='child_p'>
                            <span id='grandchild_span'>Text</span>
                        </p>
                        <footer id='child_footer'>Footer</footer>
                    </div>
                </body></html>";

            await _tab!.GetAsync($"data:text/html,{testHtml}");
            await _tab.WaitAsync(0.5);

            var root = await _tab.QuerySelectorAsync("#root");
            Assert.IsNotNull(root);

            // Act
            var html = await Util.HtmlFromTreeAsync(root, _tab);

            // Assert
            // 驗證是否包含第一層子節點 p 的 HTML
            Assert.IsTrue(html.Contains(@"<p id=""child_p"">"), "應包含第一層子節點 p 標籤");

            // 驗證是否包含第二層子節點 span 的內容 (遞迴深度驗證)
            Assert.IsTrue(html.Contains(@"<span id=""grandchild_span"">Text</span>"), "應包含第二層子節點 span 標籤");

            // 驗證是否包含同層的另一個子節點 footer
            Assert.IsTrue(html.Contains(@"<footer id=""child_footer"">Footer</footer>"), "應包含同層的 footer 標籤");

            // 根據實作邏輯，HtmlFromTreeAsync 拼接的是「Children」的 HTML，不應包含 root 標籤本身
            Assert.IsFalse(html.StartsWith(@"<div id=""root"">"), "結果不應包含根節點本身的標籤");
        }

        [TestMethod]
        public async Task HtmlFromTreeAsync_NodeVersion_ShouldConcatenateAllChildrenHtml()
        {
            // Arrange
            // 建立一個具有嵌套結構的 HTML
            var testHtml = @"
                <html><body>
                    <div id='parent'>
                        <section id='child1'>
                            <span>Text1</span>
                        </section>
                        <section id='child2'>
                            <div>Text2</div>
                        </section>
                    </div>
                </body></html>";

            await _tab!.GetAsync($"data:text/html,{testHtml}");
            await _tab.WaitAsync(0.5);

            var doc = await _tab.SendAsync(Cdp.DOM.GetDocument(-1, true));
            var parent = Util.FilterRecurse(doc.Root, n => n.Attributes?.Contains("parent") == true);
            Assert.IsNotNull(parent);

            // Act
            var html = await Util.HtmlFromTreeAsync(parent, _tab);

            // Assert
            // 驗證是否抓到第一層子節點 child1 的 HTML
            Assert.IsTrue(html.Contains(@"<section id=""child1"">"), "結果應包含 child1");

            // 驗證是否抓到第一層子節點 child2 的 HTML
            Assert.IsTrue(html.Contains(@"<section id=""child2"">"), "結果應包含 child2");

            // 驗證是否包含 child1 內部的 span
            Assert.IsTrue(html.Contains(@"<span>Text1</span>"), "應包含 child1 內部的 span 內容");

            // 驗證是否包含 child2 內部的 div
            Assert.IsTrue(html.Contains(@"<div>Text2</div>"), "應包含 child2 內部的 div 內容");

            // 此方法拼接的是 tree.Children，所以不應包含 parent 標籤本身
            Assert.IsFalse(html.Contains(@"<div id=""parent"">"), "結果不應包含根節點本身的標籤");
        }
    }
}
