using NoDriver.Core.Tools;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NoDriver.Core.Runtime
{
    /// <summary>
    /// The Browser object is the "root" of the hierarchy and contains a reference
    /// to the browser parent process.<br/>
    /// there should usually be only 1 instance of this.<br/>
    /// <br/>
    /// All opened tabs, extra browser screens and resources will not cause a new Browser process,<br/>
    /// but rather create additional :class:`Tab` objects.<br/>
    /// <br/>
    /// So, besides starting your instance and first/additional tabs, you don't actively use it a lot under normal conditions.<br/>
    /// <br/>
    /// Tab objects will represent and control<br/>
    ///  - tabs (as you know them)<br/>
    ///  - browser windows (new window)<br/>
    ///  - iframe<br/>
    ///  - background processes<br/>
    /// <br/>
    /// note:<br/>
    /// the Browser object is not instantiated by constructor but using the asynchronous :meth:`Browser.Create` method.<br/>
    /// <br/>
    /// note:<br/>
    /// in Chromium based browsers, there is a parent process which keeps running all the time, even if<br/>
    /// there are no visible browser windows. sometimes it's stubborn to close it, so make sure after using<br/>
    /// this library, the browser is correctly and fully closed/exited/killed.
    /// </summary>
    public class Browser : IDisposable, IAsyncDisposable
    {
        private readonly ConcurrentList<Tab> _targets = new();
        private readonly ConcurrentList<ProxyForwarder> _proxyForwarders = new();

        private Process? _process = null;
        private int? _processPid = null;
        private HTTPApi? _http = null;
        private CookieJar? _cookies = null;

        public Config? Config { get; private set; } = null;
        public Connection? Connection { get; private set; } = null;
        public JsonNode? Info { get; private set; } = null;

        public string WebSocketUrl
        {
            get
            {
                var url = Info?["webSocketDebuggerUrl"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(url))
                    return url;
                return "";
            }
        }

        public IReadOnlyList<Tab> Targets => _targets.ToList();

        /// <summary>
        /// Returns the target which was launched with the browser.
        /// </summary>
        public Tab? MainTab => _targets.FirstOrDefault(it => it.Target?.Type == "page");

        /// <summary>
        /// Returns the current targets which are of type "page".
        /// </summary>
        public List<Tab> Tabs => _targets.Where(it => it.Target?.Type == "page");

        public CookieJar Cookies
        {
            get
            {
                if (_cookies == null)
                    _cookies = new(this);
                return _cookies;
            }
        }

        public bool Stopped => _process == null || _process.HasExited;

        private Browser()
        {
        }

        /// <summary>
        /// Entry point for creating an instance.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public static async Task<Browser> CreateAsync(Config? config = null, CancellationToken token = default)
        {
            var browser = new Browser();
            browser.Config = config ?? new();
            return await browser.StartAsync(token);
        }

        /// <summary>
        /// Wait for time seconds. important to use, especially in between page navigation.
        /// </summary>
        /// <param name="time"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task WaitAsync(double time = 0.1, CancellationToken token = default)
        {
            await Task.WhenAll(
                UpdateTargetsAsync(token),
                Task.Delay(TimeSpan.FromSeconds(time), token));
        }

        /// <summary>
        /// This is an internal handler which updates the targets when chrome emits the corresponding event.
        /// </summary>
        /// <param name="infoChanged"></param>
        /// <returns></returns>
        private async Task HandleTargetInfoChanged(Cdp.Target.TargetInfoChanged infoChanged)
        {
            var targetInfo = infoChanged.TargetInfo;
            var target = _targets.FirstOrDefault(it => it.Target?.TargetId == targetInfo.TargetId);
            if (target != null)
            {
                //if (logger.IsEnabled(LogLevel.Debug))
                //{
                //    var sb = new StringBuilder();
                //    var changes = Util.CompareTargetInfo(target.Target, targetInfo);
                //    foreach (var change in changes)
                //    {
                //        sb.Append($"\n{change.Key}: {change.Old} => {change.New}\n");
                //    }
                //    Console.WriteLine($"Target #{_targets.IndexOf(target)} has changed: {sb.ToString()}");
                //}
                target.Target = targetInfo;
            }
            await UpdateTargetsAsync();
        }

        /// <summary>
        /// This is an internal handler which updates the targets when chrome emits the corresponding event.
        /// </summary>
        /// <param name="created"></param>
        /// <returns></returns>
        private async Task HandleTargetCreated(Cdp.Target.TargetCreated created)
        {
            var targetInfo = created.TargetInfo;
            if (Config?.Host != null && Config?.Port != null)
            {
                var newTarget = new Tab(
                    $"ws://{Config.Host}:{Config.Port}/devtools/{targetInfo.Type ?? "page"}/{targetInfo.TargetId.Value}", targetInfo, this);
                _targets.AddIfNotExist(
                    it => it.Target?.TargetId == targetInfo.TargetId,
                    () => newTarget);
                Console.WriteLine($"Target #{_targets.Count - 1} created => {newTarget.ToString()}");
            }
            await UpdateTargetsAsync();
        }

        /// <summary>
        /// This is an internal handler which updates the targets when chrome emits the corresponding event.
        /// </summary>
        /// <param name="destroyed"></param>
        /// <returns></returns>
        private async Task HandleTargetDestroyed(Cdp.Target.TargetDestroyed destroyed)
        {
            var target = _targets.FirstOrDefault(it => it.Target?.TargetId == destroyed.TargetId);
            if (target != null)
            {
                Console.WriteLine($"Target removed. id #{_targets.IndexOf(target)} => {target.ToString()}");
                if (_targets.Remove(target))
                {
                    try { await target.DisposeAsync(); }
                    catch { }
                }
            }
            await UpdateTargetsAsync();
        }

        /// <summary>
        /// This is an internal handler which updates the targets when chrome emits the corresponding event.
        /// </summary>
        /// <param name="crashed"></param>
        /// <returns></returns>
        private async Task HandleTargetCrashed(Cdp.Target.TargetCrashed crashed)
        {
            await UpdateTargetsAsync();
        }

        /// <summary>
        /// Top level get. utilizes the first tab to retrieve given url.<br/>
        /// convenience function known from selenium.<br/>
        /// this function handles waits and detects when DOM events fired, so it's the safest
        /// way of navigating.
        /// </summary>
        /// <param name="url">The url to navigate to.</param>
        /// <param name="newTab">Open new tab.</param>
        /// <param name="newWindow">Open new window.</param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<Tab> GetAsync(string url = "chrome://welcome", bool newTab = false, bool newWindow = false, CancellationToken token = default)
        {
            if (Connection == null)
                throw new InvalidOperationException("Connection cannot be null.");

            if (newTab || newWindow)
            {
                var result = await Connection.SendAsync(
                    Cdp.Target.CreateTarget(url, NewWindow: newWindow, EnableBeginFrameControl: true), token: token);
                var targetId = result.TargetId;

                await UpdateTargetsAsync(token);

                var connection = _targets.FirstOrDefault(it => it.Target?.Type == "page" && it.Target?.TargetId == targetId);
                if (connection == null)
                    throw new InvalidOperationException("Targets connection cannot be null.");

                connection.Browser = this;
                return connection;
            }
            else
            {
                var connection = _targets.FirstOrDefault(it => it.Target?.Type == "page");
                if (connection == null)
                    throw new InvalidOperationException("Targets connection cannot be null.");

                var result = await connection.SendAsync(Cdp.Page.Navigate(url), token: token);
                //connection.FrameId = result.FrameId;
                connection.Browser = this;
                await UpdateTargetsAsync(token);
                return connection;
            }
        }

        //ok 要測試
        /// <summary>
        /// Creates a new browser context - mostly useful if you want to use proxies for different browser instances<br/>
        /// since chrome usually can only use 1 proxy per browser.<br/>
        /// socks5 with authentication is supported by using a forwarder proxy, the<br/>
        /// correct string to use socks proxy with username/password auth is socks://USERNAME:PASSWORD@SERVER:PORT<br/>
        /// http/https proxies with authentication are also supported: http://USERNAME:PASSWORD@SERVER:PORT
        /// </summary>
        /// <param name="url"></param>
        /// <param name="newTab"></param>
        /// <param name="newWindow"></param>
        /// <param name="disposeOnDetach">If specified, disposes this context when debugging session disconnects.</param>
        /// <param name="proxyServer">Proxy server, similar to the one passed to –proxy-server.</param>
        /// <param name="proxyBypassList">Proxy bypass list, similar to the one passed to –proxy-bypass-list.</param>
        /// <param name="originsWithUniversalNetworkAccess">An optional list of origins to grant unlimited cross-origin access to. Parts of the URL other than those constituting origin are ignored.</param>
        /// <param name="clientCertificates">Custom SSL context for HTTPS proxy connections. If None, a default context is used.</param>
        /// <param name="remoteCertificateValidationCallback"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<Tab> CreateContextAsync(
            string url = "chrome://welcome",
            bool newTab = false,
            bool newWindow = true,
            bool disposeOnDetach = true,
            string? proxyServer = null,
            List<string>? proxyBypassList = null,
            List<string>? originsWithUniversalNetworkAccess = null,
            X509Certificate2Collection? clientCertificates = null,
            RemoteCertificateValidationCallback? remoteCertificateValidationCallback = null,
            CancellationToken token = default)
        {
            if (Connection == null)
                throw new InvalidOperationException("Connection cannot be null.");

            if (!string.IsNullOrWhiteSpace(proxyServer))
            {
                var forwarder = new ProxyForwarder(proxyServer, 
                    clientCertificates, remoteCertificateValidationCallback);
                _proxyForwarders.Add(forwarder);
                proxyServer = forwarder.ProxyServer;
            }

            var ctxResult = await Connection.SendAsync(Cdp.Target.CreateBrowserContext(
                DisposeOnDetach: disposeOnDetach,
                ProxyServer: proxyServer,
                ProxyBypassList: proxyBypassList?.Count > 0 ?
                    string.Join(';', proxyBypassList) : null,
                OriginsWithUniversalNetworkAccess: originsWithUniversalNetworkAccess
            ), token: token);

            var result = await Connection.SendAsync(
                Cdp.Target.CreateTarget(url, BrowserContextId: ctxResult.BrowserContextId, NewWindow: newWindow, ForTab: newTab), token: token);

            await WaitAsync(0.5, token);

            var connection = _targets.FirstOrDefault(it => it.Target?.Type == "page" && it.Target?.TargetId == result.TargetId);
            if (connection == null)
                throw new InvalidOperationException("Targets connection cannot be null.");
            return connection;
        }

        /// <summary>
        /// Launches the actual browser.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        public async Task<Browser> StartAsync(CancellationToken token = default)
        {
            if (Config == null)
                throw new Exception("use 'await Browser.CreateAsync()' to create a new instance.");

            if (_process != null || _processPid != null)
            {
                if (_process?.HasExited == true)
                    return await CreateAsync(Config, token);
                Console.WriteLine("Ignored! This call has no effect when already running.");
                return this;
            }

            var connectExisting = Config.Host != null && Config.Port != null;
            if (Config.Host == null || Config.Port == null)
            {
                Config.Host = "127.0.0.1";
                Config.Port = Util.FreePort();
            }

            var exePath = Config.BrowserExecutablePath;
            if (!connectExisting)
            {
                Console.WriteLine($"BROWSER EXECUTABLE PATH: {exePath}");
                if (!File.Exists(exePath))
                    throw new FileNotFoundException("Could not determine browser executable.");
            }

            var args = Config.GetArgs();
            //var args = Config.GetArgs()
            //    .Select(it => it.Trim())
            //    .Aggregate("", (r, it) => r + " " +
            //        (it.Contains(" ") ? $"\"{it}\"" : it));

            Console.WriteLine($"starting\n\texecutable: {exePath}\n\narguments: \n\t{string.Join("\n\t", args)}");

            if (!connectExisting)
            {
                var info = new ProcessStartInfo(exePath, args)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                _process = Process.Start(info);
                if (_process == null)
                    throw new Exception("Failed to start process.");
                _processPid = _process.Id;
            }

            _http = new HTTPApi(Config.Host, Config.Port.Value);

            await Task.Delay(TimeSpan.FromSeconds(0.25), token);
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    Info = await _http.GetAsync("version", token);
                    break;
                }
                catch
                {
                    if (i == 4)
                        Console.WriteLine("Could not start or connect to browser.");
                    await Task.Delay(TimeSpan.FromSeconds(0.5), token);
                }
            }

            if (Info == null)
                throw new Exception("Failed to connect to browser. If running as root in Linux, you may need Sandbox=false.");

            Connection = new Connection(WebSocketUrl, browser: this);

            if (Config.AutodiscoverTargets)
            {
                Console.WriteLine("Enabling autodiscover targets.");

                Connection.AddHandler<Cdp.Target.TargetInfoChanged>((e, tab) => HandleTargetInfoChanged(e));
                Connection.AddHandler<Cdp.Target.TargetCreated>((e, tab) => HandleTargetCreated(e));
                Connection.AddHandler<Cdp.Target.TargetDestroyed>((e, tab) => HandleTargetDestroyed(e));
                Connection.AddHandler<Cdp.Target.TargetCrashed>((e, tab) => HandleTargetCrashed(e));

                await Connection.SendAsync(Cdp.Target.SetDiscoverTargets(true), token: token);
            }
            await UpdateTargetsAsync(token);
            return this;
        }

        public async Task GrantAllPermissionsAsync(CancellationToken token = default)
        {
            var permissions = Cdp.Browser.PermissionType.GetEnums<Cdp.Browser.PermissionType>().ToList();
            //permissions.Remove(Cdp.Browser.PermissionType.FLASH);
            permissions.Remove(Cdp.Browser.PermissionType.CAPTURED_SURFACE_CONTROL);
            if (Connection != null)
                await Connection.SendAsync(Cdp.Browser.GrantPermissions(permissions), token: token);
        }

        public async Task<List<(int Left, int Top, int Width, int Height)>?> TileWindowsAsync(List<Tab>? windows = null, int maxColumns = 0, CancellationToken token = default)
        {
            var resolution = await ScreenHelper.GetResolutionAsync(token);
            var screenWidth = resolution.Width;
            var screenHeight = resolution.Height;

            var distinctWindows = new Dictionary<int, List<Tab>>();

            var tabs = Tabs;
            if (windows != null && windows.Count > 0)
                tabs = windows;

            foreach (var tab in tabs)
            {
                var result = await tab.GetWindowAsync(token);
                if (result != null)
                {
                    var (windowId, _) = result.Value;
                    if (!distinctWindows.ContainsKey(windowId.Value))
                        distinctWindows[windowId.Value] = new();
                    distinctWindows[windowId.Value].Add(tab);
                }
            }

            var numWindows = distinctWindows.Count;
            if (numWindows == 0)
                return null;

            var reqCols = (int)(numWindows * (19.0 / 6.0));
            if (maxColumns > 0)
                reqCols = maxColumns;
            reqCols = Math.Max(1, reqCols);
            var reqRows = numWindows / reqCols;

            while (reqCols * reqRows < numWindows)
            {
                reqRows++;
            }

            var boxW = (int)Math.Floor((double)screenWidth / reqCols - 1);
            var boxH = (int)Math.Floor((double)screenHeight / reqRows);

            using (var distinctWindowsIter = distinctWindows.Values.GetEnumerator())
            {
                var grid = new List<(int left, int top, int width, int height)>();
                for (var x = 0; x < reqCols; x++)
                {
                    for (var y = 0; y < reqRows; y++)
                    {
                        if (!distinctWindowsIter.MoveNext())
                            break;

                        var _tabs = distinctWindowsIter.Current;
                        if (_tabs == null || _tabs.Count == 0)
                            continue;

                        var tab = _tabs[0];
                        try
                        {
                            var pos = (left: x * boxW, top: y * boxH, width: boxW, height: boxH);
                            grid.Add(pos);
                            await tab.SetWindowSizeAsync(pos.left, pos.top, pos.width, pos.height, token);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not set window size. exception => {ex.Message}");
                            continue;
                        }
                    }
                }
                return grid;
            }
        }
        
        //ok 要測試
        public async Task<(int Width, int Height)?> GetScreenResolutionAsync(CancellationToken token = default)
        {
            if (Connection != null)
            {
                var expression = "({ width: window.screen.availWidth, height: window.screen.availHeight })";
                var result = await Connection.SendAsync(Cdp.Runtime.Evaluate(expression, ReturnByValue: true));
                var data = result.Result.Value;

                var width = data?["width"]?.GetValue<int?>();
                var height = data?["height"]?.GetValue<int?>();

                if (width != null && height != null)
                    return (width.Value, height.Value);
            }
            return null;
        }

        public async Task UpdateTargetsAsync(CancellationToken token = default)
        {
            if (Connection != null)
            {
                var result = await Connection.SendAsync(Cdp.Target.GetTargets(), token: token);

                foreach (var targetInfo in result.TargetInfos)
                {
                    var target = _targets.FirstOrDefault(it => it.Target?.TargetId == targetInfo.TargetId);
                    if (target != null)
                    {
                        if (target.Target != targetInfo)
                            target.Target = targetInfo;
                    }
                    else
                    {
                        if (Config?.Host != null && Config?.Port != null)
                        {
                            _targets.AddIfNotExist(
                                it => it.Target?.TargetId == targetInfo.TargetId,
                                () => new Tab(
                                    $"ws://{Config.Host}:{Config.Port}/devtools/page/{targetInfo.TargetId.Value}", targetInfo, this));
                        }
                    }
                }
            }
            await Task.Yield();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Connection != null)
                    await Connection.DisposeAsync();
            }
            catch { }

            try
            {
                var targets = _targets.ToList();
                _targets.Clear();

                foreach (var target in targets)
                {
                    try { await target.DisposeAsync(); }
                    catch { }
                }
            }
            catch { }

            try
            {
                var proxyForwarders = _proxyForwarders.ToList();
                _proxyForwarders.Clear();

                foreach (var forwarder in proxyForwarders)
                {
                    try { await forwarder.DisposeAsync(); }
                    catch { }
                }
            }
            catch { }

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (Connection != null)
                        Connection.Dispose();
                }
                catch { }

                try
                {
                    var targets = _targets.ToList();
                    _targets.Clear();

                    foreach (var target in targets)
                    {
                        try { target.Dispose(); }
                        catch { }
                    }
                }
                catch { }

                try
                {
                    var proxyForwarders = _proxyForwarders.ToList();
                    _proxyForwarders.Clear();

                    foreach (var forwarder in proxyForwarders)
                    {
                        try { forwarder.Dispose(); }
                        catch { }
                    }
                }
                catch { }

                if (_process != null && !_process.HasExited)
                {
                    try
                    {
                        _process.Kill(true);
                        _process.Dispose();
                        Console.WriteLine($"Killed browser with pid {_process.Id} successfully.");
                    }
                    catch { }
                    _process = null;
                    _processPid = null;
                }

                //if (Config != null)
                //{
                //    var userDataDir = Config.UserDataDir;
                //    if (!Config.CustomDataDir)
                //    {
                //        for (var i = 0; i < 5; i++)
                //        {
                //            try
                //            {
                //                if (!string.IsNullOrWhiteSpace(userDataDir))
                //                {
                //                    if (Directory.Exists(userDataDir))
                //                        Directory.Delete(userDataDir, true);
                //                    Console.WriteLine($"Successfully removed temp data dir {userDataDir}");
                //                }
                //                break;
                //            }
                //            catch (Exception ex)
                //            {
                //                if (i == 4)
                //                    Console.WriteLine(
                //                        $"Problem removing temp data dir {userDataDir}\n" +
                //                        $"Consider checking whether it's there and remove it by hand\n" +
                //                        $"Error: {ex.Message}");
                //                System.Threading.Thread.Sleep(150);
                //            }
                //        }
                //    }
                //}
            }
        }

        /// <summary>
        /// Allows to get `Tab` instances by using browser[0], browser[1].
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public Tab this[int index] => Tabs[index];

        /// <summary>
        /// A string is also allowed. it will then return the first tab where the `Cdp.Target.TargetInfo` object<br/>
        /// (as json string) contains the given key, or the first tab in case no matches are found. eg:<br/>
        /// `browser["google"]` gives the first tab which has "google" in it's serialized target object.
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public Tab this[string query]
        {
            get
            {
                var lowerQuery = query.ToLowerInvariant();
                foreach (var t in Tabs)
                {
                    if (JsonSerializer.Serialize(t.Target).ToLowerInvariant().Contains(lowerQuery))
                        return t;
                }
                return Tabs[0];
            }
        }
    }
}
