using NoDriver.Core.Tools;
using Silk.NET.Maths;
using Silk.NET.SDL;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace NoDriver.Core.Runtime
{
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
        public JsonElement? Info { get; private set; } = null;

        public string WebSocketUrl
        {
            get
            {
                if (Info?.TryGetProperty("webSocketDebuggerUrl", out var prop) == true)
                {
                    var value = prop.GetString();
                    if (value != null)
                        return value;
                }
                return "";
            }
        }

        //ok
        public IReadOnlyList<Tab> Targets => _targets.ToList();

        //ok
        public Tab? MainTab => _targets.FirstOrDefault(it => it.Target?.Type == "page");

        //ok
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

        //ok
        public bool Stopped => _process == null || _process.HasExited;

        private Browser() 
        {
        }

        //ok
        public static async Task<Browser> CreateAsync(Config? config = null, CancellationToken token = default)
        {
            var browser = new Browser();
            browser.Config = config ?? new();
            return await browser.StartAsync(token);
        }

        //ok
        public async Task WaitAsync(double time = 0.1, CancellationToken token = default)
        {
            await Task.WhenAll(
                UpdateTargetsAsync(token),
                Task.Delay(TimeSpan.FromSeconds(time), token));
        }

        //ok 要測試
        private void HandleTargetUpdate(IEvent @event)
        {
            if (@event is Cdp.Target.TargetInfoChanged infoChanged)
            {
                var targetInfo = infoChanged.TargetInfo;
                var currentTab = _targets.FirstOrDefault(it => it.Target?.TargetId == targetInfo.TargetId);
                if (currentTab != null)
                {
                    //if (logger.IsEnabled(LogLevel.Debug))
                    {
                        var sb = new StringBuilder();
                        var changes = Util.CompareTargetInfo(currentTab.Target, targetInfo);
                        foreach (var change in changes)
                        {
                            sb.Append($"\n{change.Key}: {change.Old} => {change.New}\n");
                        }
                        Console.WriteLine($"Target #{_targets.IndexOf(currentTab)} has changed: {sb.ToString()}");
                    }
                    currentTab.Target = targetInfo;
                }
            }
            else if (@event is Cdp.Target.TargetCreated created)
            {
                var targetInfo = created.TargetInfo;
                if (Config?.Host != null && Config?.Port != null)
                {
                    var newTarget = new Tab(
                        $"ws://{Config.Host}:{Config.Port}/devtools/{targetInfo.Type ?? "page"}/{targetInfo.TargetId}",
                        targetInfo, this);
                    _targets.Add(newTarget);
                    Console.WriteLine($"Target #{_targets.Count - 1} created => {newTarget.ToString()}");
                }
            }
            else if (@event is Cdp.Target.TargetDestroyed destroyed)
            {
                var currentTab = _targets.FirstOrDefault(it => it.Target?.TargetId == destroyed.TargetId);
                if (currentTab != null)
                {
                    Console.WriteLine($"Target removed. id #{_targets.IndexOf(currentTab)} => {currentTab.ToString()}");
                    if (_targets.Remove(currentTab))
                        _ = currentTab.DisposeAsync();
                }
            }
            _ = UpdateTargetsAsync();
        }

        //ok
        public async Task<Tab> GetAsync(string url = "chrome://welcome", bool newTab = false, bool newWindow = false, CancellationToken token = default)
        {
            if (Connection == null)
                throw new InvalidOperationException("Connection cannot be null.");

            if (newTab || newWindow)
            {
                var result = await Connection.SendAsync(
                    Cdp.Target.CreateTarget(url, NewWindow: newWindow, EnableBeginFrameControl: true), token: token);
                var targetId = result.TargetId;

                var connection = _targets.FirstOrDefault(it => it.Target?.Type == "page" && it.Target?.TargetId == targetId);
                if (connection == null)
                    throw new InvalidOperationException("Targets connection cannot be null.");

                connection.Browser = this;
                await UpdateTargetsAsync(token);
                await WaitAsync(0, token);
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
                await WaitAsync(0, token);
                return connection;
            }
        }

        //ok 要測試
        public async Task<Tab> CreateContextAsync(
            string url = "chrome://welcome",
            bool newTab = false,
            bool newWindow = true,
            bool disposeOnDetach = true,
            string? proxyServer = null,
            List<string>? proxyBypassList = null,
            List<string>? originsWithUniversalNetworkAccess = null,
            X509Certificate2Collection? clientCertificates = null, 
            CancellationToken token = default)
        {
            if (Connection == null)
                throw new InvalidOperationException("Connection cannot be null.");

            if (!string.IsNullOrWhiteSpace(proxyServer))
            {
                var forwarder = new ProxyForwarder(proxyServer, clientCertificates);
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

        //ok
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

            var args = Config.GetArgs()
                .Select(it => it.Trim())
                .Aggregate("", (r, it) => r + " " +
                    (it.Contains(" ") ? $"\"{it}\"" : it));
            Console.WriteLine($"starting\n\texecutable: {exePath}\n\narguments: \n{string.Join("\n\t", args)}");

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
                    var data = await _http.GetAsync("version", token);
                    Info = data;
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

                Connection.AddHandler<Cdp.Target.TargetInfoChanged>((e, tab) => HandleTargetUpdate(e));
                Connection.AddHandler<Cdp.Target.TargetCreated>((e, tab) => HandleTargetUpdate(e));
                Connection.AddHandler<Cdp.Target.TargetDestroyed>((e, tab) => HandleTargetUpdate(e));
                Connection.AddHandler<Cdp.Target.TargetCrashed>((e, tab) => HandleTargetUpdate(e));

                await Connection.SendAsync(Cdp.Target.SetDiscoverTargets(true), token: token);
            }
            await UpdateTargetsAsync(token);
            return this;
        }

        //ok 要測試
        public async Task GrantAllPermissionsAsync(CancellationToken token = default)
        {
            var permissions = Cdp.Browser.PermissionType.GetEnums<Cdp.Browser.PermissionType>();
            //permissions.Remove(Cdp.Browser.PermissionType.FLASH);
            permissions.Remove(Cdp.Browser.PermissionType.CAPTURED_SURFACE_CONTROL);
            if (Connection != null)
                await Connection.SendAsync(Cdp.Browser.GrantPermissions(permissions), token: token);
        }

        //ok
        public async Task<List<(int left, int top, int width, int height)>?> TileWindowsAsync(List<Tab>? windows = null, int maxColumns = 0, CancellationToken token = default)
        {
            var sdl = Sdl.GetApi();
            if (sdl.Init(Sdl.InitVideo) < 0)
            {
                Console.WriteLine("SDL init failed.");
                return null;
            }

            Rectangle<int> rect = default;
            if (sdl.GetDisplayBounds(0, ref rect) < 0)
            {
                Console.WriteLine("No monitors detected.");
                return null;
            }

            var screenWidth = rect.Size.X;
            var screenHeight = rect.Size.Y;

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

        //ok
        public async Task UpdateTargetsAsync(CancellationToken token = default)
        {
            if (Connection != null)
            {
                var result = await Connection.SendAsync(Cdp.Target.GetTargets(), token: token);

                foreach (var target in result.TargetInfos)
                {
                    var existingTab = _targets.FirstOrDefault(it => it.Target?.TargetId == target.TargetId);
                    if (existingTab != null)
                    {
                        if (existingTab.Target != target)
                            existingTab.Target = target;
                    }
                    else
                    {
                        if (Config?.Host != null && Config?.Port != null)
                        {
                            _targets.AddIfNotExist(
                                it => it.Target?.TargetId == target.TargetId,
                                () => new Tab(
                                    $"ws://{Config.Host}:{Config.Port}/devtools/page/{target.TargetId}", target, this));
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
                    try
                    {
                        await target.DisposeAsync();
                    }
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
                    try
                    {
                        await forwarder.DisposeAsync();
                    }
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
                        try
                        {
                            target.Dispose();
                        }
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
                        try
                        {
                            forwarder.Dispose();
                        }
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
            }
        }

        public Tab this[int index] => Tabs[index];

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
