using NoDriver.Core.Messaging;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json.Nodes;

namespace NoDriver.Core.Runtime
{
    public class Tab : Connection
    {
        private int? _windowId = null;
        private Cdp.DOM.Node? _dom = null;
        private List<string> _downloadBehavior = new();

        private bool _prepHeadlessDone = false;
        private bool _prepExpertDone = false;

        public string InspectorUrl
            => $"http://{Browser.Config.Host}:{Browser.Config.Port}/devtools/inspector.html?ws={WebSocketUrl.Substring(5)}";

        public Tab(string webSocketUrl, Cdp.Target.TargetInfo? target = null, Browser? browser = null) : 
            base(webSocketUrl, target, browser)
        {
        }

        public void InspectorOpen()
        {
            Process.Start(new ProcessStartInfo(InspectorUrl) { UseShellExecute = true });
        }

        public Task OpenExternalInspectorAsync()
        {
            InspectorOpen();
            return Task.CompletedTask;
        }

        // ok
        public async Task<TResponse> FeedCdpAsync<TResponse>(
            ICommand<TResponse> command, CancellationToken token = default) where TResponse : IType
        {
            return await SendOneshotAsync(command, token);
        }

        private async Task PrepareHeadlessAsync(CancellationToken token = default)
        {
            if (_prepHeadlessDone) 
                return;

            var response = await SendOneshotAsync(Cdp.Runtime.Evaluate("navigator.userAgent"), token);
            if (response == null) return;

            var (response, error) = resp;
            if (response?.Value != null)
            {
                string ua = response.Value.ToString();
                await SendOneshotAsync(Network.SetUserAgentOverride(ua.Replace("Headless", "")), token);
            }
            _prepHeadlessDone = true;
        }

        private async Task PrepareExpertAsync(CancellationToken token = default)
        {
            if (_prepExpertDone) 
                return;

            if (Browser != null)
            {
                await SendOneshotAsync(Page.Enable(), token);
                await SendOneshotAsync(Page.AddScriptToEvaluateOnNewDocument(@"
                    console.log('hooking attachShadow');
                    Element.prototype._attachShadow = Element.prototype.attachShadow;
                    Element.prototype.attachShadow = function () {
                        console.log('calling hooked attachShadow')
                        return this._attachShadow( { mode: 'open' } );
                    };"), token);
            }
            _prepExpertDone = true;
        }

        //ok
        public async Task<Element?> FindAsync(string text, bool bestMatch = true, bool returnEnclosingElement = true, double timeout = 10, CancellationToken token = default)
        {
            var sw = Stopwatch.StartNew();
            text = text.Trim();

            var item = await FindElementByTextAsync(text, bestMatch, returnEnclosingElement, token: token);
            while (item == null)
            {
                await WaitAsync(token: token);
                item = await FindElementByTextAsync(text, bestMatch, returnEnclosingElement, token: token);
                if (sw.Elapsed.TotalSeconds > timeout)
                    return item;
                await WaitAsync(0.5, token: token);
            }
            return item;
        }

        //ok
        public async Task<Element?> SelectAsync(string selector, double timeout = 10, CancellationToken token = default)
        {
            var sw = Stopwatch.StartNew();
            selector = selector.Trim();

            var item = await QuerySelectorAsync(selector, token: token);
            while (item == null)
            {
                await WaitAsync(token: token);
                item = await QuerySelectorAsync(selector, token: token);
                if (sw.Elapsed.TotalSeconds > timeout)
                    return item;
                await WaitAsync(0.5, token: token);
            }
            return item;
        }

        public async Task<List<Element>> FindAllAsync(string text, double timeout = 10)
        {
            var sw = Stopwatch.StartNew();
            text = text.Trim();

            var items = await FindElementsByTextAsync(text);
            while (items == null || items.Count == 0)
            {
                await WaitAsync();
                items = await FindElementsByTextAsync(text);
                if (sw.Elapsed.TotalSeconds > timeout)
                    return items;
                await SleepAsync(0.5);
            }
            return items;
        }

        //ok
        public async Task<List<Element>> SelectAllAsync(string selector, double timeout = 10, bool includeFrames = false, CancellationToken token = default)
        {
            var sw = Stopwatch.StartNew();
            selector = selector.Trim();

            var items = new List<Element>();
            if (includeFrames)
            {
                var frames = await QuerySelectorAllAsync("iframe", token: token);
                foreach (var fr in frames)
                {
                    items.AddRange(await fr.QuerySelectorAllAsync(selector, token: token));
                }
            }

            items.AddRange(await QuerySelectorAllAsync(selector, token: token));
            while (items.Count == 0)
            {
                await WaitAsync(token: token);
                items.AddRange(await QuerySelectorAllAsync(selector, token: token));
                if (sw.Elapsed.TotalSeconds > timeout)
                    return items;
                await WaitAsync(0.5, token);
            }
            return items;
        }

        //ok
        public async Task WaitAsync(double time = 0.5, CancellationToken token = default)
        {
            if (Browser != null)
            {
                await Task.WhenAll(
                    Browser.UpdateTargetsAsync(token),
                    Task.Delay(TimeSpan.FromSeconds(time), token));
            }
        }

        public async Task<List<Element>> XPathAsync(string xpath, double timeout = 2.5)
        {
            var items = new List<Element>();
            try
            {
                await SendAsync(Enable(), true);
                items = await FindAllAsync(xpath, timeout: 0);
                if (items.Count == 0)
                {
                    var sw = Stopwatch.StartNew();
                    while (items.Count == 0)
                    {
                        items = await FindAllAsync(xpath, timeout: 0);
                        await SleepAsync(0.1);
                        if (sw.Elapsed.TotalSeconds > timeout)
                            break;
                    }
                }
            }
            finally
            {
                try
                {
                    await SendAsync(Disable(), true);
                }
                catch (ProtocolErrorException) { }
            }
            return items;
        }

        public async Task<Tab> GetAsync(string url = "chrome://welcome", bool newTab = false, bool newWindow = false)
        {
            if (Browser == null)
                throw new InvalidOperationException("This tab has no browser attribute.");

            if (newWindow && !newTab) 
                newTab = true;

            if (newTab)
            {
                return await Browser.GetAsync(url, newTab, newWindow);
            }
            else
            {
                var result = await SendAsync(Page.Navigate(url));
                await WaitAsync();
                return this;
            }
        }

        public async Task<List<Element>> QuerySelectorAllAsync(string selector, Cdp.DOM.Node node = null)
        {
            var doc = null as Cdp.DOM.Node;
            if (node == null)
            {
                doc = await SendAsync(GetDocument(-1, true));
            }
            else
            {
                doc = node;
                if (node.NodeName == "IFRAME")
                    doc = node.ContentDocument;
            }

            var nodeIds = new List<int>();
            try
            {
                nodeIds = await SendAsync(QuerySelectorAll(doc.NodeId, selector));
            }
            //except AttributeError:
            //# has no content_document
            //return
            //catch (Exception e) when (e.Message.Contains("could not find node", StringComparison.OrdinalIgnoreCase))
            catch(ProtocolErrorException ex)
            {
                if (node != null)
                {
                    if (ex.Message.ToLowerInvariant().Contains("could not find node"))
                    {
                        if (node.GetMetadata("__last") != null)
                        {
                            node.RemoveMetadata("__last");
                            return new List<Element>();
                        }
                        await node.UpdateAsync();
                        node.SetMetadata("__last", true);
                        return await QuerySelectorAllAsync(selector, node);
                    }
                }
                else
                {
                    await SendAsync(Disable());
                    throw;
                }
            }

            if (nodeIds == null || nodeIds.Count == 0) 
                return new List<Element>();

            var items = new List<Element>();
            foreach (var nid in nodeIds)
            {
                var _node = Util.FilterRecurse(doc, n => n.NodeId == nid);
                if (_node == null) 
                    continue;
                items.Add(Element.Create(_node, this, doc));
            }
            return items;
        }

        public async Task<Element> QuerySelectorAsync(string selector, Cdp.DOM.Node? node = null)
        {
            selector = selector.Trim();

            var doc = null as Cdp.DOM.Node;
            if (node == null)
            {
                var result = await SendAsync(Cdp.DOM.GetDocument(-1, true));
                doc = result.Root;
            }
            else
            {
                doc = node;
                if (node.NodeName == "IFRAME")
                    doc = node.ContentDocument;
            }

            int? nodeId = null;
            try
            {
                nodeId = await SendAsync(QuerySelectorAsync(doc.NodeId, selector));
            }
            catch (ProtocolErrorException ex)
            {
                if (node != null)
                {
                    if (ex.Message.ToLowerInvariant().Contains("could not find node"))
                    {
                        if (node.GetMetadata("__last") != null)
                        {
                            node.RemoveMetadata("__last");
                            return null;
                        }
                        await node.UpdateAsync();
                        node.SetMetadata("__last", true);
                        return await QuerySelectorAsync(selector, node);
                    }
                }
                else
                {
                    await SendAsync(Disable());
                    throw;
                }
            }

            if (nodeId == null) 
                return null;

            var _node = Util.FilterRecurse(doc, n => n.NodeId == nodeId);
            if (_node == null)
                return null;
            return Element.Create(_node, this, doc);
        }

        public async Task<List<Element>> FindElementsByTextAsync(string text, string? tagHint = null)
        {
            text = text.Trim();
            var doc = await SendAsync(GetDocument(-1, true));
            var searchResult = await SendAsync(PerformSearch(text, true));
            var searchId = searchResult.SearchId;
            var nresult = searchResult.ResultCount;

            var nodeIds = new List<int>();
            if (nresult > 0)
                nodeIds = await SendAsync(DOM.GetSearchResults(searchId, 0, nresult));

            await SendAsync(DOM.DiscardSearchResults(searchId));

            var items = new List<Element>();
            foreach (var nid in nodeIds)
            {
                var node = Util.FilterRecurse(doc, n => n.NodeId == nid);
                if (node == null)
                {
                    node = await SendAsync(DOM.ResolveNode(nodeId: nid));
                    if (node == null)
                        continue;
                }

                var elem = null as Element;
                try 
                { 
                    elem = Element.Create(node, this, doc); 
                }
                catch
                { 
                    continue; 
                }

                if (elem.NodeType == 3) // Text node
                {
                    if (elem.Parent == null) 
                        await elem.UpdateAsync();
                    items.Add(elem.Parent ?? elem);
                }
                else
                {
                    items.Add(elem);
                }
            }

            var iframes = Util.FilterRecurseAll(doc, n => n.NodeName == "IFRAME");
            foreach (var iframe in iframes)
            {
                var iframeElem = Element.Create(iframe, this, iframe.ContentDocument);
                if (iframeElem.ContentDocument != null)
                {
                    var iframeTextNodes = Util.FilterRecurseAll(iframeElem, n => n.NodeType == 3 && n.NodeValue.Lower().Contains(text));
                    foreach (var textNode in iframeTextNodes)
                    {
                        var textElem = Element.Create(textNode, this, iframeElem.Tree);
                        if (textElem.Parent != null) 
                            items.Add(textElem.Parent);
                    }
                }
            }

            await SendAsync(Disable());
            return items;
        }

        public async Task<Element?> FindElementByTextAsync(string text, bool bestMatch = false, bool returnEnclosingElement = true)
        {
            var items = await FindElementsByTextAsync(text);
            if (items == null || items.Count == 0) return null;

            if (bestMatch)
            {
                return items.OrderBy(el => Math.Abs(text.Length - (el.TextAll?.Length ?? 0))).FirstOrDefault();
                //elem = closest_by_length or items[0]
            }
            return items.FirstOrDefault();
            //for elem in items:
            //    if elem:
            //        return elem
            //這裡感覺可能有 null 需要另外處理 ??
        }

        //ok
        public async Task BackAsync(CancellationToken token = default)
        {
            await SendAsync(Cdp.Runtime.Evaluate("window.history.back()"), token: token);
        }

        public async Task ForwardAsync() 
        {
            await SendAsync(Cdp.Runtime.Evaluate("window.history.forward()"));
        }

        //ok
        public async Task ReloadAsync(bool ignoreCache = true, string? scriptToEvaluateOnLoad = null, CancellationToken token = default)
        {
            await SendAsync(Cdp.Page.Reload(IgnoreCache: ignoreCache, ScriptToEvaluateOnLoad: scriptToEvaluateOnLoad), token: token);
        }

        //ok
        public async Task<(Cdp.Runtime.RemoteObject remoteObject, Cdp.Runtime.ExceptionDetails? exception)> EvaluateAsync(string expression, bool awaitPromise = false, bool returnByValue = false, CancellationToken token = default)
        {
            var ser = new Cdp.Runtime.SerializationOptions(
                Serialization: "deep",
                MaxDepth: 10,
                AdditionalParameters: new JsonObject
                {
                    ["maxNodeDepth"] = 10,
                    ["includeShadowTree"] = "all"
                });

            var result = await SendAsync(Cdp.Runtime.Evaluate(
                Expression: expression,
                UserGesture: true,
                AwaitPromise: awaitPromise,
                ReturnByValue: returnByValue,
                AllowUnsafeEvalBlockedByCSP: true,
                SerializationOptions: ser), token: token);

            return (result.Result, result.ExceptionDetails);
        }

        public async Task<object> JsDumpsAsync(string objName, bool returnByValue = true)
        {
            string jsCodeA = $$"""
                function ___dump(obj, _d = 0) { ... } // (JS snippet truncated for brevity)
                ___dumpY({{objName}})
            """;

            string jsCodeB = $$"""
                ((obj, visited = new WeakSet()) => { ... })({{objName}})
            """;

            var (remoteObject, exceptionDetails) = 
                await SendAsync(Cdp.Runtime.Evaluate(jsCodeA, awaitPromise: true, returnByValue: returnByValue, allowUnsafeEvalBlockedByCsp: true));
            if (exceptionDetails != null)
            {
                (remoteObject, exceptionDetails) = 
                    await SendAsync(Cdp.Runtime.Evaluate(jsCodeB, awaitPromise: true, returnByValue: returnByValue, allowUnsafeEvalBlockedByCsp: true));
            }

            if (exceptionDetails != null)
                throw new ProtocolErrorException(exceptionDetails.ToString());

            if (returnByValue)
                return remoteObject?.Value;
            return (remoteObject, exceptionDetails);
        }
        
        //ok
        public async Task CloseAsync(CancellationToken token = default)
        {
            if (Target?.TargetId != null)
                await SendAsync(Cdp.Target.CloseTarget(Target.TargetId), token: token);
        }

        //ok
        public async Task<(Cdp.Browser.WindowID windowId, Cdp.Browser.Bounds bounds)?> GetWindowAsync(CancellationToken token = default)
        {
            if (Target?.TargetId == null)
                return null;
            var result = await SendAsync(Cdp.Browser.GetWindowForTarget(Target.TargetId), token: token);
            return (result.WindowId, result.Bounds);
        }

        //ok
        public async Task<string> GetContentAsync(CancellationToken token = default)
        {
            var docResult = await SendAsync(Cdp.DOM.GetDocument(-1, true), token: token);
            var doc = docResult.Root;
            var result = await SendAsync(Cdp.DOM.GetOuterHTML(BackendNodeId: doc.BackendNodeId), token: token);
            return result.OuterHTML;
        }

        //ok
        public async Task MaximizeAsync(CancellationToken token = default)
        {
            await SetWindowStateAsync(state: "maximize", token: token);
        }

        //ok
        public async Task MinimizeAsync(CancellationToken token = default)
        {
            await SetWindowStateAsync(state: "minimize", token: token);
        }

        //ok
        public async Task FullscreenAsync(CancellationToken token = default)
        {
            await SetWindowStateAsync(state: "fullscreen", token: token);
        }

        //ok
        public async Task MedimizeAsync(CancellationToken token = default)
        {
            await SetWindowStateAsync(state: "normal", token: token);
        }

        //ok
        public async Task SetWindowSizeAsync(int left = 0, int top = 0, int width = 1280, int height = 1024, CancellationToken token = default)
        {
            await SetWindowStateAsync(left, top, width, height, token: token);
        }

        //ok
        public async Task ActivateAsync(CancellationToken token = default)
        {
            if (Target?.TargetId != null)
                await SendAsync(Cdp.Target.ActivateTarget(Target.TargetId), token: token);
        }

        //ok
        public async Task BringToFrontAsync(CancellationToken token = default)
        {
            await ActivateAsync(token);
        }

        //ok
        public async Task SetWindowStateAsync(int left = 0, int top = 0, int width = 1280, int height = 720, string state = "normal", CancellationToken token = default)
        {
            var availableStates = new[]
            {
                "minimized", "maximized", "fullscreen", "normal"
            };
            var stateName = availableStates.FirstOrDefault(it => it.Contains(state.ToLowerInvariant()));
            if (stateName == null)
                throw new ArgumentException($"Could not determine any of {string.Join(",", availableStates)} from input '{state}'");

            var result = await GetWindowAsync(token);
            if (result == null)
                return;
            var (windowId, _) = result.Value;

            if (stateName == "normal")
            {
                var bounds = new Cdp.Browser.Bounds(left, top, width, height, new("normal"));
                await SendAsync(Cdp.Browser.SetWindowBounds(windowId, bounds), token: token);
            }
            else
            {
                await SetWindowStateAsync(state: "normal");
                var bounds = new Cdp.Browser.Bounds(WindowState: new(stateName));
                await SendAsync(Cdp.Browser.SetWindowBounds(windowId, bounds), token: token);
            }
        }

        //ok
        public async Task ScrollDownAsync(int amount = 25, CancellationToken token = default)
        {
            var result = await GetWindowAsync(token);
            if (result == null)
                return;
            var (_, bounds) = result.Value;

            await SendAsync(Input.SynthesizeScrollGesture(
                0, 0,
                XDistance: -(bounds.Height * (amount / 100.0)),
                YOverscroll: 0,
                XOverscroll: 0,
                PreventFling: true,
                RepeatDelayMs: 0,
                Speed: 7777), token: token);
        }

        //ok
        public async Task ScrollUpAsync(int amount = 25, CancellationToken token = default)
        {
            var result = await GetWindowAsync(token);
            if (result == null)
                return;
            var (_, bounds) = result.Value;

            await SendAsync(Input.SynthesizeScrollGesture(
                0, 0,
                YDistance: bounds.Height * (amount / 100.0),
                XOverscroll: 0,
                PreventFling: true,
                RepeatDelayMs: 0,
                Speed: 7777), token: token);
        }

        public async Task<Element?> WaitForAsync(string selector = "", string text = "", double timeout = 10)
        {
            var sw = Stopwatch.StartNew();
            if (!string.IsNullOrWhiteSpace(selector))
            {
                var item = await QuerySelectorAsync(selector);
                while (item == null)
                {
                    item = await QuerySelectorAsync(selector);

                    if (sw.Elapsed.TotalSeconds > timeout) 
                        throw new TimeoutException($"Time ran out while waiting for {selector}");
                    await SleepAsync(0.5);
                }
                return item;
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                var item = await FindElementByTextAsync(text);
                while (item == null)
                {
                    item = await FindElementByTextAsync(text);

                    if (sw.Elapsed.TotalSeconds > timeout) 
                        throw new TimeoutException($"Time ran out while waiting for text: {text}");
                    await SleepAsync(0.5);
                }
                return item;
            }
            return null;
        }

        public async Task DownloadFileAsync(string url, string? filename = null)
        {
            if (_downloadBehavior == null)
            {
                var dirPath = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
                Directory.CreateDirectory(dirPath);
                await SetDownloadPathAsync(dirPath);
                Console.WriteLine($"No download path set, using default: {dirPath}");
            }

            if (string.IsNullOrWhiteSpace(filename))
                filename = url.Split('/').Last().Split('?').First();

            var code = $$"""
                (elem) => {
                    async function _downloadFile(
                      imageSrc,
                      nameOfDownload,
                    ) {
                      const response = await fetch(imageSrc);
                      const blobImage = await response.blob();
                      const href = URL.createObjectURL(blobImage);

                      const anchorElement = document.createElement('a');
                      anchorElement.href = href;
                      anchorElement.download = nameOfDownload;

                      document.body.appendChild(anchorElement);
                      anchorElement.click();

                      setTimeout(() => {
                        document.body.removeChild(anchorElement);
                        window.URL.revokeObjectURL(href);
                        }, 500);
                    }
                    _downloadFile('{{url}}', '{{filename}}')
                    }
            """;

            var body = (await QuerySelectorAllAsync("body")).First();
            await body.UpdateAsync();
            await SendAsync(Cdp.Runtime.CallFunctionOn(code, body.ObjectId, 
                new List<Cdp.Runtime.CallArgument> { new Cdp.Runtime.CallArgument(body.ObjectId) }));
            await WaitAsync(0.1);
        }

        //ok
        public async Task<string> SaveScreenshotAsync(string filename = "auto", string format = "jpeg", bool fullPage = false, CancellationToken token = default)
        {
            await WaitAsync(1, token: token);

            var path = "";
            if (string.IsNullOrWhiteSpace(filename) || filename == "auto")
            {
                if (Target != null)
                {
                    var uri = new Uri(Target.Url);
                    var lastPart = uri.AbsolutePath.Split('/').Last();
                    var index = lastPart.LastIndexOf('?');
                    if (index != -1)
                        lastPart = lastPart.Substring(0, index);
                    var dtStr = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var candidate = $"{uri.Host}__{lastPart}_{dtStr}";

                    var ext = "";
                    if (format.ToLowerInvariant() == "jpg" ||
                        format.ToLowerInvariant() == "jpeg")
                    {
                        ext = ".jpg";
                        format = "jpeg";
                    }
                    if (format.ToLowerInvariant() == "png")
                    {
                        ext = ".png";
                        format = "png";
                    }

                    path = Path.Combine(Directory.GetCurrentDirectory(), $"{candidate}{ext}");
                }
            }
            else
            {
                path = Path.Combine(Directory.GetCurrentDirectory(), filename);
            }

            if (string.IsNullOrWhiteSpace(path))
                throw new Exception($"Invalid filename or path: '{filename}'");

            var parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentDir))
                Directory.CreateDirectory(parentDir);

            var result = await SendAsync(Cdp.Page.CaptureScreenshot(format, CaptureBeyondViewport: fullPage), token: token);
            var base64Data = result.Data;
            if (string.IsNullOrWhiteSpace(base64Data))
                throw new InvalidOperationException("Could not take screenshot. most possible cause is the page has not finished loading yet.");

            var dataBytes = Convert.FromBase64String(base64Data);
            await File.WriteAllBytesAsync(path, dataBytes, token);
            return path;
        }

        public async Task SetDownloadPathAsync(string path)
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
            await SendAsync(Cdp.Browser.SetDownloadBehavior("allow", fullPath));
            _downloadBehavior = new List<string> { "allow", fullPath };
        }

        public async Task<List<Element>> GetAllLinkedSourcesAsync()
        {
            return await QuerySelectorAllAsync("a,link,img,script,meta");
        }

        public async Task<List<string>> GetAllUrlsAsync(bool absolute = true)
        {
            var res = new List<string>();
            var allAssets = await QuerySelectorAllAsync("a,link,img,script,meta");

            foreach (var asset in allAssets)
            {
                if (!absolute)
                {
                    // 假設 Element 內有對應的屬性或可以從 Attrs 取出
                    res.Add(asset.Src ?? asset.Href);
                }
                else
                {
                    // 假設 asset.Attrs 是一個 Dictionary<string, string>
                    foreach (var kvp in asset.Attrs)
                    {
                        var k = kvp.Key;
                        var v = kvp.Value;

                        if (k == "src" || k == "href")
                        {
                            if (v.Contains("#")) 
                                continue;

                            var validStarts = new List<string> { "http", "//", "/" };
                            if (!validStarts.Any(it => v.Contains(it)))
                                continue;

                            if (Target == null)
                                continue;

                            var baseUri = new Uri(Target.Url);
                            var baseUrl = $"{baseUri.Scheme}://{baseUri.Host}";

                            if (Uri.TryCreate(new Uri(baseUrl), v, out var absUri))
                            {
                                var absUrl = absUri.ToString();
                                if (absUrl.StartsWith("http") || absUrl.StartsWith("//") || absUrl.StartsWith("ws"))
                                {
                                    res.Add(absUrl);
                                }
                            }
                        }
                    }
                }
            }
            return res;
        }

        public async Task<Dictionary<string, string>> GetLocalStorageAsync()
        {
            if (string.IsNullOrWhiteSpace(Target.Url))
            {
                await WaitAsync();
            }

            var origin = new Uri(Target.Url).GetLeftPart(UriPartial.Authority);

            var storageId = new DOMStorage.StorageId 
            { 
                IsLocalStorage = true, 
                SecurityOrigin = origin 
            };
            var items = await SendAsync(DOMStorage.GetDOMStorageItems(storageId));

            var retval = new Dictionary<string, string>();
            foreach (var item in items)
            {
                if (item != null && item.Count >= 2)
                {
                    retval[item[0]] = item[1];
                }
            }
            return retval;
        }

        public async Task SetLocalStorageAsync(Dictionary<string, object> items)
        {
            if (string.IsNullOrWhiteSpace(Target.Url))
            {
                await WaitAsync();
            }

            var origin = new Uri(Target.Url).GetLeftPart(UriPartial.Authority);

            var storageId = new DOMStorage.StorageId
            {
                SecurityOrigin = origin,
                IsLocalStorage = true
            };

            var tasks = items.Select(kvp =>
                SendAsync(DOMStorage.SetDOMStorageItem(
                    storageId,
                    kvp.Key,
                    kvp.Value
                ))
            );
            await Task.WhenAll(tasks);
        }

        public async Task<Page.FrameTree> GetFrameTreeAsync()
        {
            return await SendAsync(Page.GetFrameTree());
        }

        public async Task<Page.FrameResourceTree> GetFrameResourceTreeAsync()
        {
            return await SendAsync(Page.GetResourceTree());
        }

        public async Task<List<string>> GetFrameResourceUrlsAsync()
        {
            var tree = await GetFrameResourceTreeAsync();

            var flatResources = Util.FlattenFrameTreeResources(tree);

            return flatResources
                .Where(r => r.Resource != null && !string.IsNullOrEmpty(r.Resource.Url))
                .Select(r => r.Resource.Url)
                .ToList();

            //return [
            //    x
            //    for x in functools.reduce(
            //        lambda a, b: a + [b[1].url if isinstance(b, tuple) else ""],
            //        util.flatten_frame_tree_resources(_tree),
            //        [],
            //    )
            //    if x
            //]
        }

        public async Task<Dictionary<string, List<Cdp.Debugger.SearchMatch>>> SearchFrameResourcesAsync(string query)
        {
            try
            {
                await SendOneshotAsync(Page.Enable());

                var tree = await GetFrameResourceTreeAsync();
                var listOfTuples = Util.FlattenFrameTreeResources(tree);

                var results = new Dictionary<string, List<Cdp.Debugger.SearchMatch>>();
                foreach (var item in listOfTuples)
                {
                    var frame = item.Frame;
                    var resource = item.Resource;

                    if (frame == null || resource == null) 
                        continue;

                    var res = await SendAsync(Page.SearchInResource(frame.Id, resource.Url, query));
                    if (res != null && res.Count > 0)
                    {
                        results[resource.Url] = res;
                    }
                }
                return results;
            }
            finally
            {
                await SendOneshotAsync(Page.Disable());
            }
        }

        public async Task VerifyCfAsync(string? templateImage = null, bool flash = false)
        {
            if (Browser?.Config?.Expert == true)
                throw new Exception("This function is useless in expert mode...");

            var loc = await TemplateLocationAsync(templateImage);
            if (loc != null)
            {
                await MouseClickAsync(loc.Value.X, loc.Value.Y);
                if (flash) 
                    await FlashPointAsync(loc.Value.X, loc.Value.Y);
            }
        }

        public async Task<(int X, int Y)?> TemplateLocationAsync(string? templateImage = null)
        {
            var screenPath = "screen.jpg";
            var cfTemplatePath = "cf_template.png";
            try
            {
                if (!string.IsNullOrWhiteSpace(templateImage))
                    if (!File.Exists(templateImage))
                    throw new FileNotFoundException($"{templateImage} was not found.");

                await SaveScreenshotAsync(screenPath);
                await SleepAsync(0.05);

                using var im = Cv2.ImRead(screenPath);
                using var imGray = new Mat();
                Cv2.CvtColor(im, imGray, ColorConversionCodes.BGR2GRAY);

                Mat template;
                if (!string.IsNullOrWhiteSpace(templateImage))
                {
                    template = Cv2.ImRead(templateImage);
                }
                else
                {
                    File.WriteAllBytes(cfTemplatePath, Util.GetCfTemplate());
                    template = Cv2.ImRead(cfTemplatePath);
                }

                using var templateGray = new Mat();
                Cv2.CvtColor(template, templateGray, ColorConversionCodes.BGR2GRAY);

                using var match = new Mat();
                Cv2.MatchTemplate(imGray, templateGray, match, TemplateMatchModes.CCoeffNormed);

                Cv2.MinMaxLoc(match, out _, out _, out _, out Point maxL);

                int tmpW = templateGray.Width;
                int tmpH = templateGray.Height;
                int cx = maxL.X + tmpW / 2;
                int cy = maxL.Y + tmpH / 2;

                return (cx, cy);
            }
            catch (Exception ex)
            {
                //Logger.LogError(ex, "OpenCV template matching failed.");
                throw;
            }
            finally
            {
                if (File.Exists(screenPath)) 
                    File.Delete(screenPath);
                if (string.IsNullOrWhiteSpace(templateImage))
                {
                    if(File.Exists(cfTemplatePath))
                        File.Delete(cfTemplatePath);
                }
            }
        }

        public async Task BypassInsecureConnectionWarningAsync()
        {
            var body = await SelectAsync("body");
            await body.SendKeysAsync("thisisunsafe");
        }

        //ok
        public async Task MouseMoveAsync(double x, double y, int steps = 10, bool flash = false, CancellationToken token = default)
        {
            steps = steps < 1 ? 1 : steps;
            if (steps > 1)
            {
                var stepSizeX = Math.Floor(x / steps);
                var stepSizeY = Math.Floor(y / steps);
                for (var i = 0; i <= steps; i++)
                {
                    var currentX = stepSizeX * i;
                    var currentY = stepSizeY * i;
                    if (flash) 
                        await FlashPointAsync(currentX, currentY, token: token);
                    await SendAsync(Cdp.Input.DispatchMouseEvent("mouseMoved", X: currentX, Y: currentY), token: token);
                }
            }
            else
            {
                await SendAsync(Cdp.Input.DispatchMouseEvent("mouseMoved", X: x, Y: y), token: token);
            }

            if (flash) 
                await FlashPointAsync(x, y, token: token);
            else 
                await WaitAsync(0.05, token: token);

            await SendAsync(Cdp.Input.DispatchMouseEvent("mouseReleased", X: x, Y: y), token: token);
            if (flash) 
                await FlashPointAsync(x, y, token: token);
        }

        //ok 要檢查 value 是不是有正確轉換
        public async Task<bool> ScrollBottomReachedAsync(CancellationToken token = default)
        {
            var (remoteObj, exception) = await EvaluateAsync(
                "document.body.offsetHeight - window.innerHeight == window.scrollY", token: token);

            var value = remoteObj?.Value?.ToString();

            if (!string.IsNullOrWhiteSpace(value))
            {
                if (bool.TryParse(value, out var isReached))
                {
                    return isReached;
                }
            }
            return false;
        }

        public async Task MouseClickAsync(double x, double y, string button = "left", int buttons = 1, int modifiers = 0)
        {
            await SendAsync(Input.DispatchMouseEvent("mousePressed", x, y,
                modifiers: modifiers, button: new Input.MouseButton(button), buttons: buttons, clickCount: 1));
            await SendAsync(Input.DispatchMouseEvent("mouseReleased", x, y,
                modifiers: modifiers, button: new Input.MouseButton(button), buttons: buttons, clickCount: 1));
        }

        //ok
        public async Task MouseDragAsync((double X, double Y) sourcePoint, (double X, double Y) destPoint, bool relative = false, int steps = 1, CancellationToken token = default)
        {
            if (relative)
                destPoint = (sourcePoint.X + destPoint.X, sourcePoint.Y + destPoint.Y);

            await SendAsync(Input.DispatchMouseEvent("mousePressed",
                X: sourcePoint.X, Y: sourcePoint.Y, Button: new Input.MouseButton("left")), token: token);

            steps = steps < 1 ? 1 : steps;
            if (steps == 1)
            {
                await SendAsync(Input.DispatchMouseEvent("mouseMoved", X: destPoint.X, Y: destPoint.Y), token: token);
            }
            else
            {
                var stepSizeX = (destPoint.X - sourcePoint.X) / steps;
                var stepSizeY = (destPoint.Y - sourcePoint.Y) / steps;
                for (var i = 0; i < steps + 1; i++)
                {
                    await SendAsync(Input.DispatchMouseEvent("mouseMoved",
                        X: sourcePoint.X + stepSizeX * i, Y: sourcePoint.Y + stepSizeY * i), token: token);
                    await Task.Yield();
                }
            }
            await SendAsync(Input.DispatchMouseEvent("mouseReleased",
                X: destPoint.X, Y: destPoint.Y, Button: new Input.MouseButton("left")), token: token);
        }

        //ok
        public async Task FlashPointAsync(double x, double y, double duration = 0.5, int size = 10, CancellationToken token = default)
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 16);
            var style =
                $$"""
                    position:absolute;z-index:99999999;padding:0;margin:0;
                    left:{{x - 8}}px;top:{{y - 8}}px;
                    opacity:1;
                    width:{{size}}px;height:{{size}}px;border-radius:50%;background:red;
                    animation:show-pointer-ani {{duration}}s ease 1;
                """;
            var script =
                $$"""
                    var css = document.styleSheets[0];
                    for( let css of [...document.styleSheets]) {
                        try {
                            css.insertRule(`
                            @keyframes show-pointer-ani {
                                  0% { opacity: 1; transform: scale(1, 1);}
                                  50% { transform: scale(3, 3);}
                                  100% { transform: scale(1, 1); opacity: 0;}
                            }`,css.cssRules.length);
                            break;
                        } catch (e) {
                            console.log(e)
                        }
                    };
                    var _d = document.createElement('div');
                    _d.style = `{{style}}`;
                    _d.id = `{{id}}`;
                    document.body.insertAdjacentElement('afterBegin', _d);
    
                    setTimeout( () => document.getElementById('{{id}}').remove(), {{Math.Floor(duration * 1000)}});
                """;

            script = script
                .Replace("  ", "")
                .Replace("\n", "");

            await SendAsync(Cdp.Runtime.Evaluate(script, AwaitPromise: true, UserGesture: true), token: token);
        }

        public bool Equals(Tab? other)
        {
            if (other == null)
                return false;
            if (other.Target == null || Target == null)
                return false;
            return other.Target == Target;
        }

        public override bool Equals(object? obj) => Equals(obj as Tab);

        public override int GetHashCode() => Target?.GetHashCode() ?? 0;

        public static bool operator ==(Tab? left, Tab? right)
        {
            if (left is null)
                return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(Tab? left, Tab? right) => !(left == right);

        public override string ToString()
        {
            var extra = "";
            if (!string.IsNullOrWhiteSpace(Target?.Url))
                extra = $"[url: {Target.Url}]";

            var type = Target?.Type ?? "";

            return $"<{GetType().Name} [{TargetId}] [{type}] {extra}>";
        }
    }
}
