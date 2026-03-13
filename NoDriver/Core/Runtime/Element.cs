using NoDriver.Core.Messaging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace NoDriver.Core.Runtime
{
    public class Element : IEquatable<Element>
    {
        private readonly ConcurrentDictionary<string, string> _attrs = new(StringComparer.OrdinalIgnoreCase);

        private Tab _tab;
        private Cdp.DOM.Node _node;
        private Cdp.DOM.Node? _tree;
        private Element? _parent = null;
        private Cdp.Runtime.RemoteObject? _remoteObject = null;
        
        private bool _isHighlighted = false;

        public string Tag => NodeName.ToLowerInvariant();
        public string TagName => Tag;
        public Cdp.DOM.NodeId NodeId => _node.NodeId;
        public Cdp.DOM.BackendNodeId BackendNodeId => _node.BackendNodeId;
        public int NodeType => _node.NodeType;
        public string NodeName => _node.NodeName;
        public string LocalName => _node.LocalName;
        public string NodeValue => _node.NodeValue;
        public Cdp.DOM.NodeId? ParentId => _node.ParentId;
        public int? ChildNodeCount => _node.ChildNodeCount;
        public IReadOnlyList<string>? Attributes => _node.Attributes;
        public string? DocumentUrl => _node.DocumentURL;
        public string? BaseUrl => _node.BaseURL;
        public string? PublicId => _node.PublicId;
        public string? SystemId => _node.SystemId;
        public string? InternalSubset => _node.InternalSubset;
        public string? XmlVersion => _node.XmlVersion;
        public string? Value => _node.Value;
        public Cdp.DOM.PseudoType? PseudoType => _node.PseudoType;
        public string? PseudoIdentifier => _node.PseudoIdentifier;
        public Cdp.DOM.ShadowRootType? ShadowRootType => _node.ShadowRootType;
        public string? FrameId => _node.FrameId?.Value;
        public Cdp.DOM.Node? ContentDocument => _node.ContentDocument;
        public IReadOnlyList<Cdp.DOM.Node>? ShadowRoots => _node.ShadowRoots;
        public Cdp.DOM.Node? TemplateContent => _node.TemplateContent;
        public IReadOnlyList<Cdp.DOM.Node>? PseudoElements => _node.PseudoElements;
        public Cdp.DOM.Node? ImportedDocument => _node.ImportedDocument;
        public IReadOnlyList<Cdp.DOM.BackendNode>? DistributedNodes => _node.DistributedNodes;
        public bool? IsSvg => _node.IsSVG;
        public Cdp.DOM.CompatibilityMode? CompatibilityMode => _node.CompatibilityMode;
        public Cdp.DOM.BackendNode? AssignedSlot => _node.AssignedSlot;
        public Tab Tab => _tab;
        //ok 要測試
        public List<Element> ShadowChildren
        {
            get
            {
                var children = new List<Element>();
                if (ShadowRoots?.Count > 0)
                {
                    var root = ShadowRoots[0];
                    if (root.ShadowRootType == Cdp.DOM.ShadowRootType.OPEN)
                    {
                        if (root.Children != null)
                        {
                            foreach (var child in root.Children)
                            {
                                children.Add(new Element(child, _tab));
                            }
                        }
                    }
                }
                return children;
            }
        }
        public Cdp.DOM.Node Node => _node;
        public Cdp.DOM.Node? Tree
        {
            get => _tree;
            set => _tree = value;
        }
        public ConcurrentDictionary<string, string> Attrs => _attrs;
        //ok 要測試
        public Element? Parent
        {
            get
            {
                if (Tree == null)
                    throw new InvalidOperationException("Could not get parent since the element has no tree set.");
                var parentNode = Util.FilterRecurse(Tree, n => n.NodeId == ParentId);
                if (parentNode != null)
                    return new Element(parentNode, _tab, _tree);
                return null;
            }
        }
        //ok 要測試
        public IEnumerable<Element> Children
        {
            get
            {
                if (_node.NodeName == "IFRAME")
                {
                    var frame = _node.ContentDocument;
                    if (frame?.ChildNodeCount > 0)
                        if (frame.Children != null)
                            foreach (var child in frame.Children)
                                yield return new Element(child, _tab, frame);
                }
                else
                {
                    if (_node.ChildNodeCount > 0)
                        if (_node.Children != null)
                            foreach (var child in _node.Children)
                                yield return new Element(child, _tab, _tree);
                }
            }
        }
        public Cdp.Runtime.RemoteObject? RemoteObject => _remoteObject;
        public Cdp.Runtime.RemoteObjectId? ObjectId => RemoteObject?.ObjectId;
        //ok 要測試
        public string Text
        {
            get
            {
                var textNode = Util.FilterRecurse(_node, n => n.NodeType == 3);
                return textNode?.NodeValue ?? "";
            }
        }
        //ok 要測試
        public string TextAll
        {
            get
            {
                var textNodes = Util.FilterRecurseAll(_node, n => n.NodeType == 3);
                return string.Join(" ", textNodes.Select(n => n.NodeValue));
            }
        }

        public Element(Cdp.DOM.Node node, Tab tab, Cdp.DOM.Node? tree = null)
        {
            if (node == null)
                throw new ArgumentException("Node cannot be null.");

            _tab = tab;
            _node = node;
            _tree = tree;
            MakeAttrs();
        }

        //ok 要測試
        public async Task SaveToDomAsync(CancellationToken token = default)
        {
            var result = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId: BackendNodeId), token: token);
            _remoteObject = result.Object;
            await _tab.SendAsync(Cdp.DOM.SetOuterHTML(NodeId: NodeId, OuterHTML: $"{this}"), token: token);
            await UpdateAsync(token: token);
        }

        //ok 要測試
        public async Task RemoveFromDomAsync(CancellationToken token = default)
        {
            await UpdateAsync(token: token);
            var node = Util.FilterRecurse(_tree, n => n.BackendNodeId == BackendNodeId);
            if (node != null)
                await Tab.SendAsync(Cdp.DOM.RemoveNode(NodeId: node.NodeId), token: token);
        }

        //ok
        public async Task<Element> UpdateAsync(Cdp.DOM.Node? node = null, CancellationToken token = default)
        {
            var doc = node;
            if (doc == null)
            {
                var docResult = await _tab.SendAsync(Cdp.DOM.GetDocument(-1, true), token: token);
                doc = docResult.Root;
            }
            _parent = null;

            var updatedNode = Util.FilterRecurse(doc, n => n.BackendNodeId == _node.BackendNodeId);
            if (updatedNode != null)
            {
                Console.WriteLine("Node seems changed, and has now been updated.");
                _node = updatedNode;
            }
            _tree = doc;

            var result = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId: _node.BackendNodeId), token: token);
            _remoteObject = result.Object;
            _attrs.Clear();
            MakeAttrs();

            if (NodeName != "IFRAME")
            {
                var parentNode = Util.FilterRecurse(doc, n => n.NodeId == _node.ParentId);
                if (parentNode != null)
                    _parent = new Element(parentNode, _tab, _tree);
            }
            return this;
        }

        //ok
        public async Task ClickAsync(CancellationToken token = default)
        {
            var result = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId: BackendNodeId), token: token);
            _remoteObject = result.Object;

            await FlashAsync(0.25, token);
            await _tab.SendAsync(Cdp.Runtime.CallFunctionOn(
                "(el) => el.click()",
                ObjectId: _remoteObject.ObjectId,
                Arguments: new List<Cdp.Runtime.CallArgument>
                {
                    new Cdp.Runtime.CallArgument(ObjectId: _remoteObject.ObjectId)
                },
                AwaitPromise: true,
                UserGesture: true,
                ReturnByValue: true), token: token);
        }

        public async Task<JsonNode?> GetJsAttributesAsync()
        {
            var (remoteObj, exception) = await ApplyAsync(@"
                function (e) {
                    let o = {}
                    for(let k in e){
                        o[k] = e[k]
                    }
                    return JSON.stringify(o)
                }");
            return remoteObj.Value;
            //return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonStr);
        }

        //ok 要測試
        public async Task<(Cdp.Runtime.RemoteObject remoteObject, Cdp.Runtime.ExceptionDetails? exception)> CallAsync(string jsMethod, CancellationToken token = default)
        {
            return await ApplyAsync($"(e) => e['{jsMethod}']()", token: token);
        }

        //ok 要測試
        public async Task<(Cdp.Runtime.RemoteObject remoteObject, Cdp.Runtime.ExceptionDetails? exception)> ApplyAsync(string jsFunction, bool returnByValue = true, CancellationToken token = default)
        {
            var resolveResult = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId: BackendNodeId), token: token);
            _remoteObject = resolveResult.Object;

            var result = await _tab.SendAsync(Cdp.Runtime.CallFunctionOn(
                jsFunction,
                ObjectId: _remoteObject.ObjectId,
                Arguments: new List<Cdp.Runtime.CallArgument>
                {
                    new Cdp.Runtime.CallArgument(ObjectId: _remoteObject.ObjectId)
                },
                ReturnByValue: true,
                UserGesture: true), token: token);

            return (result.Result, result.ExceptionDetails);
        }

        //ok 要檢查 value 是不是有正確轉換
        public async Task<Position?> GetPositionAsync(bool abs = false, CancellationToken token = default)
        {
            if (Parent == null || _remoteObject?.ObjectId == null)
            {
                var result = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId: BackendNodeId), token: token);
                _remoteObject = result.Object;
            }

            try
            {
                var result = await _tab.SendAsync(Cdp.DOM.GetContentQuads(ObjectId: _remoteObject.ObjectId), token: token);
                var quads = result.Quads;
                if (quads == null || quads.Count == 0)
                    throw new Exception($"Could not find position for {this}");

                var pos = new Position(quads[0].Items);
                if (abs)
                {
                    async Task<double> getValue(string expression)
                    {
                        var (remoteObj, exception) = await _tab.EvaluateAsync(expression, token: token);

                        var value = remoteObj?.Value?.ToString();

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            if (double.TryParse(value, out var result))
                            {
                                return result;
                            }
                        }
                        return 0;
                    }

                    var scrollY = await getValue("window.scrollY");
                    var scrollX = await getValue("window.scrollX");

                    return pos with
                    {
                        AbsX = pos.Left + scrollX + pos.Width / 2.0,
                        AbsY = pos.Top + scrollY + pos.Height / 2.0
                    };
                }
                return pos;
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine($"No content quads for {this}. Mostly caused by element not in plain sight.");
            }
            return null;
        }

        //ok 要測試
        public async Task MouseClickAsync(string button = "left", int buttons = 1, int modifiers = 0, CancellationToken token = default)
        {
            var pos = await GetPositionAsync(token: token);
            if (pos?.Center == null)
            {
                Console.WriteLine($"Could not calculate box model for {this}");
                return;
            }

            Console.WriteLine($"Clicking on location {pos.Center.X}, {pos.Center.Y}");

            await _tab.MouseClickAsync(pos.Center.X, pos.Center.Y, token: token);
            await _tab.FlashPointAsync(pos.Center.X, pos.Center.Y, token: token);
        }

        //ok
        public async Task MouseMoveAsync(CancellationToken token = default)
        {
            var pos = await GetPositionAsync(token: token);
            if (pos?.Center == null)
            {
                Console.WriteLine($"Did not find location for {this}");
                return;
            }

            Console.WriteLine($"Mouse move to location {pos.Center.X}, {pos.Center.Y} where {this} is located");

            await _tab.MouseMoveAsync(pos.Center.X, pos.Center.Y, token: token);
        }

        //ok
        public async Task MouseDragAsync(Element destElement, int steps = 1, CancellationToken token = default)
        {
            var endPos = await destElement.GetPositionAsync(token: token);
            if (endPos?.Center == null)
            {
                Console.WriteLine($"Could not calculate box model for {destElement}");
                return;
            }
            await MouseDragAsync(endPos.Center, false, steps, token);
        }

        //ok
        public async Task MouseDragAsync((double X, double Y) destPoint, bool relative = false, int steps = 1, CancellationToken token = default)
        {
            var startPos = await GetPositionAsync(token: token);
            if (startPos?.Center == null)
            {
                Console.WriteLine($"Could not calculate box model for {this}");
                return;
            }

            var endPoint = destPoint;
            if (relative)
                endPoint = (startPos.Center.X + destPoint.X, startPos.Center.Y + destPoint.Y);

            await _tab.MouseDragAsync(startPos.Center, endPoint, false, steps, token);
        }

        //ok
        public async Task ScrollIntoViewAsync(CancellationToken token = default)
        {
            try
            {
                await Tab.SendAsync(Cdp.DOM.ScrollIntoViewIfNeeded(BackendNodeId: BackendNodeId), token: token);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not scroll into view: {e.Message}");
            }
        }

        //ok
        public async Task ClearInputAsync(CancellationToken token = default)
        {
            await ApplyAsync(@"function (element) { element.value = """" } ", token: token);
        }

        //ok
        public async Task SendKeysAsync(string text, CancellationToken token = default)
        {
            await ApplyAsync("(elem) => elem.focus()", token: token);
            foreach (var c in text)
            {
                await _tab.SendAsync(Cdp.Input.DispatchKeyEvent("char", Text: c.ToString()), token: token);
            }
        }

        //ok
        public async Task SendFileAsync(params string[] filePaths)
        {
            await SendFileAsync(filePaths);
        }

        //ok 要測試
        public async Task SendFileAsync(List<string> filePaths, CancellationToken token = default)
        {
            await _tab.SendAsync(Cdp.DOM.SetFileInputFiles(
                Files: filePaths.ToList(),
                BackendNodeId: BackendNodeId,
                ObjectId: ObjectId), token: token);
        }

        //ok 要測試
        public async Task FocusAsync(CancellationToken token = default)
        {
            await ApplyAsync("(element) => element.focus()", token: token);
        }

        //ok 要測試
        public async Task SelectOptionAsync(CancellationToken token = default)
        {
            if (NodeName == "OPTION")
            {
                await ApplyAsync(@"
                    (o) => {  
                        o.selected = true ; 
                        o.dispatchEvent(new Event('change', {view: window, bubbles: true}))
                    }", token: token);
            }
        }

        //ok 要測試
        public async Task SetValueAsync(string value, CancellationToken token = default)
        {
            await _tab.SendAsync(Cdp.DOM.SetNodeValue(NodeId: NodeId, Value: value), token: token);
        }

        //ok 要測試
        public async Task SetTextAsync(string value, CancellationToken token = default)
        {
            if (NodeType != 3)
            {
                if (ChildNodeCount == 1)
                {
                    var childNode = Children.First();
                    await childNode.SetTextAsync(value, token);
                    await UpdateAsync(token: token);
                    return;
                }
                throw new InvalidOperationException("Could only set value of text nodes.");
            }
            await UpdateAsync(token: token);
            await _tab.SendAsync(Cdp.DOM.SetNodeValue(NodeId: NodeId, Value: value), token: token);
        }

        //ok 要測試
        public async Task<string> GetHtmlAsync(CancellationToken token = default)
        {
            var result = await _tab.SendAsync(Cdp.DOM.GetOuterHTML(BackendNodeId: BackendNodeId), token: token);
            return result.OuterHTML;
        }

        //ok 要測試
        public async Task<List<Element>> QuerySelectorAllAsync(string selector, CancellationToken token = default)
        {
            await UpdateAsync(token: token);
            return await _tab.QuerySelectorAllAsync(selector, this, token: token);
        }

        //ok 要測試
        public async Task<Element?> QuerySelectorAsync(string selector, CancellationToken token = default)
        {
            await UpdateAsync(token: token);
            return await _tab.QuerySelectorAsync(selector, this, token: token);
        }

        //ok 檢查是否正常 Tab 也有相同函數
        public async Task<string> SaveScreenshotAsync(string filename = "auto", string format = "jpeg", double scale = 1, CancellationToken token = default)
        {
            var pos = await GetPositionAsync(token: token);
            if (pos == null)
                throw new InvalidOperationException("Could not determine position of element. Probably because it's not in view, or hidden.");

            var viewport = pos.ToViewport(scale);
            await _tab.WaitAsync(1, token: token);

            var path = "";
            if (string.IsNullOrWhiteSpace(filename) || filename == "auto")
            {
                if (_tab.Target != null)
                {
                    var uri = new Uri(_tab.Target.Url);
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

            var result = await _tab.SendAsync(
                Cdp.Page.CaptureScreenshot(format, Clip: viewport, CaptureBeyondViewport: true), token: token);
            var base64Data = result.Data;
            if (string.IsNullOrWhiteSpace(base64Data))
                throw new InvalidOperationException("Could not take screenshot. most possible cause is the page has not finished loading yet.");

            var dataBytes = Convert.FromBase64String(base64Data);
            await File.WriteAllBytesAsync(path, dataBytes, token);
            return path;
        }

        //ok
        public async Task FlashAsync(double duration = 0.5, CancellationToken token = default)
        {
            if (_remoteObject == null)
            {
                try
                {
                    var result = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId: BackendNodeId), token: token);
                    _remoteObject = result.Object;
                }
                catch (ProtocolErrorException)
                {
                    return;
                }
            }

            var pos = null as Position;
            try
            {
                pos = await GetPositionAsync(token: token);
                if (pos?.Center == null)
                    throw new InvalidOperationException();
            }
            catch
            {
                Console.WriteLine("FlashAsync() : could not determine position.");
                return;
            }

            var id = Guid.NewGuid().ToString("N").Substring(0, 16);
            var style =
                $$"""
                    position:absolute;z-index:99999999;padding:0;margin:0;
                    left:{{pos.Center.X}}px; top: {{pos.Center.Y}}px;
                    opacity:1;
                    width:16px;height:16px;border-radius:50%;background:red;
                    animation:show-pointer-ani {{duration}}s ease 1;
                """;
            var script =
                $$"""
                (targetElement) => {
                    var css = document.styleSheets[0];
                    for( let css of [...document.styleSheets]) {
                        try {
                            css.insertRule(`
                            @keyframes show-pointer-ani {
                                  0% { opacity: 1; transform: scale(2, 2);}
                                  25% { transform: scale(5,5) }
                                  50% { transform: scale(3, 3);}
                                  75%: { transform: scale(2,2) }
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
                }
                """;

            await _tab.SendAsync(Cdp.Runtime.CallFunctionOn(
                script,
                ObjectId: _remoteObject.ObjectId,
                Arguments: new List<Cdp.Runtime.CallArgument>
                {
                    new Cdp.Runtime.CallArgument(ObjectId: _remoteObject.ObjectId)
                },
                AwaitPromise: true,
                UserGesture: true), token: token);
        }

        //ok 要測試
        public async Task HighlightOverlayAsync(CancellationToken token = default)
        {
            if (_isHighlighted)
            {
                _isHighlighted = false;
                await _tab.SendAsync(Cdp.Overlay.HideHighlight(), token: token);
                await _tab.SendAsync(Cdp.DOM.Disable(), token: token);
                await _tab.SendAsync(Cdp.Overlay.Disable(), token: token);
                return;
            }

            await _tab.SendAsync(Cdp.DOM.Enable(), token: token);
            await _tab.SendAsync(Cdp.Overlay.Enable(), token: token);
            await _tab.SendAsync(Cdp.Overlay.HighlightNode(
                HighlightConfig: new Cdp.Overlay.HighlightConfig
                {
                    ShowInfo = true,
                    ShowExtensionLines = true,
                    ShowStyles = true
                },
                BackendNodeId: BackendNodeId), token: token);
            _isHighlighted = true;
        }

        //ok 要測試
        public async Task RecordVideoAsync(string? filename = null, string? folder = null, double? duration = null, CancellationToken token = default)
        {
            if (NodeName != "VIDEO")
                throw new InvalidOperationException("RecordVideoAsync can only be called on html5 video elements.");

            var directoryPath = folder;
            if (string.IsNullOrWhiteSpace(directoryPath))
                directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
            directoryPath = Path.GetFullPath(directoryPath);
            Directory.CreateDirectory(directoryPath);

            await _tab.SendAsync(Cdp.Browser.SetDownloadBehavior("allow", DownloadPath: directoryPath), token: token);
            await CallAsync("pause", token);

            var jsDuration = 0.0d;
            if (duration != null)
                jsDuration = Math.Floor(duration.Value * 1000);

            var jsFilename = @"document.title + "".mp4""";
            if (!string.IsNullOrWhiteSpace(filename))
                jsFilename = $@"""{filename}""";

            var script =
                $$"""
                    function extractVid(vid) {
                        var duration = {{jsDuration}}; 
                        var stream = vid.captureStream();
                        var mr = new MediaRecorder(stream, {audio:true, video:true})
                        mr.ondataavailable  = function(e) {
                            vid['_recording'] = false
                            var blob = e.data;
                            f = new File([blob], {name: {{jsFilename}}, type:'octet/stream'});
                            var objectUrl = URL.createObjectURL(f);
                            var link = document.createElement('a');
                            link.setAttribute('href', objectUrl)
                            link.setAttribute('download', {{jsFilename}})
                            link.style.display = 'none'

                            document.body.appendChild(link)

                            link.click()

                            document.body.removeChild(link)
                        }
                        
                        mr.start()
                        vid.addEventListener('ended' , (e) => mr.stop())
                        vid.addEventListener('pause' , (e) => mr.stop())
                        vid.addEventListener('abort', (e) => mr.stop())
                       
                        if ( duration ) {
                            setTimeout(() => { vid.pause(); vid.play() }, duration);
                        }
                        vid['_recording'] = true
                    ;}
                """;

            await ApplyAsync(script, token: token);
            await CallAsync("play", token);
            await _tab.WaitAsync(token: token);
        }

        //ok 要檢查 value 是不是有正確轉換
        public async Task<bool> IsRecordingAsync(CancellationToken token = default)
        {
            var (remoteObj, exception) = await ApplyAsync(@"(vid) => vid[""_recording""]", token: token);

            var value = remoteObj?.Value?.ToString();

            if (!string.IsNullOrWhiteSpace(value))
            {
                if (bool.TryParse(value, out var isRecording))
                {
                    return isRecording;
                }
            }
            return false;
        }

        //ok
        private void MakeAttrs()
        {
            if (_node.Attributes != null)
            {
                for (var i = 0; i < _node.Attributes.Count - 1; i += 2)
                {
                    var key = _node.Attributes[i];
                    var value = _node.Attributes[i + 1];
                    _attrs[key] = value;
                }
            }
        }

        public bool Equals(Element? other)
        {
            if (other == null)
                return false;
            if (other.BackendNodeId == null || BackendNodeId == null)
                return false;
            return other.BackendNodeId == BackendNodeId;
        }

        public override bool Equals(object? obj) => Equals(obj as Element);

        public override int GetHashCode() => BackendNodeId?.GetHashCode() ?? 0;

        public static bool operator ==(Element? left, Element? right)
        {
            if (left is null) 
                return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(Element? left, Element? right) => !(left == right);

        public override string ToString()
        {
            var tagName = NodeName.ToLowerInvariant();

            var content = new StringBuilder();
            if (ChildNodeCount > 0)
            {
                foreach (var child in Children)
                    content.Append(child.ToString());
            }
            if (NodeType == 3)
            {
                content.Append(NodeValue);
                return content.ToString();
            }

            var attrStrings = _attrs.Select(kvp => $@"{kvp.Key}=""{kvp.Value}""");
            var attrs = string.Join(" ", attrStrings);

            return $"<{tagName} {attrs}>{content}</{tagName}>";
        }
    }
}
