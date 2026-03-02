using NoDriver.Core.Messaging;
using System.Collections.Concurrent;
using System.Text.Json;
using static NoDriver.Cdp.DOM;
using static NoDriver.Cdp.Runtime;

namespace NoDriver.Core.Runtime
{
    public class Element
    {
        private Tab _tab;
        private Node _node;
        private Node? _tree;
        private Element? _parent = null;
        private RemoteObject? _remoteObject = null;
        private ConcurrentDictionary<string, string> _attrs = new(StringComparer.OrdinalIgnoreCase);

        public string Tag => NodeName.ToLowerInvariant();
        public string TagName => Tag;
        public NodeId NodeId => _node.NodeId;
        public BackendNodeId BackendNodeId => _node.BackendNodeId;
        public int NodeType => _node.NodeType;
        public string NodeName => _node.NodeName;
        public string LocalName => _node.LocalName;
        public string NodeValue => _node.NodeValue;
        public NodeId? ParentId => _node.ParentId;
        public int? ChildNodeCount => _node.ChildNodeCount;
        public IReadOnlyList<string>? Attributes => _node.Attributes;
        public string? DocumentUrl => _node.DocumentURL;
        public string? BaseUrl => _node.BaseURL;
        public string? PublicId => _node.PublicId;
        public string? SystemId => _node.SystemId;
        public string? InternalSubset => _node.InternalSubset;
        public string? XmlVersion => _node.XmlVersion;
        public string? Value => _node.Value;
        public PseudoType? PseudoType => _node.PseudoType;
        public string? PseudoIdentifier => _node.PseudoIdentifier;
        public ShadowRootType? ShadowRootType => _node.ShadowRootType;
        public string? FrameId => _node.FrameId?.Value;
        public Node? ContentDocument => _node.ContentDocument;
        public IReadOnlyList<Node>? ShadowRoots => _node.ShadowRoots;
        public Node? TemplateContent => _node.TemplateContent;
        public IReadOnlyList<Node>? PseudoElements => _node.PseudoElements;
        public Node? ImportedDocument => _node.ImportedDocument;
        public IReadOnlyList<BackendNode>? DistributedNodes => _node.DistributedNodes;
        public bool? IsSvg => _node.IsSVG;
        public CompatibilityMode? CompatibilityMode => _node.CompatibilityMode;
        public BackendNode? AssignedSlot => _node.AssignedSlot;
        public Tab Tab => _tab;
        public List<Element> ShadowChildren
        {
            get
            {
                if (ShadowRoots != null && ShadowRoots.Count > 0)
                {
                    var root = ShadowRoots[0];
                    if (root.ShadowRootType == Cdp.DOM.ShadowRootType.Open)
                    {
                        return root.Children?.Select(child => ElementFactory.Create(child, Tab)).ToList();
                    }
                }
                return null;
            }
        }
        public Cdp.DOM.Node Node => _node;
        public Cdp.DOM.Node Tree
        {
            get => _tree;
            set => _tree = value;
        }
        public ConcurrentDictionary<string, string> Attrs => _attrs;
        public Element Parent
        {
            get
            {
                if (Tree == null)
                    throw new InvalidOperationException("Could not get parent since the element has no tree set.");
                var parentNode = Util.FilterRecurse(Tree, n => n.NodeId == ParentId);
                if (parentNode == null)
                    return null;
                return ElementFactory.Create(parentNode, _tab, Tree);
            }
        }
        public List<Element> Children
        {
            get
            {
                var children = new List<Element>();
                if (_node.NodeName == "IFRAME")
                {
                    var frame = _node.ContentDocument;
                    if (frame?.ChildNodeCount == null || frame.ChildNodeCount == 0)
                        return children;

                    if (frame.Children != null)
                    {
                        foreach (var child in frame.Children)
                        {
                            var childElem = ElementFactory.Create(child, _tab, frame);
                            if (childElem != null)
                                children.Add(childElem);
                        }
                    }
                    return children;
                }

                if (_node.ChildNodeCount == null || _node.ChildNodeCount == 0)
                    return children;

                if (_node.Children != null)
                {
                    foreach (var child in _node.Children)
                    {
                        var childElem = ElementFactory.Create(child, _tab, Tree);
                        if (childElem != null)
                            children.Add(childElem);
                    }
                }
                return children;
            }
        }
        public RemoteObject? RemoteObject => _remoteObject;
        public RemoteObjectId? ObjectScsid => RemoteObject?.ObjectId;
        public string Text
        {
            get
            {
                var textNode = Util.FilterRecurse(_node, n => n.NodeType == 3);
                return textNode?.NodeValue ?? "";
            }
        }
        public string TextAll
        {
            get
            {
                var textNodes = Util.FilterRecurseAll(_node, n => n.NodeType == 3);
                return string.Join(" ", textNodes.Select(n => n.NodeValue));
            }
        }

        public Element(Node node, Tab tab, Node? tree = null)
        {
            if (node == null)
                throw new ArgumentException("Node cannot be null.");

            _tab = tab;
            _tree = tree;
            MakeAttrs();
        }

        public async Task SaveToDomAsync()
        {
            _remoteObject = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId));
            await _tab.SendAsync(Cdp.DOM.SetOuterHtml(NodeId, ToString()));
            await UpdateAsync();
        }

        public async Task RemoveFromDomAsync()
        {
            await UpdateAsync();
            var node = Util.FilterRecurse(_tree, n => n.BackendNodeId == BackendNodeId);
            if (node != null)
            {
                await Tab.SendAsync(Cdp.DOM.RemoveNode(node.NodeId));
            }
        }

        public async Task<Element> UpdateAsync(Node node = null)
        {
            var doc = null as Node;
            if (node != null)
            {
                doc = node;
                _parent = null;
            }
            else
            {
                doc = await _tab.SendAsync(GetDocument(-1, true));
                _parent = null;
            }

            var updatedNode = Util.FilterRecurse(doc, n => n.BackendNodeId == _node.BackendNodeId);
            if (updatedNode != null)
            {
                Console.WriteLine("Node seems changed, and has now been updated.");
                _node = updatedNode;
            }
            _tree = doc;

            _remoteObject = await _tab.SendAsync(Cdp.DOM.ResolveNode(_node.BackendNodeId));
            _attrs.Clear();
            MakeAttrs();

            if (NodeName != "IFRAME")
            {
                var parentNode = Util.FilterRecurse(doc, n => n.NodeId == _node.ParentId);
                if (parentNode != null)
                {
                    _parent = ElementFactory.Create(parentNode, _tab, _tree);
                }
            }
            return this;
        }

        public async Task ClickAsync()
        {
            _remoteObject = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId));
            var arguments = new List<CallArgument>
            {
                new CallArgument { ObjectId = _remoteObject.ObjectId }
            };

            await FlashAsync(0.25);
            await _tab.SendAsync(CallFunctionOn(
                "(el) => el.click()",
                objectId: _remoteObject.ObjectId,
                arguments: arguments,
                awaitPromise: true,
                userGesture: true,
                returnByValue: true
            ));
        }

        public async Task<Dictionary<string, object>> GetJsAttributesAsync()
        {
            var jsonStr = (string)await ApplyAsync(@"
                function (e) {
                    let o = {}
                    for(let k in e){
                        o[k] = e[k]
                    }
                    return JSON.stringify(o)
                }");
            return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonStr);
        }

        public async Task<object> ApplyAsync(string jsFunction, bool returnByValue = true)
        {
            _remoteObject = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId));
            var result = await _tab.SendAsync(CallFunctionOn(
                jsFunction,
                objectId: _remoteObject.ObjectId,
                arguments: new List<CallArgument>
                {
                    new CallArgument { ObjectId = _remoteObject.ObjectId }
                },
                returnByValue: true,
                userGesture: true
            ));

            if (result != null && result.Result != null)
            {
                if (returnByValue)
                    return result.Result.Value;
                return result.Result;
            }
            return result?.ExceptionDetails;

            //if result and result[0]:
            //    if return_by_value:
            //        return result[0].value
            //    return result[0]
            //elif result[1]:
            //    return result[1]
        }

        public async Task<Position> GetPositionAsync(bool abs = false)
        {
            if (Parent == null || ObjectId == null)
            {
                _remoteObject = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId));
            }

            try
            {
                var quads = await _tab.SendAsync(Cdp.DOM.GetContentQuads(_remoteObject.ObjectId));
                if (quads == null || quads.Count == 0)
                    throw new Exception($"Could not find position for {this}");

                var pos = new Position(quads[0]);
                if (abs)
                {
                    // Assuming Tab.EvaluateAsync returns a convertible type
                    var scrollY = Convert.ToDouble(await _tab.EvaluateAsync("window.scrollY"));
                    var scrollX = Convert.ToDouble(await _tab.EvaluateAsync("window.scrollX"));
                    pos.AbsX = pos.Left + scrollX + pos.Width / 2.0;
                    pos.AbsY = pos.Top + scrollY + pos.Height / 2.0;
                }
                return pos;
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine($"No content quads for {this}. Mostly caused by element not in plain sight.");
            }
            return null;
        }

        public async Task MouseClickAsync(string button = "left", int buttons = 1, int modifiers = 0)
        {
            var pos = await GetPositionAsync();

            var center = pos?.Center;
            if (center == null)
            {
                Console.WriteLine($"Could not calculate box model for {this}");
                return;
            }

            Console.WriteLine($"Clicking on location {center.X}, {center.Y}");

            await _tab.MouseClickAsync(pos.Center.X, pos.Center.Y);
            await _tab.FlashPointAsync(pos.Center.X, pos.Center.Y);
        }

        public async Task MouseMoveAsync()
        {
            var pos = await GetPositionAsync();

            var center = pos?.Center;
            if (center == null)
            {
                Console.WriteLine($"Did not find location for {this}");
                return;
            }

            Console.WriteLine($"Mouse move to location {center.X}, {center.Y} where {this} is located");

            await _tab.MouseMoveAsync(center.X, center.Y);
        }

        public async Task MouseDragAsync(object destination, bool relative = false, int steps = 1)
        {
            var startPos = await GetPositionAsync();

            var startPoint = startPos?.Center;
            if (startPoint == null)
            {
                Console.WriteLine($"Could not calculate box model for {this}");
                return;
            }

            var endPoint = null as Position;

            if (destination is Element destElement)
            {
                var endPos = await destElement.GetPositionAsync();

                endPoint = endPos?.Center;
                if (endPoint == null)
                {
                    Console.WriteLine($"Could not calculate box model for {destElement}");
                    return;
                }
            }
            else if (destination is ValueTuple<double, double> coords)
            {
                if (relative)
                {
                    endPoint = (startPoint.X + coords.Item1, startPoint.Y + coords.Item2);
                }
                else
                {
                    endPoint = coords;
                }
            }
            await _tab.MouseDragAsync(startPoint, endPoint, relative, steps);
        }

        public async Task ScrollIntoViewAsync()
        {
            try
            {
                await Tab.SendAsync(Cdp.DOM.ScrollIntoViewIfNeeded(BackendNodeId));
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not scroll into view: {e.Message}");
            }
        }

        public async Task ClearInputAsync()
        {
            await ApplyAsync("function (element) { element.value = \"\" } ");
        }

        public async Task SendKeysAsync(string text)
        {
            await ApplyAsync("(elem) => elem.focus()");
            foreach (var ch in text)
            {
                await _tab.SendAsync(Cdp.Input.DispatchKeyEvent("char", text: ch.ToString()));
            }
        }

        public async Task SendFileAsync(params string[] filePaths)
        {
            await _tab.SendAsync(Cdp.DOM.SetFileInputFiles(
                files: filePaths.ToList(),
                backendNodeId: BackendNodeId,
                objectId: ObjectId
            ));
        }

        public async Task FocusAsync()
        {
            await ApplyAsync("(element) => element.focus()");
        }

        public async Task SelectOptionAsync()
        {
            if (NodeName == "OPTION")
            {
                await ApplyAsync(@"
                    (o) => {  
                        o.selected = true ; 
                        o.dispatchEvent(new Event('change', {view: window, bubbles: true}))
                    }");
            }
        }

        public async Task SetValueAsync(string value)
        {
            await _tab.SendAsync(SetNodeValue(NodeId, value));
        }

        public async Task SetTextAsync(string value)
        {
            if (NodeType != 3)
            {
                if (ChildNodeCount == 1)
                {
                    var childNode = Children[0];
                    await childNode.SetTextAsync(value);
                    await UpdateAsync();
                    return;
                }
                throw new InvalidOperationException("Could only set value of text nodes.");
            }
            await UpdateAsync();
            await _tab.SendAsync(SetNodeValue(NodeId, value));
        }

        public async Task<string> GetHtmlAsync()
        {
            return await _tab.SendAsync(Cdp.DOM.GetOuterHtml(BackendNodeId));
        }

        public async Task<List<Element>> QuerySelectorAllAsync(string selector)
        {
            await UpdateAsync();
            return await _tab.QuerySelectorAllAsync(selector, node: this);
        }

        public async Task<Element> QuerySelectorAsync(string selector)
        {
            await UpdateAsync();
            return await _tab.QuerySelectorAsync(selector, this);
        }

        public async Task<string> SaveScreenshotAsync(string filename = "auto", string format = "jpeg", double scale = 1)
        {
            var pos = await GetPositionAsync();
            if (pos == null)
                throw new InvalidOperationException("Could not determine position of element. Probably because it's not in view, or hidden.");

            var viewport = pos.ToViewport(scale);
            await _tab.SleepAsync();

            var path = "";
            if (string.IsNullOrWhiteSpace(filename) || filename == "auto")
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
                path = candidate + ext;
            }
            else
            {
                path = filename;
            }

            if (string.IsNullOrWhiteSpace(path))
                throw new Exception($"Invalid filename or path: '{filename}'");

            var parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentDir))
                Directory.CreateDirectory(parentDir);

            var base64Data = await _tab.SendAsync(Cdp.Page.CaptureScreenshot(format, clip: viewport, captureBeyondViewport: true));
            if (string.IsNullOrWhiteSpace(base64Data))
                throw new Exception("Could not take screenshot. most possible cause is the page has not finished loading yet.");

            var dataBytes = Convert.FromBase64String(base64Data);
            await File.WriteAllBytesAsync(path, dataBytes);
            return path;
        }

        public async Task FlashAsync(double duration = 0.5)
        {
            if (_remoteObject == null)
            {
                try
                {
                    _remoteObject = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId));
                }
                catch (ProtocolErrorException)
                {
                    return;
                }
            }

            var pos = null as Position;
            try
            {
                pos = await GetPositionAsync();
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

            var arguments = new List<CallArgument>
            {
                new CallArgument { ObjectId = _remoteObject.ObjectId }
            };
            await _tab.SendAsync(CallFunctionOn(
                script,
                objectId: _remoteObject.ObjectId,
                arguments: arguments,
                awaitPromise: true,
                userGesture: true
            ));
        }

        private bool _isHighlighted = false;
        public async Task HighlightOverlayAsync()
        {
            if (_isHighlighted)
            {
                _isHighlighted = false;
                await _tab.SendAsync(Cdp.Overlay.HideHighlight());
                await _tab.SendAsync(Cdp.DOM.Disable());
                await _tab.SendAsync(Cdp.Overlay.Disable());
                return;
            }

            await _tab.SendAsync(Cdp.DOM.Enable());
            await _tab.SendAsync(Cdp.Overlay.Enable());
            var conf = new Cdp.Overlay.HighlightConfig 
            { 
                ShowInfo = true, 
                ShowExtensionLines = true, 
                ShowStyles = true 
            };
            await _tab.SendAsync(Cdp.Overlay.HighlightNode(highlightConfig: conf, backendNodeId: BackendNodeId));
            _isHighlighted = true;
        }

        public async Task RecordVideoAsync(string? filename = null, string? folder = null, double? duration = null)
        {
            if (NodeName != "VIDEO") 
                throw new InvalidOperationException("RecordVideoAsync can only be called on html5 video elements.");

            var directoryPath = folder;
            if (string.IsNullOrWhiteSpace(directoryPath))
                directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
            Directory.CreateDirectory(directoryPath);

            await _tab.SendAsync(Cdp.Browser.SetDownloadBehavior("allow", downloadPath: directoryPath));
            await InvokeAsync("pause");

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

            await ApplyAsync(script);
            await InvokeAsync("play");
            await _tab;
        }

        public async Task<bool> IsRecordingAsync()
        {
            var result = await ApplyAsync(@"(vid) => vid[""_recording""]");
            return result is bool b && b;
        }

        private void MakeAttrs()
        {
            var sav = null as string;
            if (_node.Attributes != null)
            {
                for (var i = 0; i < _node.Attributes.Count; i++)
                {
                    var a = _node.Attributes[i];
                    if (i == 0 || i % 2 == 0)
                    {
                        sav = a;
                    }
                    else
                    {
                        if (sav != null) 
                            _attrs[sav] = a;
                    }
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

            var content = "";
            if (ChildNodeCount > 0)
            {
                if (Children != null)
                {
                    foreach (var child in Children)
                    {
                        content += child.ToString();
                    }
                }
            }

            if (NodeType == 3)
            {
                content += NodeValue;
                return content;
            }

            var attrStrings = _attrs.Select(kvp => $@"{kvp.Key}=""{kvp.Value}""");
            var attrs = string.Join(" ", attrStrings);

            return $"<{tagName} {attrs}>{content}</{tagName}>";
        }
    }
}
