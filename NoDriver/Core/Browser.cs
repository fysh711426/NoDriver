using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NoDriver.Core
{
    public class Browser
    {
        private Browser() { }

        private Config? _config = null;
        private Process? _process = null;
        private int? _processPid = null;
        private HTTPApi? _http = null;
        private Connection? _connection = null;
        private ContraDict? _info = null;

        //private List<string> _targets = new();
        //
        //private Target? _target = null;
        //private bool _keepUserDataDir = true;
        //private Connection? _connection = null;

        public static async Task<Browser> CreateAsync(Config? config = null)
        {
            var browser = new Browser();
            browser._config = config ?? new();
            return await browser.StartAsync();
        }

        public async Task<Browser> StartAsync()
        {
            if (_config == null)
                throw new Exception("use 'await Browser.CreateAsync()' to create a new instance.");

            if (_process != null || _processPid != null)
            {
                if (_process?.HasExited == true)
                    return await CreateAsync(_config);
                Console.WriteLine("Ignored! This call has no effect when already running.");
                return this;
            }

            var connectExisting = _config.Host != null && _config.Port != null;
            if (_config.Host == null || _config.Port == null)
            {
                _config.Host = "127.0.0.1";
                _config.Port = findFreePort();
            }

            var exePath = _config.BrowserExecutablePath;
            if (!connectExisting)
            {
                Console.WriteLine($"BROWSER EXECUTABLE PATH: {exePath}");
                if (!File.Exists(exePath))
                    throw new FileNotFoundException("Could not determine browser executable.");
            }

            var args = _config.GetArgs()
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

            _http = new HTTPApi(_config.Host, _config.Port.Value);

            await Task.Delay(250);
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    var data = await _http.GetAsync("version");
                    _info = data;
                    break;
                }
                catch
                {
                    if (i == 4)
                        Console.WriteLine("Could not start or connect to browser.");
                    await Task.Delay(500);
                }
            }

            if (_info == null)
                throw new Exception("Failed to connect to browser. If running as root in Linux, you may need Sandbox=false.");

            _connection = new Connection(_info.webSocketDebuggerUrl, this);


            if (Config.AutodiscoverTargets)
            {
                // 註冊 CDP 事件處理
                Connection.RegisterHandler<Cdp.Target.TargetInfoChanged>(HandleTargetUpdate);
                Connection.RegisterHandler<Cdp.Target.TargetCreated>(HandleTargetUpdate);
                Connection.RegisterHandler<Cdp.Target.TargetDestroyed>(HandleTargetUpdate);

                await Connection.SendAsync(Cdp.Target.SetDiscoverTargets(true));
            }
            await UpdateTargetsAsync();
        }

        private static int findFreePort()
        {
            var socket = new Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                var localEP = new IPEndPoint(IPAddress.Any, 0);
                socket.Bind(localEP);
                var freeEP = (IPEndPoint?)socket.LocalEndPoint;
                if (freeEP == null)
                    throw new Exception("Not found free port.");
                return freeEP.Port;
            }
            finally
            {
                socket.Close();
            }
        }
    }

    internal class HTTPApi
    {
        private readonly string _apiBase;

        private static readonly HttpClient _httpClient = new();

        public HTTPApi(string host, int port)
        {
            _apiBase = $"http://{host}:{port}";
        }

        public async Task<JsonElement> GetAsync(string endpoint, CancellationToken token = default)
        {
            return await RequestAsync(endpoint, "GET", null, token);
        }

        public async Task<JsonElement> PostAsync(string endpoint, object? data = null, CancellationToken token = default)
        {
            return await RequestAsync(endpoint, "POST", data, token);
        }

        private async Task<JsonElement> RequestAsync(
            string endpoint, string method = "GET", object? data = null, CancellationToken token = default)
        {
            var url = $"{_apiBase}/json";
            if (!string.IsNullOrWhiteSpace(endpoint))
                url = $"{_apiBase}/json/{endpoint}";

            method = method.ToUpper();
            if (data != null && method == "GET")
                throw new ArgumentException("GET requests cannot contain data.");

            using (var request = new HttpRequestMessage(new HttpMethod(method), url))
            {
                if (data != null)
                {
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
                }

                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                    {
                        try
                        {
                            using (var response = await _httpClient.SendAsync(request, cts.Token))
                            {
                                response.EnsureSuccessStatusCode();
                                using (var stream = await response.Content.ReadAsStreamAsync(cts.Token))
                                {
                                    return JsonSerializer.Deserialize<JsonElement>(stream);
                                }
                            }
                        }
                        catch(OperationCanceledException ex)
                        {
                            if (timeoutCts.IsCancellationRequested)
                                throw new TimeoutException("The request timed out after 30 seconds.", ex);
                            throw;
                        }
                    }
                }
            }
        }
    }
}
