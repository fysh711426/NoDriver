using Microsoft.Extensions.Logging;
using NoDriver.Core.Tools;
using System.Globalization;
using System.IO.Compression;

namespace NoDriver.Core.Runtime
{
    /// <summary>
    /// Creates a config object.<br/>
    /// Can be called without any arguments to generate a best-practice config, which is recommended.<br/>
    /// <br/>
    /// Calling the method GetArgs(), will return the list of arguments which<br/>
    /// are provided to the browser.<br/>
    /// <br/>
    /// Additional arguments can be added using the AddArgument() method.<br/>
    /// <br/>
    /// Instances of this class are usually not instantiated by end users.
    /// </summary>
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
        private readonly List<string> _browserArgs = new();
        private readonly List<string> _extensions = new();
        private readonly List<string> _tempExtensionDirs = new();

        private string _userDataDir = "";
        private bool _customDataDir = true;

        public ILogger? Logger { get; set; } = null;

        /// <summary>
        /// The data directory to use.
        /// </summary>
        public string UserDataDir
        {
            get => _userDataDir;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _userDataDir = value;
                    _customDataDir = true;
                }
            }
        }
        public bool CustomDataDir => _customDataDir;
        /// <summary>
        /// Language string to use other than the default "CultureInfo.CurrentCulture".
        /// </summary>
        public string Lang { get; set; } = "";
        /// <summary>
        /// Specify browser executable, instead of using autodetect.
        /// </summary>
        public string BrowserExecutablePath { get; set; } = "";
        public string? Host { get; set; } = null;
        public int? Port { get; set; } = null;
        /// <summary>
        /// Set to True for headless mode.
        /// </summary>
        public bool Headless { get; set; } = false;
        /// <summary>
        /// Disables sandbox.
        /// </summary>
        public bool Sandbox { get; set; } = true;
        /// <summary>
        /// When set to true, enabled "expert" mode.<br/>
        /// This conveys, the inclusion of parameters:  ----disable-site-isolation-trials,<br/>
        /// as well as some scripts and patching useful for debugging (for example, ensuring shadow-root is always in "open" mode)
        /// </summary>
        public bool Expert { get; set; } = false;
        /// <summary>
        /// Use autodiscovery of targets.
        /// </summary>
        public bool AutodiscoverTargets { get; set; } = true;
        public Dictionary<string, object> Attributes { get; } = new();
        public IReadOnlyList<string> TempExtensionDirs => _tempExtensionDirs.AsReadOnly();

        public Config()
        {
            //----- UserDataDir -----
            if (string.IsNullOrWhiteSpace(UserDataDir))
            {
                _customDataDir = false;
                //_userDataDir = Path.Combine(
                //    Path.GetTempPath(), $"uc_{Path.GetRandomFileName()}");
                _userDataDir = Path.Combine(
                    Path.GetTempPath(), $"uc_{Guid.NewGuid().ToString("n").Substring(0, 8)}");
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
                Lang = CultureInfo.CurrentCulture.Name;
            //----- Language -----

            //----- Sandbox -----
            if (PlatformHelper.IsPosix() && PlatformHelper.IsRoot() && Sandbox)
            {
                Logger?.LogInformation("Detected root usage, auto disabling sandbox mode.");
                Sandbox = false;
            }
            //----- Sandbox -----
        }

        /// <summary>
        /// Forwarded to browser executable. eg: ["--some-chromeparam=somevalue", "some-other-param=someval"]
        /// </summary>
        /// <param name="arg"></param>
        /// <exception cref="ArgumentException"></exception>
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

        /// <summary>
        /// Adds an extension to load, you could point extensionPath
        /// to a folder (containing the manifest), or extension file (crx)
        /// </summary>
        /// <param name="extensionPath"></param>
        /// <exception cref="FileNotFoundException"></exception>
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
                _tempExtensionDirs.Add(tempDir);
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

        /// <summary>
        /// Get the list of arguments which are provided to the browser.
        /// </summary>
        /// <returns></returns>
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

            if (_extensions.Any())
                args.Add($"--load-extension={string.Join(",", _extensions)}");

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
