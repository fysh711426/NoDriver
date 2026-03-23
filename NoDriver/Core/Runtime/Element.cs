using NoDriver.Core.Messaging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
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
        public IEnumerable<Element> ShadowChildren
        {
            get
            {
                if (ShadowRoots?.Count > 0)
                {
                    var root = ShadowRoots[0];
                    if (root.ShadowRootType == Cdp.DOM.ShadowRootType.OPEN)
                        if (root.Children != null)
                            foreach (var child in root.Children)
                                yield return new Element(child, _tab);
                }
            }
        }
        public Cdp.DOM.Node Node => _node;
        public Cdp.DOM.Node? Tree
        {
            get => _tree;
            set => _tree = value;
        }
        public ConcurrentDictionary<string, string> Attrs => _attrs;
        /// <summary>
        /// Get the parent element (node) of current element(node)
        /// </summary>
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
        /// <summary>
        /// Returns the elements' children. those children also have a children property<br/>
        /// so you can browse through the entire tree as well.
        /// </summary>
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
        /// <summary>
        /// Gets the text contents of this element.<br/>
        /// note: this includes text in the form of script content, as those are also just 'text nodes'
        /// </summary>
        public string Text
        {
            get
            {
                var textNode = Util.FilterRecurse(_node, n => n.NodeType == 3);
                return textNode?.NodeValue ?? "";
            }
        }
        /// <summary>
        /// Gets the text contents of this element, and it's children in a concatenated string.<br/>
        /// note: this includes text in the form of script content, as those are also just 'text nodes'
        /// </summary>
        public string TextAll
        {
            get
            {
                var textNodes = Util.FilterRecurseAll(_node, n => n.NodeType == 3);
                return string.Join(" ", textNodes.Select(n => n.NodeValue));
            }
        }

        /// <summary>
        /// Represents an (HTML) DOM Element
        /// </summary>
        /// <param name="node">Cdp dom node representation.</param>
        /// <param name="tab">The target object to which this element belongs.</param>
        /// <param name="tree">The full node tree to which &lt;node&gt; belongs, enhances performance.<br/>
        /// When not provided, you need to call `await elem.UpdateAsync()` before using .Children / .Parent</param>
        /// <exception cref="ArgumentException"></exception>
        public Element(Cdp.DOM.Node node, Tab tab, Cdp.DOM.Node? tree = null)
        {
            if (node == null)
                throw new ArgumentException("Node cannot be null.");

            _tab = tab;
            _node = node;
            _tree = tree;
            MakeAttrs();
        }

        public async Task<string> GetHtmlAsync(CancellationToken token = default)
        {
            var result = await _tab.SendAsync(Cdp.DOM.GetOuterHTML(BackendNodeId: BackendNodeId), token: token);
            return result.OuterHTML;
        }

        /// <summary>
        /// Saves element to dom.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task SaveToDomAsync(CancellationToken token = default)
        {
            var result = await _tab.SendAsync(Cdp.DOM.ResolveNode(BackendNodeId: BackendNodeId), token: token);
            _remoteObject = result.Object;
            await _tab.SendAsync(Cdp.DOM.SetOuterHTML(NodeId: NodeId, OuterHTML: $"{this}"), token: token);
            await UpdateAsync(token: token);
        }

        /// <summary>
        /// Removes the element from dom.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task RemoveFromDomAsync(CancellationToken token = default)
        {
            await UpdateAsync(token: token);
            var node = Util.FilterRecurse(_tree, n => n.BackendNodeId == BackendNodeId);
            if (node != null)
                await Tab.SendAsync(Cdp.DOM.RemoveNode(NodeId: node.NodeId), token: token);
        }

        /// <summary>
        /// Updates element to retrieve more properties. for example this enables<br/>
        /// `Children` and `Parent` attributes.<br/>
        /// <br/>
        /// Also resolves js opbject which is stored object in `RemoteObject`<br/>
        /// <br/>
        /// Usually you will get element nodes by the usage of<br/>
        /// <br/>
        /// `Tab.QuerySelectorAllAsync()`<br/>
        /// `Tab.FindElementsByTextAsync()`<br/>
        /// <br/>
        /// Those elements are already updated and you can browse through children directly.<br/>
        /// <br/>
        /// The reason for a seperate call instead of doing it at initialization,<br/>
        /// is because when you are retrieving 100+ elements this becomes quite expensive.<br/>
        /// <br/>
        /// Therefore, it is not advised to call this method on a bunch of blocks (100+) at the same time.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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
            if (remoteObj.Value != null)
                return JsonSerializer.Deserialize<JsonNode>(remoteObj.Value.ToString());
            return null;
        }

        /// <summary>
        /// Calling the element object will call a js method on the object.<br/>
        /// eg, element.CallAsync("play") in case of a video element, it will call .play()
        /// </summary>
        /// <param name="jsMethod"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<(Cdp.Runtime.RemoteObject remoteObject, Cdp.Runtime.ExceptionDetails? exception)> CallAsync(string jsMethod, CancellationToken token = default)
        {
            return await ApplyAsync($"(e) => e['{jsMethod}']()", token: token);
        }

        /// <summary>
        /// Apply javascript to this element. the given jsFunction string should accept the js element as parameter,<br/>
        /// and can be a arrow function, or function declaration.<br/>
        /// eg:<br/>
        ///     - '(elem) => { elem.value = "blabla"; consolelog(elem); alert(JSON.stringify(elem); } '<br/>
        ///     - 'elem => elem.play()'<br/>
        ///     - function myFunction(elem) { alert(elem) }
        /// </summary>
        /// <param name="jsFunction">The js function definition which received this element.</param>
        /// <param name="returnByValue"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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
                        return remoteObj?.Value?.GetValue<double>() ?? 0;
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

        /// <summary>
        /// Click the element.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Native click (on element). note: this likely does not work atm, use ClickAsync() instead.
        /// </summary>
        /// <param name="button"></param>
        /// <param name="buttons"></param>
        /// <param name="modifiers">Bit field representing pressed modifier keys.<br/>
        /// Alt=1, Ctrl=2, Meta/Command=4, Shift=8 (default: 0).</param>
        /// <param name="token"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Moves mouse (not click), to element position. when an element has an<br/>
        /// hover/mouseover effect, this would trigger it.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Drag an element to another element or target coordinates. dragging of elements should be supported  by the site of course.
        /// </summary>
        /// <param name="destElement">Another element where to drag to.</param>
        /// <param name="steps">move in &lt;steps&gt; points, this could make it look more "natural" (default 1),<br/>
        /// but also a lot slower.<br/>
        /// for very smooth action use 50-100</param>
        /// <param name="token"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Drag an element to another element or target coordinates. dragging of elements should be supported  by the site of course.
        /// </summary>
        /// <param name="destPoint">A tuple (x,y) of ints representing coordinate.</param>
        /// <param name="relative">When True, treats coordinate as relative. for example (-100, 200) will move left 100px and down 200px</param>
        /// <param name="steps">move in &lt;steps&gt; points, this could make it look more "natural" (default 1),<br/>
        /// but also a lot slower.<br/>
        /// for very smooth action use 50-100</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task MouseDragAsync((double X, double Y) destPoint, bool relative = false, int steps = 1, CancellationToken token = default)
        {
            var startPos = await GetPositionAsync(token: token);
            if (startPos?.Center == null)
            {
                Console.WriteLine($"Could not calculate box model for {this}");
                return;
            }
            await _tab.MouseDragAsync(startPos.Center, destPoint, relative, steps, token);
        }

        /// <summary>
        /// Clears an input field.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task ClearInputAsync(CancellationToken token = default)
        {
            await ApplyAsync(@"function (element) { element.value = """" } ", token: token);
        }

        /// <summary>
        /// Send text to an input field, or any other html element.<br/>
        /// <br/>
        /// hint, if you ever get stuck where using `ClickAsync`<br/>
        /// does not work, sending the keystroke \n or \r\n or a spacebar work wonders!
        /// </summary>
        /// <param name="text">Text to send.</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task SendKeysAsync(string text, CancellationToken token = default)
        {
            await ApplyAsync("(elem) => elem.focus()", token: token);
            foreach (var c in text)
                await _tab.SendAsync(Cdp.Input.DispatchKeyEvent("char", Text: c.ToString()), token: token);
        }

        /// <summary>
        /// For form (select) fields. when you have queried the options you can call this method on the option object.<br/>
        /// 02/08/2024: fixed the problem where events are not fired when programattically selecting an option.<br/>
        /// <br/>
        /// Calling `option.SelectOptionAsync()` will use that option as selected value.<br/>
        /// Does not work in all cases.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Some form input require a file (upload), a full path needs to be provided.<br/>
        /// This method sends 1 or more file(s) to the input field.<br/>
        /// <br/>
        /// Needles to say, but make sure the field accepts multiple files if you want to send more files.<br/>
        /// Otherwise the browser might crash.<br/>
        /// <br/>
        /// example:<br/>
        /// `await fileinputElement.SendFileAsync("c:/temp/image.png", "c:/users/myuser/lol.gif")`
        /// </summary>
        /// <param name="filePaths"></param>
        /// <returns></returns>
        public async Task SendFileAsync(params string[] filePaths)
        {
            await SendFileAsync(filePaths.ToList());
        }

        /// <summary>
        /// Some form input require a file (upload), a full path needs to be provided.<br/>
        /// This method sends 1 or more file(s) to the input field.<br/>
        /// <br/>
        /// Needles to say, but make sure the field accepts multiple files if you want to send more files.<br/>
        /// Otherwise the browser might crash.
        /// </summary>
        /// <param name="filePaths"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task SendFileAsync(List<string> filePaths, CancellationToken token = default)
        {
            await _tab.SendAsync(Cdp.DOM.SetFileInputFiles(
                Files: filePaths.ToList(),
                BackendNodeId: BackendNodeId,
                ObjectId: ObjectId), token: token);
        }

        public async Task SetValueAsync(string value, CancellationToken token = default)
        {
            await _tab.SendAsync(Cdp.DOM.SetNodeValue(NodeId: NodeId, Value: value), token: token);
        }

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

        /// <summary>
        /// Focus the current element. often useful in form (select) fields.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task FocusAsync(CancellationToken token = default)
        {
            await ApplyAsync("(element) => element.focus()", token: token);
        }

        /// <summary>
        /// Scrolls element into view.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task ScrollIntoViewAsync(CancellationToken token = default)
        {
            try
            {
                await Tab.SendAsync(Cdp.DOM.ScrollIntoViewIfNeeded(BackendNodeId: BackendNodeId), token: token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not scroll into view: {ex.Message}");
            }
        }

        /// <summary>
        /// Like js querySelectorAll()
        /// </summary>
        /// <param name="selector"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<IEnumerable<Element>> QuerySelectorAllAsync(string selector, CancellationToken token = default)
        {
            await UpdateAsync(token: token);
            return await _tab.QuerySelectorAllAsync(selector, this, token: token);
        }

        /// <summary>
        /// Like js querySelector()
        /// </summary>
        /// <param name="selector"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<Element?> QuerySelectorAsync(string selector, CancellationToken token = default)
        {
            await UpdateAsync(token: token);
            return await _tab.QuerySelectorAsync(selector, this, token: token);
        }

        private string GetScreenshotFormat(string format)
        {
            var f = format.ToLowerInvariant();
            if (f == "jpg" || f == "jpeg")
                return "jpeg";
            if (f == "png")
                return "png";
            return f;
        }

        private string GetScreenshotPath(string filename, string ext)
        {
            if (string.IsNullOrWhiteSpace(filename) || filename == "auto")
            {
                if (_tab.Target == null)
                    return "";

                var uri = new Uri(_tab.Target.Url);
                var lastPart = uri.AbsolutePath.Split('/').Last();
                var index = lastPart.LastIndexOf('?');
                if (index != -1)
                    lastPart = lastPart.Substring(0, index);
                var dtStr = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var candidate = $"{uri.Host}__{lastPart}_{dtStr}";

                return Path.Combine(AppContext.BaseDirectory, $"{candidate}{ext}");
            }
            return Path.Combine(AppContext.BaseDirectory, filename);
        }

        /// <summary>
        /// Saves a screenshot of this element (only)<br/>
        /// This is not the same as `Tab.SaveScreenshotAsync`, which saves a "regular" screenshot.<br/>
        /// <br/>
        /// When the element is hidden, or has no size, or is otherwise not capturable, a RuntimeError is raised.
        /// </summary>
        /// <param name="format">jpeg or png (defaults to jpeg)</param>
        /// <param name="viewport">The scale of the screenshot, eg: 1 = size as is, 2 = double, 0.5 is half.</param>
        /// <param name="token"></param>
        /// <returns>The path/filename of saved screenshot.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task<byte[]> GetScreenshotDataAsync(string format, Cdp.Page.Viewport viewport, CancellationToken token)
        {
            var result = await _tab.SendAsync(
                Cdp.Page.CaptureScreenshot(format, Clip: viewport, CaptureBeyondViewport: true), token: token);
            var base64Data = result.Data;
            if (string.IsNullOrWhiteSpace(base64Data))
                throw new InvalidOperationException("Could not take screenshot. most possible cause is the page has not finished loading yet.");
            return Convert.FromBase64String(base64Data);
        }

        public async Task<byte[]> CaptureScreenshotAsync(string format = "jpeg", double scale = 1, CancellationToken token = default)
        {
            var pos = await GetPositionAsync(token: token);
            if (pos == null)
                throw new InvalidOperationException("Could not determine position of element. Probably because it's not in view, or hidden.");

            var viewport = pos.ToViewport(scale);
            await _tab.WaitAsync(1, token: token);

            format = GetScreenshotFormat(format);
            return await GetScreenshotDataAsync(format, viewport, token);
        }

        /// <summary>
        /// Saves a screenshot of this element (only)<br/>
        /// This is not the same as `Tab.SaveScreenshotAsync`, which saves a "regular" screenshot.<br/>
        /// <br/>
        /// When the element is hidden, or has no size, or is otherwise not capturable, a RuntimeError is raised.
        /// </summary>
        /// <param name="filename">Uses this as the save path.</param>
        /// <param name="format">jpeg or png (defaults to jpeg)</param>
        /// <param name="scale">The scale of the screenshot, eg: 1 = size as is, 2 = double, 0.5 is half.</param>
        /// <param name="token"></param>
        /// <returns>The path/filename of saved screenshot.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<string> SaveScreenshotAsync(string filename = "auto", string format = "jpeg", double scale = 1, CancellationToken token = default)
        {
            var pos = await GetPositionAsync(token: token);
            if (pos == null)
                throw new InvalidOperationException("Could not determine position of element. Probably because it's not in view, or hidden.");

            var viewport = pos.ToViewport(scale);
            await _tab.WaitAsync(1, token: token);

            format = GetScreenshotFormat(format);
            var ext = format switch
            {
                "jpeg" => ".jpg",
                "png" => ".png",
                _ => ""
            };

            var path = GetScreenshotPath(filename, ext);
            if (string.IsNullOrWhiteSpace(path))
                throw new Exception($"Invalid filename or path: '{filename}'");

            var parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parentDir))
                if (!Directory.Exists(parentDir))
                    Directory.CreateDirectory(parentDir);

            var bytes = await GetScreenshotDataAsync(format, viewport, token);
            await File.WriteAllBytesAsync(path, bytes, token);
            return path;
        }

        /// <summary>
        /// Displays for a short time a red dot on the element (only if the element itself is visible)
        /// </summary>
        /// <param name="duration"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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
                Console.WriteLine("FlashAsync(): could not determine position.");
                return;
            }

            var id = Guid.NewGuid().ToString("N").Substring(0, 16);
            var style =
                $$"""
                    position:absolute;z-index:99999999;padding:0;margin:0;
                    left:{{pos.Center.X - 8}}px; top: {{pos.Center.Y - 8}}px;
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
                                  75% { transform: scale(2,2) }
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

        /// <summary>
        /// Highlights the element devtools-style. To remove the highlight,<br/>
        /// call the method again.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Experimental option.<br/>
        /// <br/>
        /// On html5 video nodes, you can call this method to start recording of the video.<br/>
        /// <br/>
        /// When any of the follow happens:<br/>
        /// <br/>
        /// - video ends<br/>
        /// - calling videoelement('pause')<br/>
        /// - video stops<br/>
        /// <br/>
        /// The video recorded will be downloaded.
        /// </summary>
        /// <param name="filename">The desired filename.</param>
        /// <param name="folder">The download folder path.</param>
        /// <param name="duration">Record for this many seconds and then download.</param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task RecordVideoAsync(string? filename = null, string? folder = null, double? duration = null, CancellationToken token = default)
        {
            if (NodeName != "VIDEO")
                throw new InvalidOperationException("RecordVideoAsync can only be called on html5 video elements.");

            var directoryPath = folder;
            if (string.IsNullOrWhiteSpace(directoryPath))
                directoryPath = Path.Combine(AppContext.BaseDirectory, "downloads");
            if (!Directory.Exists(directoryPath))
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

        public async Task<bool> IsRecordingAsync(CancellationToken token = default)
        {
            var (remoteObj, exception) = await ApplyAsync(@"(vid) => vid[""_recording""]", token: token);
            return remoteObj?.Value?.GetValue<bool>() ?? false;
        }

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
