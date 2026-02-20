using System.Runtime.InteropServices;

namespace NoDriver.Core
{
    internal class ChromeExecutable
    {
        public string? GetExecutablePath()
        {
            var result = null as string;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                result = findChromeExecutable();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                result = findChromeExecutableLinux();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                result = findChromeExecutableMacos();
            return result;
        }

        private static string? findChromeExecutable()
        {
            var candidates = new List<string>();

            foreach (var item in new[] {
                "PROGRAMFILES", "PROGRAMFILES(X86)", "LOCALAPPDATA", "PROGRAMW6432"
            })
            {
                foreach (var subitem in new[] {
                    @"Google\Chrome\Application",
                    @"Google\Chrome Beta\Application",
                    @"Google\Chrome Canary\Application"
                })
                {
                    var variable = Environment.GetEnvironmentVariable(item);
                    if (variable != null)
                        candidates.Add(Path.Combine(variable, subitem, "chrome.exe"));
                }
            }

            foreach (var candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;
            return null;
        }

        private static string? findChromeExecutableLinux()
        {
            var candidates = new List<string>();

            var environmentPATH = Environment.GetEnvironmentVariable("PATH");
            if (environmentPATH == null)
                throw new Exception("Not found environment PATH.");

            var variables = environmentPATH.Split(Path.PathSeparator);
            foreach (var item in variables)
            {
                foreach (var subitem in new[] {
                    "google-chrome",
                    "chromium",
                    "chromium-browser",
                    "chrome",
                    "google-chrome-stable",
                })
                {
                    candidates.Add(Path.Combine(item, subitem));
                }
            }

            foreach (var candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;
            return null;
        }

        private static string? findChromeExecutableMacos()
        {
            var candidates = new List<string>();

            var environmentPATH = Environment.GetEnvironmentVariable("PATH");
            if (environmentPATH == null)
                throw new Exception("Not found environment PATH.");

            var variables = environmentPATH.Split(Path.PathSeparator);
            foreach (var item in variables)
            {
                foreach (var subitem in new[] {
                    "google-chrome",
                    "chromium",
                    "chromium-browser",
                    "chrome",
                    "google-chrome-stable",
                })
                {
                    candidates.Add(Path.Combine(item, subitem));
                }
            }

            candidates.AddRange(new string[] {
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "/Applications/Chromium.app/Contents/MacOS/Chromium"
            });

            foreach (var candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;
            return null;
        }
    }
}
