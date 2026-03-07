using Silk.NET.Maths;
using Silk.NET.SDL;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace NoDriver.Core.Runtime
{
    public class Browser : IDisposable, IAsyncDisposable
    {
        private Process? _process = null;
        private int? _processPid = null;
        private HTTPApi? _http = null;
        private CookieJar? _cookies = null;

        public Config? Config { get; private set; } = null;
        public Connection? Connection { get; private set; } = null;
        public List<Tab> Targets { get; private set; } = new();
        public JsonElement? Info { get; private set; } = null;

        public string WebSocketUrl => Info?.GetProperty("webSocketDebuggerUrl").GetString();

        //ok
        public Tab? MainTab => Targets.Where(it => it.Target?.Type == "page").FirstOrDefault();

        //ok
        public List<Tab> Tabs => Targets.Where(item => item.Target?.Type == "page").ToList();

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
        public static async Task<Browser> CreateAsync(Config? config = null)
        {
            var browser = new Browser();
            browser.Config = config ?? new();
            return await browser.StartAsync();
        }

        //ok
        public async Task WaitAsync(double time = 0.1, CancellationToken token = default)
        {
            await Task.WhenAll(
                UpdateTargetsAsync(token),
                Task.Delay(TimeSpan.FromSeconds(time), token));
        }

        private void HandleTargetUpdate(IEvent @event)
        {
            if (@event is Cdp.Target.TargetInfoChanged infoChanged)
            {
                var targetInfo = infoChanged.TargetInfo;
                var currentTab = Targets.FirstOrDefault(it => it.Target?.TargetId == targetInfo.TargetId);
                if (currentTab != null)
                    currentTab.Target = targetInfo;

                //if logger.getEffectiveLevel() <= 10:
                //    changes = util.compare_target_info(current_target, target_info)
                //    changes_string = ""
                //    for change in changes:
                //        key, old, new = change
                //        changes_string += f"\n{key}: {old} => {new}\n"
                //    logger.debug(
                //        "target #%d has changed: %s"
                //        % (self.targets.index(current_tab), changes_string)
                //    )

                //    current_tab._target = target_info
            }
            else if (@event is Cdp.Target.TargetCreated created)
            {
                var targetInfo = created.TargetInfo;
                if (Config?.Host != null && Config?.Port != null)
                {
                    var newTarget = new Tab(
                        $"ws://{Config.Host}:{Config.Port}/devtools/{targetInfo.Type ?? "page"}/{targetInfo.TargetId}",
                        targetInfo, this);
                    Targets.Add(newTarget);
                    Console.WriteLine($"Target #{Targets.Count - 1} created => {newTarget.ToString()}");
                }
            }
            else if (@event is Cdp.Target.TargetDestroyed destroyed)
            {
                var currentTab = Targets.FirstOrDefault(it => it.Target?.TargetId == destroyed.TargetId);
                if (currentTab != null)
                {
                    Console.WriteLine($"Target removed. id #{Targets.IndexOf(currentTab)} => {currentTab.ToString()}");
                    Targets.Remove(currentTab);
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

                var connection = Targets.FirstOrDefault(it => it.Target?.Type == "page" && it.Target?.TargetId == targetId);
                if (connection == null)
                    throw new InvalidOperationException("Targets connection cannot be null.");

                connection.Browser = this;
                await UpdateTargetsAsync(token);
                await WaitAsync(0, token);
                return connection;
            }
            else
            {
                var connection = Targets.FirstOrDefault(it => it.Target?.Type == "page");
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

        public async Task<Tab> CreateContextAsync(
            string url = "chrome://welcome",
            bool newTab = false,
            bool newWindow = true,
            bool disposeOnDetach = true,
            string? proxyServer = null,
            List<string>? proxyBypassList = null,
            List<string>? originsWithUniversalNetworkAccess = null,
            dynamic? proxySslContext= null)
        {
            if (!string.IsNullOrWhiteSpace(proxyServer))
            {
                var fw = new Util.ProxyForwarder(proxyServer, proxySslContext);
                proxyServer = fw.ProxyServer;
            }

            var ctx = await Connection.SendAsync(Cdp.Target.CreateBrowserContext(
                DisposeOnDetach: disposeOnDetach,
                ProxyServer: proxyServer,
                ProxyBypassList: proxyBypassList,
                OriginsWithUniversalNetworkAccess: originsWithUniversalNetworkAccess
            ));

            var targetId = await Connection.SendAsync(
                Cdp.Target.CreateTarget(url, BrowserContextId: ctx, NewWindow: newWindow, ForTab: newTab));

            await WaitAsync(0.5);

            var connection = Targets.FirstOrDefault(it => it.Target?.Type == "page" && it.Target?.TargetIdId == targetId);
            return connection;
        }

        public async Task<Browser> StartAsync()
        {
            if (Config == null)
                throw new Exception("use 'await Browser.CreateAsync()' to create a new instance.");

            if (_process != null || _processPid != null)
            {
                if (_process?.HasExited == true)
                    return await CreateAsync(Config);
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
            //Util.GetRegisteredInstances().Add(this);

            await Task.Delay(250);
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    var data = await _http.GetAsync("version");
                    Info = data;
                    break;
                }
                catch
                {
                    if (i == 4)
                        Console.WriteLine("Could not start or connect to browser.");
                    await Task.Delay(500);
                }
            }

            if (Info == null)
                throw new Exception("Failed to connect to browser. If running as root in Linux, you may need Sandbox=false.");

            Connection = new Connection(Info.webSocketDebuggerUrl, browser: this);

            if (Config.AutodiscoverTargets)
            {
                Console.WriteLine("Enabling autodiscover targets.");

                Connection.AddHandler<Cdp.Target.TargetInfoChanged>(HandleTargetUpdate);
                Connection.AddHandler<Cdp.Target.TargetCreated>(HandleTargetUpdate);
                Connection.AddHandler<Cdp.Target.TargetDestroyed>(HandleTargetUpdate);
                Connection.AddHandler<Cdp.Target.TargetCrashed>(HandleTargetUpdate);

                await Connection.SendAsync(Cdp.Target.SetDiscoverTargets(true));
            }
            await UpdateTargetsAsync();
            //await self
        }

        public async Task GrantAllPermissionsAsync()
        {
            var permissions = Enum.GetValues(typeof(PermissionType)).Cast<PermissionType>().ToList();
            permissions.Remove(PermissionType.Flash);
            permissions.Remove(PermissionType.CapturedSurfaceControl);
            await Connection.SendAsync(Cdp.Browser.GrantPermissions(permissions));
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

                var targets = result.TargetInfos;
                foreach (var target in targets)
                {
                    var existingTab = Targets.FirstOrDefault(it => it.Target?.TargetId == target.TargetId);
                    if (existingTab != null)
                    {
                        if (existingTab.Target != target)
                            existingTab.Target = target;
                    }
                    else
                    {
                        if (Config?.Host != null && Config?.Port != null)
                        {
                            Targets.Add(new Tab(
                                $"ws://{Config.Host}:{Config.Port}/devtools/page/{target.TargetId}",
                                target, this));
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
