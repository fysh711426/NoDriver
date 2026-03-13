using NoDriver.Core.Messaging;
using OpenCvSharp;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace NoDriver.Core.Runtime
{
    public class Tab : Connection, IEquatable<Tab>
    {
        private List<string>? _downloadBehavior = null;

        private int? _windowId = null;
        private Cdp.DOM.Node? _dom = null;
        
        private bool _prepHeadlessDone = false;
        private bool _prepExpertDone = false;
        
        //ok
        public string InspectorUrl
        {
            get
            {
                if (Browser?.Config?.Host != null && Browser?.Config?.Port != null)
                    return $"http://{Browser.Config.Host}:{Browser.Config.Port}/devtools/inspector.html?ws={WebSocketUrl.Substring(5)}";
                return "";
            }
        }

        public Tab(string webSocketUrl, Cdp.Target.TargetInfo? target = null, Browser? browser = null) : 
            base(webSocketUrl, target, browser)
        {
        }

        // ok 要測試
        public void InspectorOpen()
        {
            Process.Start(new ProcessStartInfo(InspectorUrl) { UseShellExecute = true });
        }

        // ok 要測試
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

        //ok 要檢查 json 轉換有沒有成功
        public async Task PrepareHeadlessAsync(CancellationToken token = default)
        {
            if (_prepHeadlessDone) 
                return;

            var response = await SendOneshotAsync(Cdp.Runtime.Evaluate("navigator.userAgent"), token);
            if (response == null) 
                return;

            if (response?.Result?.Value != null)
            {
                var ua = response.Result.Value.ToString();
                await SendOneshotAsync(Cdp.Network.SetUserAgentOverride(UserAgent: ua.Replace("Headless", "")), token);
            }
            _prepHeadlessDone = true;
        }

        // ok 要測試
        public async Task PrepareExpertAsync(CancellationToken token = default)
        {
            if (_prepExpertDone) 
                return;

            if (Browser != null)
            {
                await SendOneshotAsync(Cdp.Page.Enable(), token);
                await SendOneshotAsync(Cdp.Page.AddScriptToEvaluateOnNewDocument(
                    $$"""
                        console.log('hooking attachShadow');
                        Element.prototype._attachShadow = Element.prototype.attachShadow;
                        Element.prototype.attachShadow = function () {
                            console.log('calling hooked attachShadow')
                            return this._attachShadow( { mode: 'open' } );
                        };
                    """), token);
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

        // ok 要測試
        public async Task<List<Element>> FindAllAsync(string text, double timeout = 10, CancellationToken token = default)
        {
            var sw = Stopwatch.StartNew();
            text = text.Trim();

            var items = await FindElementsByTextAsync(text, token: token);
            while (items == null || items.Count == 0)
            {
                await WaitAsync(token: token);
                items = await FindElementsByTextAsync(text, token: token);
                if (sw.Elapsed.TotalSeconds > timeout)
                    return items;
                await WaitAsync(0.5, token);
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
                foreach (var frame in frames)
                {
                    items.AddRange(await frame.QuerySelectorAllAsync(selector, token: token));
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

        // ok 要測試
        public async Task<List<Element>> XPathAsync(string xpath, double timeout = 2.5, CancellationToken token = default)
        {
            var items = new List<Element>();
            try
            {
                await SendAsync(Cdp.DOM.Enable(), true, token: token);
                items = await FindAllAsync(xpath, timeout: 0, token: token);
                if (items.Count == 0)
                {
                    var sw = Stopwatch.StartNew();
                    while (items.Count == 0)
                    {
                        items = await FindAllAsync(xpath, timeout: 0, token: token);
                        await WaitAsync(0.1, token);
                        if (sw.Elapsed.TotalSeconds > timeout)
                            break;
                    }
                }
            }
            finally
            {
                try
                {
                    await SendAsync(Cdp.DOM.Disable(), true, token: token);
                }
                catch (ProtocolErrorException) { }
            }
            return items;
        }

        //ok
        public async Task<Tab> GetAsync(string url = "chrome://welcome", bool newTab = false, bool newWindow = false, CancellationToken token = default)
        {
            if (Browser == null)
                throw new InvalidOperationException("This tab has no browser attribute.");

            if (newWindow && !newTab)
                newTab = true;

            if (newTab)
            {
                return await Browser.GetAsync(url, newTab, newWindow, token);
            }
            else
            {
                var result = await SendAsync(Cdp.Page.Navigate(url), token: token);
                await WaitAsync(token: token);
                return this;
            }
        }

        //ok
        public async Task<List<Element>> QuerySelectorAllAsync(string selector, Element? node = null, CancellationToken token = default)
        {
            return await QuerySelectorAllAsync(selector, node, false, token);
        }

        // ok 要測試
        private async Task<List<Element>> QuerySelectorAllAsync(string selector, Element? node, bool isRetry = false, CancellationToken token = default)
        {
            selector = selector.Trim();

            var doc = null as Cdp.DOM.Node;
            if (node == null)
            {
                var result = await SendAsync(Cdp.DOM.GetDocument(-1, true), token: token);
                doc = result.Root;
            }
            else
            {
                doc = node.Node;
                if (node.NodeName == "IFRAME")
                    doc = node.ContentDocument;
            }

            var nodeIds = new List<Cdp.DOM.NodeId>();
            try
            {
                if (doc?.NodeId != null)
                {
                    var result = await SendAsync(Cdp.DOM.QuerySelectorAll(doc.NodeId, selector), token: token);
                    nodeIds = result.NodeIds.ToList();
                }
            }
            catch (ProtocolErrorException ex)
            {
                if (node != null)
                {
                    if (ex.Message.ToLowerInvariant().Contains("could not find node"))
                    {
                        if (isRetry)
                            return new();

                        await node.UpdateAsync(token: token);
                        return await QuerySelectorAllAsync(selector, node, true, token);
                    }
                }
                else
                {
                    await SendAsync(Cdp.DOM.Disable(), token: token);
                    throw;
                }
            }

            if (nodeIds == null || nodeIds.Count == 0)
                return new();

            var items = new List<Element>();
            foreach (var nid in nodeIds)
            {
                var _node = Util.FilterRecurse(doc, n => n.NodeId == nid);
                if (_node != null)
                    items.Add(new Element(_node, this, doc));
            }
            return items;
        }

        //ok
        public async Task<Element?> QuerySelectorAsync(string selector, Element? node = null, CancellationToken token = default)
        {
            return await QuerySelectorAsync(selector, node, false, token);
        }

        // ok 要測試
        private async Task<Element?> QuerySelectorAsync(string selector, Element? node, bool isRetry = false, CancellationToken token = default)
        {
            selector = selector.Trim();

            var doc = null as Cdp.DOM.Node;
            if (node == null)
            {
                var result = await SendAsync(Cdp.DOM.GetDocument(-1, true), token: token);
                doc = result.Root;
            }
            else
            {
                doc = node.Node;
                if (node.NodeName == "IFRAME")
                    doc = node.ContentDocument;
            }

            var nodeId = null as Cdp.DOM.NodeId;
            try
            {
                if (doc?.NodeId != null)
                {
                    var result = await SendAsync(Cdp.DOM.QuerySelector(doc.NodeId, selector), token: token);
                    nodeId = result.NodeId;
                }
            }
            catch (ProtocolErrorException ex)
            {
                if (node != null)
                {
                    if (ex.Message.ToLowerInvariant().Contains("could not find node"))
                    {
                        if (isRetry)
                            return null;

                        await node.UpdateAsync(token: token);
                        return await QuerySelectorAsync(selector, node, true, token);
                    }
                }
                else
                {
                    await SendAsync(Cdp.DOM.Disable(), token: token);
                    throw;
                }
            }

            if (nodeId == null)
                return null;

            var _node = Util.FilterRecurse(doc, n => n.NodeId == nodeId);
            if (_node != null)
                return new Element(_node, this, doc);
            return null;
        }

        // ok 要測試
        public async Task<List<Element>> FindElementsByTextAsync(string text, string? tagHint = null, CancellationToken token = default)
        {
            text = text.Trim();
            var docResult = await SendAsync(Cdp.DOM.GetDocument(-1, true), token: token);
            var doc = docResult.Root;

            var searchResult = await SendAsync(Cdp.DOM.PerformSearch(text, true), token: token);
            var searchId = searchResult.SearchId;
            var nresult = searchResult.ResultCount;

            var nodeIds = new List<Cdp.DOM.NodeId>();
            if (nresult > 0)
            {
                var result = await SendAsync(Cdp.DOM.GetSearchResults(searchId, 0, nresult), token: token);
                nodeIds = result.NodeIds.ToList();
            }

            await SendAsync(Cdp.DOM.DiscardSearchResults(searchId), token: token);

            var items = new List<Element>();
            foreach (var nid in nodeIds)
            {
                var node = Util.FilterRecurse(doc, n => n.NodeId == nid);
                if (node == null)
                {
                    node = await ResolveNodeAsync(nodeId: nid, token);
                    if (node == null)
                        continue;
                }

                var elem = new Element(node, this, doc);
                if (elem.NodeType == 3)
                {
                    if (elem.Parent == null)
                        await elem.UpdateAsync(token: token);
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
                var iframeElem = new Element(iframe, this, iframe.ContentDocument);
                if (iframeElem.ContentDocument != null)
                {
                    var textLower = text.ToLowerInvariant();
                    var iframeTextNodes = Util.FilterRecurseAll(iframeElem.Node,
                        n => n.NodeType == 3 && n.NodeValue.ToLowerInvariant().Contains(textLower));

                    foreach (var textNode in iframeTextNodes)
                    {
                        var textElem = new Element(textNode, this, iframeElem.Tree);
                        var parent = textElem.Parent;
                        if (parent != null)
                            items.Add(parent);
                    }
                }
            }

            await SendAsync(Cdp.DOM.Disable(), token: token);
            return items;
        }

        // ok 要測試
        public async Task<Element?> FindElementByTextAsync(string text, bool bestMatch = false, bool returnEnclosingElement = true, CancellationToken token = default)
        {
            var items = await FindElementsByTextAsync(text, token: token);
            if (bestMatch)
                return items.OrderBy(el => Math.Abs(text.Length - (el.TextAll?.Length ?? 0))).FirstOrDefault();
            return items.FirstOrDefault();
        }

        //ok
        public async Task<Cdp.DOM.Node?> ResolveNodeAsync(Cdp.DOM.NodeId nodeId, CancellationToken token = default)
        {
            var result = await SendAsync(Cdp.DOM.ResolveNode(NodeId: nodeId), token: token);
            var remoteObj = result.Object;
            if (remoteObj.ObjectId != null)
            {
                var idResult = await SendAsync(Cdp.DOM.RequestNode(ObjectId: remoteObj.ObjectId), token: token);
                var nodeResult = await SendAsync(Cdp.DOM.DescribeNode(NodeId: idResult.NodeId), token: token);
                return nodeResult.Node;
            }
            return null;
        }

        //ok
        public async Task BackAsync(CancellationToken token = default)
        {
            await SendAsync(Cdp.Runtime.Evaluate("window.history.back()"), token: token);
        }

        // ok 要測試
        public async Task ForwardAsync(CancellationToken token = default) 
        {
            await SendAsync(Cdp.Runtime.Evaluate("window.history.forward()"), token: token);
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

        // ok 要測試
        public async Task<(Cdp.Runtime.RemoteObject remoteObject, Cdp.Runtime.ExceptionDetails? exception)> JsDumpsAsync(string objName, bool returnByValue = true, CancellationToken token = default)
        {
            var jsCodeA = 
                $$"""
                    function ___dump(obj, _d = 0) {
                        let _typesA = ['object', 'function'];
                        let _typesB = ['number', 'string', 'boolean'];
                        if (_d == 2) {
                            // console.log('maxdepth reached for ', obj);
                            return
                        }
                        let tmp = {}
                        for (let k in obj) {
                            if (obj[k] == window) continue;
                            let v;
                            try {
                                if (obj[k] === null || obj[k] === undefined || obj[k] === NaN) {
                                     // console.log('obj[k] is null or undefined or Nan', k, '=>', obj[k])
                                    tmp[k] = obj[k];
                                    continue
                                }
                            } catch (e) {
                                tmp[k] = null;
                                continue
                            }

                            if (_typesB.includes(typeof obj[k])) {
                                tmp[k] = obj[k]
                                continue
                            }

                            try {
                                if (typeof obj[k] === 'function') {
                                    tmp[k] = obj[k].toString()
                                    continue
                                }


                                if (typeof obj[k] === 'object') {
                                    tmp[k] = ___dump(obj[k], _d + 1);
                                    continue
                                }


                            } catch (e) {}

                            try {
                                tmp[k] = JSON.stringify(obj[k])
                                continue
                            } catch (e) {

                            }
                            try {
                                tmp[k] = obj[k].toString();
                                continue
                            } catch (e) {}
                        }
                        return tmp
                    }

                    function ___dumpY(obj) {
                        var objKeys = (obj) => {
                            var [target, result] = [obj, []];
                            while (target !== null) {
                                result = result.concat(Object.getOwnPropertyNames(target));
                                target = Object.getPrototypeOf(target);
                            }
                            return result;
                        }
                        return Object.fromEntries(
                            objKeys(obj).map(_ => [_, ___dump(obj[_])]))

                    }
                    ___dumpY({{objName}})             
                """;

            var jsCodeB = 
                $$"""
                    ((obj, visited = new WeakSet()) => {
                     if (visited.has(obj)) {
                         return {}
                     }
                     visited.add(obj)
                     var result = {}, _tmp;
                     for (var i in obj) {
                             try {
                                 if (i === 'enabledPlugin' || typeof obj[i] === 'function') {
                                     continue;
                                 } else if (typeof obj[i] === 'object') {
                                     _tmp = recurse(obj[i], visited);
                                     if (Object.keys(_tmp).length) {
                                         result[i] = _tmp;
                                     }
                                 } else {
                                     result[i] = obj[i];
                                 }
                             } catch (error) {
                                 // console.error('Error:', error);
                             }
                         }
                    return result;
                })({{objName}})
                """;

            var result = await SendAsync(Cdp.Runtime.Evaluate(
                jsCodeA, 
                AwaitPromise: true, 
                ReturnByValue: returnByValue, 
                AllowUnsafeEvalBlockedByCSP: true), token: token);

            if (result.ExceptionDetails != null)
            {
                result = await SendAsync(Cdp.Runtime.Evaluate(
                    jsCodeB,
                    AwaitPromise: true,
                    ReturnByValue: returnByValue,
                    AllowUnsafeEvalBlockedByCSP: true), token: token);
            }
            return (result.Result, result.ExceptionDetails);
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

            await SendAsync(Cdp.Input.SynthesizeScrollGesture(
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

            await SendAsync(Cdp.Input.SynthesizeScrollGesture(
                0, 0,
                YDistance: bounds.Height * (amount / 100.0),
                XOverscroll: 0,
                PreventFling: true,
                RepeatDelayMs: 0,
                Speed: 7777), token: token);
        }

        // ok 要測試
        public async Task<Element?> WaitForAsync(string selector = "", string text = "", double timeout = 10, CancellationToken token = default)
        {
            var sw = Stopwatch.StartNew();
            if (!string.IsNullOrWhiteSpace(selector))
            {
                var item = await QuerySelectorAsync(selector, token: token);
                while (item == null)
                {
                    item = await QuerySelectorAsync(selector, token: token);

                    if (sw.Elapsed.TotalSeconds > timeout) 
                        throw new TimeoutException($"Time ran out while waiting for {selector}");
                    await WaitAsync(0.5, token);
                }
                return item;
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                var item = await FindElementByTextAsync(text, token: token);
                while (item == null)
                {
                    item = await FindElementByTextAsync(text, token: token);

                    if (sw.Elapsed.TotalSeconds > timeout) 
                        throw new TimeoutException($"Time ran out while waiting for text: {text}");
                    await WaitAsync(0.5, token);
                }
                return item;
            }
            return null;
        }

        //ok 要檢查下載是不是正確
        public async Task DownloadFileAsync(string url, string? filename = null, CancellationToken token = default)
        {
            if (_downloadBehavior == null || _downloadBehavior.Count == 0)
            {
                var dirPath = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
                Directory.CreateDirectory(dirPath);
                await SetDownloadPathAsync(dirPath, token);
                Console.WriteLine($"No download path set, using default: {dirPath}");
            }

            if (string.IsNullOrWhiteSpace(filename))
                filename = url.Split('/').Last().Split('?').First();

            var code = 
                $$"""
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

            var result = await QuerySelectorAllAsync("body", token: token);
            var body = result.First();
            await body.UpdateAsync(token: token);
            await SendAsync(Cdp.Runtime.CallFunctionOn(code, 
                ObjectId: body.ObjectId, 
                Arguments: new List<Cdp.Runtime.CallArgument> 
                { 
                    new Cdp.Runtime.CallArgument(ObjectId: body.ObjectId) 
                }), token: token);
            await WaitAsync(0.1, token);
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

        //ok 要測試
        public async Task SetDownloadPathAsync(string path, CancellationToken token = default)
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
            await SendAsync(Cdp.Browser.SetDownloadBehavior(Behavior: "allow", DownloadPath: fullPath), token: token);
            _downloadBehavior = new List<string> { "allow", fullPath };
        }

        //ok 要測試
        public async Task<List<Element>> GetAllLinkedSourcesAsync(CancellationToken token = default)
        {
            var allAssets = await QuerySelectorAllAsync("a,link,img,script,meta", token: token);
            return allAssets.Select(it => new Element(it.Node, this)).ToList();
        }

        //ok 要測試
        public async Task<List<string>> GetAllUrlsAsync(bool absolute = true, CancellationToken token = default)
        {
            var res = new List<string>();
            var allAssets = await QuerySelectorAllAsync("a,link,img,script,meta", token: token);

            foreach (var asset in allAssets)
            {
                if (!absolute)
                {
                    var rawUrl = 
                        asset.Attrs.GetValueOrDefault("src") ??
                        asset.Attrs.GetValueOrDefault("href");
                    if (!string.IsNullOrWhiteSpace(rawUrl))
                        res.Add(rawUrl);
                }
                else
                {
                    foreach (var kvp in asset.Attrs)
                    {
                        var k = kvp.Key;
                        var v = kvp.Value;

                        if (k == "src" || k == "href")
                        {
                            if (string.IsNullOrWhiteSpace(v))
                                continue;

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

        //ok 要檢查 json 有沒有轉換成功
        public async Task<Dictionary<string, string>> GetLocalStorageAsync(CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(Target?.Url))
                await WaitAsync(token: token);

            var origin = new Uri(Target?.Url ?? "").GetLeftPart(UriPartial.Authority);

            var result = await SendAsync(Cdp.DOMStorage.GetDOMStorageItems(
                new Cdp.DOMStorage.StorageId(IsLocalStorage: true, SecurityOrigin: origin)), token: token);

            var retval = new Dictionary<string, string>();
            foreach (var item in result.Entries)
            {
                var items = item.Items?.ToList();
                if (items != null && items.Count >= 2)
                {
                    retval[items[0]] = items[1];
                }
            }
            return retval;
        }

        //ok 要測試
        public async Task SetLocalStorageAsync(Dictionary<string, string> items, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(Target?.Url))
                await WaitAsync(token: token);

            if (items?.Count > 0)
            {
                var origin = new Uri(Target?.Url ?? "").GetLeftPart(UriPartial.Authority);

                var tasks = items.Select(kvp =>
                    SendAsync(Cdp.DOMStorage.SetDOMStorageItem(
                        new Cdp.DOMStorage.StorageId(IsLocalStorage: true, SecurityOrigin: origin),
                        kvp.Key, kvp.Value), token: token));

                await Task.WhenAll(tasks);
            }
        }

        //ok 要測試
        public async Task<Cdp.Page.FrameTree> GetFrameTreeAsync(CancellationToken token = default)
        {
            var result = await SendAsync(Cdp.Page.GetFrameTree(), token: token);
            return result.FrameTree;
        }

        //ok 要測試
        public async Task<Cdp.Page.FrameResourceTree> GetFrameResourceTreeAsync(CancellationToken token = default)
        {
            var result = await SendAsync(Cdp.Page.GetResourceTree(), token: token);
            return result.FrameTree;
        }

        //ok 要測試
        public async Task<List<string>> GetFrameResourceUrlsAsync(CancellationToken token = default)
        {
            var tree = await GetFrameResourceTreeAsync(token);

            var flatResources = Util.FlattenFrameTreeResources(tree);

            return flatResources
                .Where(it => !string.IsNullOrWhiteSpace(it.resource?.Url))
                .Select(it => it.resource.Url)
                .ToList();
        }

        //ok 要測試
        public async Task<Dictionary<string, List<Cdp.Debugger.SearchMatch>>> SearchFrameResourcesAsync(string query, CancellationToken token = default)
        {
            try
            {
                await SendOneshotAsync(Cdp.Page.Enable(), token);

                var tree = await GetFrameResourceTreeAsync(token);

                var listOfTuples = Util.FlattenFrameTreeResources(tree);

                var results = new Dictionary<string, List<Cdp.Debugger.SearchMatch>>();
                foreach (var item in listOfTuples)
                {
                    var frame = item.frame;
                    var resource = item.resource;

                    if (frame == null || resource == null) 
                        continue;

                    var result = await SendAsync(Cdp.Page.SearchInResource(
                        FrameId: frame.Id, Url: resource.Url, Query: query), token: token);
                    if (result.Result?.Count > 0)
                        results[resource.Url] = result.Result.ToList();
                }
                return results;
            }
            finally
            {
                await SendOneshotAsync(Cdp.Page.Disable(), token);
            }
        }

        //ok 要測試
        public async Task VerifyCfAsync(string? templateImage = null, bool flash = false, CancellationToken token = default)
        {
            if (Browser?.Config?.Expert == true)
                throw new Exception("This function is useless in expert mode...");

            var loc = await TemplateLocationAsync(templateImage, token);
            if (loc != null)
            {
                await MouseClickAsync(loc.Value.X, loc.Value.Y, token: token);
                if (flash)
                    await FlashPointAsync(loc.Value.X, loc.Value.Y, token: token);
            }
        }

        //ok 要測試
        public async Task<(int X, int Y)?> TemplateLocationAsync(string? templateImage = null, CancellationToken token = default)
        {
            var screenPath = "screen.jpg";
            var cfTemplatePath = "cf_template.png";
            try
            {
                if (!string.IsNullOrWhiteSpace(templateImage))
                {
                    templateImage = Path.GetFullPath(templateImage);
                    if (!File.Exists(templateImage))
                        throw new FileNotFoundException($"{templateImage} was not found.");
                }

                await SaveScreenshotAsync(screenPath, token: token);
                await WaitAsync(0.05, token);

                using (var im = Cv2.ImRead(screenPath))
                {
                    using (var imGray = new Mat())
                    {
                        Cv2.CvtColor(im, imGray, ColorConversionCodes.BGR2GRAY);

                        var template = null as Mat;
                        if (!string.IsNullOrWhiteSpace(templateImage))
                        {
                            template = Cv2.ImRead(templateImage);
                        }
                        else
                        {
                            File.WriteAllBytes(cfTemplatePath, Util.GetCfTemplate());
                            template = Cv2.ImRead(cfTemplatePath);
                        }

                        using (var templateGray = new Mat())
                        {
                            Cv2.CvtColor(template, templateGray, ColorConversionCodes.BGR2GRAY);
                            using (var match = new Mat())
                            {
                                Cv2.MatchTemplate(imGray, templateGray, match, TemplateMatchModes.CCoeffNormed);
                                Cv2.MinMaxLoc(match, out var minV, out var maxV, out var minL, out var maxL);

                                var xs = maxL.X;
                                var ys = maxL.Y;

                                var tmpW = templateGray.Width;
                                var tmpH = templateGray.Height;

                                var xe = xs + tmpW;
                                var ye = ys + tmpH;

                                var cx = (xs + xe) / 2;
                                var cy = (ys + ye) / 2;

                                return (cx, cy);
                            }
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(screenPath))
                        File.Delete(screenPath);
                }
                catch
                {
                    Console.WriteLine("Could not delete temporary screenshot.");
                }

                if (string.IsNullOrWhiteSpace(templateImage))
                {
                    try
                    {
                        if (File.Exists(cfTemplatePath))
                            File.Delete(cfTemplatePath);
                    }
                    catch
                    {
                        Console.WriteLine($"Could not unlink template file {cfTemplatePath}.");
                    }
                }
            }
        }

        //ok 要測試
        public async Task BypassInsecureConnectionWarningAsync(CancellationToken token = default)
        {
            var body = await SelectAsync("body", token: token);
            if (body != null)
                await body.SendKeysAsync("thisisunsafe", token);
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

        //ok 要測試
        public async Task MouseClickAsync(double x, double y, string button = "left", int buttons = 1, int modifiers = 0, CancellationToken token = default)
        {
            await SendAsync(Cdp.Input.DispatchMouseEvent("mousePressed", x, y,
                Modifiers: modifiers, Button: new Cdp.Input.MouseButton(button), Buttons: buttons, ClickCount: 1), token: token);
            await SendAsync(Cdp.Input.DispatchMouseEvent("mouseReleased", x, y,
                Modifiers: modifiers, Button: new Cdp.Input.MouseButton(button), Buttons: buttons, ClickCount: 1), token: token);
        }

        //ok
        public async Task MouseDragAsync((double X, double Y) sourcePoint, (double X, double Y) destPoint, bool relative = false, int steps = 1, CancellationToken token = default)
        {
            if (relative)
                destPoint = (sourcePoint.X + destPoint.X, sourcePoint.Y + destPoint.Y);

            await SendAsync(Cdp.Input.DispatchMouseEvent("mousePressed",
                X: sourcePoint.X, Y: sourcePoint.Y, Button: new Cdp.Input.MouseButton("left")), token: token);

            steps = steps < 1 ? 1 : steps;
            if (steps == 1)
            {
                await SendAsync(Cdp.Input.DispatchMouseEvent("mouseMoved", X: destPoint.X, Y: destPoint.Y), token: token);
            }
            else
            {
                var stepSizeX = (destPoint.X - sourcePoint.X) / steps;
                var stepSizeY = (destPoint.Y - sourcePoint.Y) / steps;
                for (var i = 0; i < steps + 1; i++)
                {
                    await SendAsync(Cdp.Input.DispatchMouseEvent("mouseMoved",
                        X: sourcePoint.X + stepSizeX * i, Y: sourcePoint.Y + stepSizeY * i), token: token);
                    await Task.Yield();
                }
            }
            await SendAsync(Cdp.Input.DispatchMouseEvent("mouseReleased",
                X: destPoint.X, Y: destPoint.Y, Button: new Cdp.Input.MouseButton("left")), token: token);
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

        //ok
        public override string ToString()
        {
            var extra = "";
            if (!string.IsNullOrWhiteSpace(Target?.Url))
                extra = $"[url: {Target.Url}]";

            var type = Target?.Type ?? "";
            var targetId = Target?.TargetId?.Value ?? "";

            return $"<{GetType().Name} [{targetId}] [{type}] {extra}>";
        }
    }
}
