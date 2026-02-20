using System.Globalization;
using System.IO.Compression;

namespace NoDriver.Core
{
    public class Config
    {
        private readonly List<string> _defaultBrowserArgs = new()
        {
            "--remote-allow-origins=*",
            "--no-first-run",
            "--no-service-autorun",
            "--no-default-browser-check",
            "--homepage=about:blank",
            "--no-pings",
            "--password-store=basic",
            "--disable-infobars",
            "--disable-breakpad",
            "--disable-dev-shm-usage",
            "--disable-session-crashed-bubble",
            "--disable-search-engine-choice-screen"
        };

        private string _userDataDir = "";
        private bool _customDataDir = true;
        private List<string> _browserArgs = new();
        private List<string> _extensions = new();

        public string UserDataDir
        {
            get => _userDataDir;
            set
            {
                _userDataDir = value;
                _customDataDir = true;
            }
        }
        public string Lang { get; set; } = "";
        public string BrowserExecutablePath { get; set; } = "";
        public string? Host { get; set; } = null;
        public int? Port { get; set; } = null;
        public bool Headless { get; set; } = false;
        public bool Sandbox { get; set; } = true;
        public bool Expert { get; set; } = false;
        public bool AutodiscoverTargets { get; set; } = true;
        public Dictionary<string, object> Attributes { get; set; } = new();

        public Config()
        {
            //----- UserDataDir -----
            if (string.IsNullOrWhiteSpace(UserDataDir))
            {
                _customDataDir = false;
                _userDataDir = Path.Combine(
                    Path.GetTempPath(), $"uc_{Path.GetRandomFileName()}");
            }
            //----- UserDataDir -----

            //----- BrowserExecutablePath -----
            if (string.IsNullOrWhiteSpace(BrowserExecutablePath))
            {
                var executable = new ChromeExecutable();
                var browserExecutablePath = executable.GetExecutablePath();
                if (browserExecutablePath == null)
                    throw new Exception("Not found chrome.exe.");
                BrowserExecutablePath = browserExecutablePath;
            }
            //----- BrowserExecutablePath -----

            //----- Language -----
            if (string.IsNullOrWhiteSpace(Lang))
            {
                Lang = CultureInfo.CurrentCulture.Name;
            }
            //----- Language -----

            //----- Sandbox -----
            if (PlatformHelper.IsPosix() && PlatformHelper.IsRoot() && Sandbox)
            {
                //Console.WriteLine("Detected root usage, auto disabling sandbox mode.");
                Sandbox = false;
            }
            //----- Sandbox -----
        }

        public void AddArgument(string arg)
        {
            var lowerArg = arg.ToLower();
            var forbidden = new[]
            {
                "headless",
                "data-dir",
                "data_dir",
                "no-sandbox",
                "no_sandbox",
                "lang"
            };
            if (forbidden.Any(it => lowerArg.Contains(it)))
                throw new ArgumentException($"'{arg}' not allowed. Please use the properties of the Config object to set it.");
            _browserArgs.Add(arg);
        }

        public void AddExtension(string extensionPath)
        {
            if (!File.Exists(extensionPath) && !Directory.Exists(extensionPath))
                throw new FileNotFoundException($"Could not find anything here: {extensionPath}");

            if (File.Exists(extensionPath))
            {
                var tempDir = Path.Combine(
                    Path.GetTempPath(), $"extension_{Guid.NewGuid().ToString("n").Substring(0, 8)}");
                ZipFile.ExtractToDirectory(extensionPath, tempDir);
                _extensions.Add(tempDir);
                return;
            }
            if (Directory.Exists(extensionPath))
            {
                var manifest = Directory.GetFiles(
                    extensionPath, "manifest.*", SearchOption.AllDirectories).FirstOrDefault();
                if (manifest != null)
                {
                    var dirPath = Path.GetDirectoryName(manifest);
                    if (!string.IsNullOrWhiteSpace(dirPath))
                        _extensions.Add(dirPath);
                }
                return;
            }
        }

        public List<string> GetArgs()
        {
            var args = new List<string>(_defaultBrowserArgs);

            args.Add($"--user-data-dir={UserDataDir}");
            args.Add("--disable-session-crashed-bubble");

            var disabledFeatures = "IsolateOrigins,site-per-process";
            if (_extensions.Any())
                disabledFeatures += ",DisableLoadExtensionCommandLineSwitch";
            args.Add($"--disable-features={disabledFeatures}");

            if (_extensions.Any())
                args.Add("--enable-unsafe-extension-debugging");

            if (Expert)
                args.Add("--disable-site-isolation-trials");

            foreach (var arg in _browserArgs)
            {
                if (!args.Contains(arg, StringComparer.OrdinalIgnoreCase))
                    args.Add(arg);
            }

            if (Headless)
                args.Add("--headless=new");

            if (!Sandbox)
                args.Add("--no-sandbox");

            if (!string.IsNullOrWhiteSpace(Host))
                args.Add($"--remote-debugging-host={Host}");

            if (Port > 0)
                args.Add($"--remote-debugging-port={Port}");

            return args;
        }
    }
}
