using NoDriver.Cdp;
using NoDriver.Core.Interface;
using NoDriver.Core.Message;
using System.Diagnostics;
using System.Drawing;

namespace NoDriver.Core
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

        public Tab(string webSocketUrl, dynamic? target = null, Browser? browser = null) : 
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
                await SendOneshotAsync(Cdp.Network.SetUserAgentOverride(ua.Replace("Headless", "")), token);
            }
            _prepHeadlessDone = true;
        }

        private async Task PrepareExpertAsync(CancellationToken token = default)
        {
            if (_prepExpertDone) 
                return;

            if (Browser != null)
            {
                await SendOneshotAsync(Cdp.Page.Enable(), token);
                await SendOneshotAsync(Cdp.Page.AddScriptToEvaluateOnNewDocument(@"
                    console.log('hooking attachShadow');
                    Element.prototype._attachShadow = Element.prototype.attachShadow;
                    Element.prototype.attachShadow = function () {
                        console.log('calling hooked attachShadow')
                        return this._attachShadow( { mode: 'open' } );
                    };"), token);
            }
            _prepExpertDone = true;
        }

        public async Task<Element> FindAsync(string text, bool bestMatch = true, bool returnEnclosingElement = true, double timeout = 10)
        {
            var sw = Stopwatch.StartNew();
            text = text.Trim();

            var item = await FindElementByTextAsync(text, bestMatch, returnEnclosingElement);
            while (item == null)
            {
                await WaitAsync();
                item = await FindElementByTextAsync(text, bestMatch, returnEnclosingElement);
                if (sw.Elapsed.TotalSeconds > timeout)
                    return item;
                await SleepAsync(0.5);
            }
            return item;
        }

        public async Task<Element> SelectAsync(string selector, double timeout = 10)
        {
            var sw = Stopwatch.StartNew();
            selector = selector.Trim();

            var item = await QuerySelectorAsync(selector);
            while (item == null)
            {
                await WaitAsync();
                item = await QuerySelectorAsync(selector);
                if (sw.Elapsed.TotalSeconds > timeout)
                    return item;
                await SleepAsync(0.5);
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

        public async Task<List<Element>> SelectAllAsync(string selector, double timeout = 10, bool includeFrames = false)
        {
            var sw = Stopwatch.StartNew();
            selector = selector.Trim();

            var items = new List<Element>();
            if (includeFrames)
            {
                var frames = await QuerySelectorAllAsync("iframe");
                foreach (var fr in frames)
                {
                    items.AddRange(await fr.QuerySelectorAllAsync(selector));
                }
            }

            items.AddRange(await QuerySelectorAllAsync(selector));
            while (items.Count == 0)
            {
                await WaitAsync();
                items.AddRange(await QuerySelectorAllAsync(selector));
                if (sw.Elapsed.TotalSeconds > timeout)
                    return items;
                await SleepAsync(0.5);
            }
            return items;
        }

        public async Task SleepAsync(double t = 1)
        {
            if (Browser != null)
            {
                var updateTask = Browser.UpdateTargetsAsync();
                var sleepTask = Task.Delay(TimeSpan.FromSeconds(t));
                await Task.WhenAll(updateTask, sleepTask);
            }
        }

        public async Task<List<Element>> XPathAsync(string xpath, double timeout = 2.5)
        {
            var items = new List<Element>();
            try
            {
                await SendAsync(Cdp.DOM.Enable(), true);
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
                    await SendAsync(Cdp.DOM.Disable(), true);
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
                var result = await SendAsync(Cdp.Page.Navigate(url));
                await WaitAsync();
                return this;
            }
        }

        public async Task<List<Element>> QuerySelectorAllAsync(string selector, Cdp.DOM.Node _node = null)
        {
            var doc = null as Cdp.DOM.Node;
            if (_node == null)
            {
                doc = await SendAsync(Cdp.DOM.GetDocument(-1, true));
            }
            else
            {
                doc = _node;
                if (_node.NodeName == "IFRAME")
                    doc = _node.ContentDocument;
            }

            var nodeIds = new List<int>();
            try
            {
                nodeIds = await SendAsync(Cdp.DOM.QuerySelectorAll(doc.NodeId, selector));
            }
            //except AttributeError:
            //# has no content_document
            //return
            //catch (Exception e) when (e.Message.Contains("could not find node", StringComparison.OrdinalIgnoreCase))
            catch(ProtocolErrorException ex)
            {
                if (_node != null)
                {
                    if (ex.Message.ToLowerInvariant().Contains("could not find node"))
                    {
                        if (_node.GetMetadata("__last") != null)
                        {
                            _node.RemoveMetadata("__last");
                            return new List<Element>();
                        }
                        await _node.UpdateAsync();
                        _node.SetMetadata("__last", true);
                        return await QuerySelectorAllAsync(selector, _node);
                    }
                }
                else
                {
                    await SendAsync(Cdp.DOM.Disable());
                    throw;
                }
            }

            if (nodeIds == null || nodeIds.Count == 0) 
                return new List<Element>();

            var items = new List<Element>();
            foreach (var nid in nodeIds)
            {
                var node = Util.FilterRecurse(doc, n => n.NodeId == nid);
                if (node == null) 
                    continue;
                items.Add(Element.Create(node, this, doc));
            }
            return items;
        }

        public async Task<Element> QuerySelectorAsync(string selector, Cdp.DOM.Node _node = null)
        {
            selector = selector.Trim();

            var doc = null as Cdp.DOM.Node;
            if (_node == null)
            {
                doc = await SendAsync(Cdp.DOM.GetDocument(-1, true));
            }
            else
            {
                doc = _node;
                if (_node.NodeName == "IFRAME")
                    doc = _node.ContentDocument;
            }

            int? nodeId = null;
            try
            {
                nodeId = await SendAsync(Cdp.DOM.QuerySelector(doc.NodeId, selector));
            }
            catch (ProtocolErrorException ex)
            {
                if (_node != null)
                {
                    if (ex.Message.ToLowerInvariant().Contains("could not find node"))
                    {
                        if (_node.GetMetadata("__last") != null)
                        {
                            _node.RemoveMetadata("__last");
                            return null;
                        }
                        await _node.UpdateAsync();
                        _node.SetMetadata("__last", true);
                        return await QuerySelectorAsync(selector, _node);
                    }
                }
                else
                {
                    await SendAsync(Cdp.DOM.Disable());
                    throw;
                }
            }

            if (nodeId == null) 
                return null;

            var node = Util.FilterRecurse(doc, n => n.NodeId == nodeId);
            if (node == null) 
                return null;
            return Element.Create(node, this, doc);
        }

        public async Task<List<Element>> FindElementsByTextAsync(string text, string? tagHint = null)
        {
            text = text.Trim();
            var doc = await SendAsync(Cdp.DOM.GetDocument(-1, true));
            var searchResult = await SendAsync(Cdp.DOM.PerformSearch(text, true));
            var searchId = searchResult.SearchId;
            var nresult = searchResult.ResultCount;

            var nodeIds = new List<int>();
            if (nresult > 0)
                nodeIds = await SendAsync(Cdp.DOM.GetSearchResults(searchId, 0, nresult));

            await SendAsync(Cdp.DOM.DiscardSearchResults(searchId));

            var items = new List<Element>();
            foreach (var nid in nodeIds)
            {
                var node = Util.FilterRecurse(doc, n => n.NodeId == nid);
                if (node == null)
                {
                    node = await SendAsync(Cdp.DOM.ResolveNode(nodeId: nid));
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

            await SendAsync(Cdp.DOM.Disable());
            return items;
        }

        public async Task<Element> FindElementByTextAsync(string text, bool bestMatch = false, bool returnEnclosingElement = true)
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

        public async Task BackAsync()
        {
            await SendAsync(Cdp.Runtime.Evaluate("window.history.back()"));
        }

        public async Task ForwardAsync() 
        {
            await SendAsync(Cdp.Runtime.Evaluate("window.history.forward()"));
        }

        public async Task ReloadAsync(bool ignoreCache = true, string? scriptToEvaluateOnLoad = null)
        {
            await SendAsync(Cdp.Page.Reload(ignoreCache, scriptToEvaluateOnLoad));
        }

        public async Task<(Cdp.Runtime.RemoteObject, Cdp.Runtime.ExceptionDetails)> EvaluateAsync(string expression, bool awaitPromise = false, bool returnByValue = false)
        {
            var ser = new Cdp.Runtime.SerializationOptions("deep", 10, new Dictionary<string, object>
            {
                {"maxNodeDepth", 10}, {"includeShadowTree", "all"}
            });

            return await SendAsync(Cdp.Runtime.Evaluate(
                expression,
                userGesture: true,
                awaitPromise: awaitPromise,
                returnByValue: returnByValue,
                allowUnsafeEvalBlockedByCsp: true,
                serializationOptions: ser));

            //if errors:
            //    return errors
            //if remote_object:
            //    if return_by_value:
            //        if remote_object.value:
            //            return remote_object.value
            //    else:
            //        if remote_object.deep_serialized_value:
            //            return remote_object.deep_serialized_value.value

            //return remote_object
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

        public async Task CloseAsync()
        {
            if (Target?.TargetId != null)
            {
                await SendAsync(Cdp.Target.CloseTarget(Target.TargetId));
            }
        }

        public async Task<(string WindowId, Cdp.Browser.Bounds Bounds)> GetWindowAsync()
        {
            return await SendAsync(Cdp.Browser.GetWindowForTarget(TargetId));
        }

        public async Task<string> GetContentAsync()
        {
            Cdp.DOM.Node doc = await SendAsync(Cdp.DOM.GetDocument(-1, true));
            return await SendAsync(Cdp.DOM.GetOuterHtml(doc.BackendNodeId));
        }

        public Task MaximizeAsync()
        {
            SetWindowStateAsync(state: "maximize");
        }

        public Task MinimizeAsync()
        {
            SetWindowStateAsync(state: "minimize");
        }

        public Task FullscreenAsync()
        {
            SetWindowStateAsync(state: "fullscreen");
        }

        public Task MedimizeAsync()
        {
            SetWindowStateAsync(state: "normal");
        }

        public Task SetWindowSizeAsync(int left = 0, int top = 0, int width = 1280, int height = 1024)
        {
            SetWindowStateAsync(left, top, width, height);
        }

        public async Task ActivateAsync()
        {
            await SendAsync(Cdp.Target.ActivateTarget(Target.TargetId));
        }

        public Task BringToFrontAsync()
        {
            await ActivateAsync();
        }

        public async Task SetWindowStateAsync(int left = 0, int top = 0, int width = 1280, int height = 720, string state = "normal")
        {
            var availableStates = new[] { "minimized", "maximized", "fullscreen", "normal" };
            var stateName = availableStates.FirstOrDefault(s => s.Contains(state.ToLowerInvariant()));
            if (stateName == null) 
                throw new ArgumentException($"Could not determine any of {string.Join(",", availableStates)} from input '{state}'");

            var (windowId, bounds) = await GetWindowAsync();

            //window_state = getattr(
            //    cdp.browser.WindowState, state_name.upper(), cdp.browser.WindowState.NORMAL
            //)

            var newBounds = null as Cdp.Browser.Bounds;
            if (stateName == "normal")
            {
                newBounds = new Cdp.Browser.Bounds 
                { 
                    Left = left, 
                    Top = top, 
                    Width = width, 
                    Height = height, 
                    WindowState = "normal" 
                };
            }
            else
            {
                await SetWindowStateAsync(state: "normal");
                newBounds = new Cdp.Browser.Bounds 
                { 
                    WindowState = stateName 
                };
            }
            await SendAsync(Cdp.Browser.SetWindowBounds(windowId, newBounds));
        }

        public async Task ScrollDownAsync(int amount = 25)
        {
            var (_, bounds) = await GetWindowAsync();
            await SendAsync(Cdp.Input.SynthesizeScrollGesture(
                0, 0, 
                xDistance: -(bounds.Height * (amount / 100.0)),
                yOverscroll:0,
                xOverscroll:0, 
                preventFling: true, 
                repeatDelayMs: 0,
                speed: 7777));
        }

        public async Task ScrollUpAsync(int amount = 25)
        {
            var (_, bounds) = await GetWindowAsync();
            await SendAsync(Cdp.Input.SynthesizeScrollGesture(
                0, 0, 
                yDistance: (bounds.Height * (amount / 100.0)),
                xOverscroll: 0, 
                preventFling: true,
                repeatDelayMs: 0,
                speed: 7777));
        }

        public async Task WaitAsync(double t = 0.5)
        {
            await SleepAsync(t);
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

        public async Task<string> SaveScreenshotAsync(string filename = "auto", string format = "jpeg", bool fullPage = false)
        {
            await SleepAsync(); // update target URL

            if (format.ToLowerInvariant() == "jpg" || format.ToLowerInvariant() == "jpeg") 
                format = "jpeg";
            else if (format.ToLowerInvariant() == "png") 
                format = "png";

            var ext = format == "jpeg" ? ".jpg" : ".png";

            var path = "";
            if (string.IsNullOrWhiteSpace(filename) || filename == "auto")
            {
                var uri = new Uri(Target.Url);
                var lastPart = uri.AbsolutePath.Split('/').Last();
                var index = lastPart.LastIndexOf('?');
                if (index != -1)
                    lastPart = lastPart.Substring(0, index);
                var dtStr = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                path = Path.Combine(Directory.GetCurrentDirectory(), $"{uri.Host}__{lastPart}_{dtStr}{ext}");
            }
            else
            {
                path = filename;
            }

            if (string.IsNullOrWhiteSpace(path))
                throw new Exception($"Invalid filename or path: '{filename}'");

            var base64Data = await SendAsync(Cdp.Page.CaptureScreenshot(format, fullPage));
            if (string.IsNullOrWhiteSpace(base64Data)) 
                throw new ProtocolErrorException("Could not take screenshot. most possible cause is the page has not finished loading yet.");

            var parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentDir))
                Directory.CreateDirectory(parentDir);

            var dataBytes = Convert.FromBase64String(base64Data);
            File.WriteAllBytes(path, dataBytes);
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
                            if (!validStarts.Any(s => v.Contains(s)))
                                continue;

                            if (Uri.TryCreate(new Uri(Target.Url), v, out var absUri))
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

            var storageId = new Cdp.DOMStorage.StorageId 
            { 
                IsLocalStorage = true, 
                SecurityOrigin = origin 
            };
            var items = await SendAsync(Cdp.DOMStorage.GetDOMStorageItems(storageId));

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

            var storageId = new Cdp.DOMStorage.StorageId
            {
                SecurityOrigin = origin,
                IsLocalStorage = true
            };

            var tasks = items.Select(kvp =>
                SendAsync(Cdp.DOMStorage.SetDOMStorageItem(
                    storageId,
                    kvp.Key,
                    kvp.Value
                ))
            );
            await Task.WhenAll(tasks);
        }

        public async Task<Cdp.Page.FrameTree> GetFrameTreeAsync()
        {
            return await SendAsync(Cdp.Page.GetFrameTree());
        }

        public async Task<Cdp.Page.FrameResourceTree> GetFrameResourceTreeAsync()
        {
            return await SendAsync(Cdp.Page.GetResourceTree());
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
                await SendOneshotAsync(Cdp.Page.Enable());

                var tree = await GetFrameResourceTreeAsync();
                var listOfTuples = Util.FlattenFrameTreeResources(tree);

                var results = new Dictionary<string, List<Cdp.Debugger.SearchMatch>>();
                foreach (var item in listOfTuples)
                {
                    var frame = item.Frame;
                    var resource = item.Resource;

                    if (frame == null || resource == null) 
                        continue;

                    var res = await SendAsync(Cdp.Page.SearchInResource(frame.Id, resource.Url, query));
                    if (res != null && res.Count > 0)
                    {
                        results[resource.Url] = res;
                    }
                }
                return results;
            }
            finally
            {
                await SendOneshotAsync(Cdp.Page.Disable());
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
                int cx = maxL.X + (tmpW / 2);
                int cy = maxL.Y + (tmpH / 2);

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

        public async Task MouseMoveAsync(double x, double y, int steps = 10, bool flash = false)
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
                        await FlashPointAsync(currentX, currentY);
                    await SendAsync(Cdp.Input.DispatchMouseEvent("mouseMoved", currentX, currentY));
                }
            }
            else
            {
                await SendAsync(Cdp.Input.DispatchMouseEvent("mouseMoved", x, y));
            }

            if (flash) 
                await FlashPointAsync(x, y);
            else 
                await SleepAsync(0.05);

            await SendAsync(Cdp.Input.DispatchMouseEvent("mouseReleased", x, y));
            if (flash) 
                await FlashPointAsync(x, y);
        }

        public async Task<bool> ScrollBottomReachedAsync()
        {
            var expression = "document.body.offsetHeight - window.innerHeight === window.scrollY";

            var (remoteObj, exception) = await EvaluateAsync(expression, returnByValue: true);

            if (remoteObj?.Value != null)
            {
                if (bool.TryParse(remoteObj.Value.ToString(), out var isReached))
                {
                    return isReached;
                }
            }
            return false;
        }

        public async Task MouseClickAsync(double x, double y, string button = "left", int buttons = 1, int modifiers = 0)
        {
            await SendAsync(Cdp.Input.DispatchMouseEvent("mousePressed", x, y,
                modifiers: modifiers, button: new Cdp.Input.MouseButton(button), buttons: buttons, clickCount: 1));
            await SendAsync(Cdp.Input.DispatchMouseEvent("mouseReleased", x, y,
                modifiers: modifiers, button: new Cdp.Input.MouseButton(button), buttons: buttons, clickCount: 1));
        }

        public async Task MouseDragAsync((double X, double Y) sourcePoint, (double X, double Y) destPoint, bool relative = false, int steps = 1)
        {
            if (relative) 
                destPoint = (sourcePoint.X + destPoint.X, sourcePoint.Y + destPoint.Y);

            await SendAsync(Cdp.Input.DispatchMouseEvent("mousePressed", 
                sourcePoint.X, sourcePoint.Y, new Cdp.Input.MouseButton("left")));

            steps = steps < 1 ? 1 : steps;
            if (steps == 1)
            {
                await SendAsync(Cdp.Input.DispatchMouseEvent("mouseMoved", destPoint.X, destPoint.Y));
            }
            else
            {
                var stepSizeX = (destPoint.X - sourcePoint.X) / steps;
                var stepSizeY = (destPoint.Y - sourcePoint.Y) / steps;
                for (var i = 0; i <= steps; i++)
                {
                    await SendAsync(Cdp.Input.DispatchMouseEvent("mouseMoved", 
                        sourcePoint.X + stepSizeX * i, sourcePoint.Y + stepSizeY * i));
                    await Task.Yield();
                }
            }
            await SendAsync(Cdp.Input.DispatchMouseEvent("mouseReleased", 
                destPoint.X, destPoint.Y, new Cdp.Input.MouseButton("left")));
        }

        public async Task FlashPointAsync(double x, double y, double duration = 0.5, int size = 10)
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

            await SendAsync(Cdp.Runtime.Evaluate(script, awaitPromise: true, userGesture: true));
        }
    }
}
