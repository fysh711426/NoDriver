using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace NoDriver.Core.Tools
{
    public class ScreenHelper
    {
        private static readonly int SM_CXSCREEN = 0;
        private static readonly int SM_CYSCREEN = 1;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        public static (int Width, int Height) GetResolution()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetWindowsResolution();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetLinuxResolution();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GetMacResolution();
            return (1920, 1080);
        }

        private static (int Width, int Height) GetWindowsResolution()
        {
            try
            {
                var width = GetSystemMetrics(SM_CXSCREEN);
                var height = GetSystemMetrics(SM_CYSCREEN);
                if (width == 0 || height == 0)
                    throw new InvalidOperationException("Screen resolution not found.");
                return (width, height);
            }
            catch { }
            return (1920, 1080);
        }

        private static (int Width, int Height) GetLinuxResolution()
        {
            try
            {
                var output = RunShellCommand("xrandr", "--current");
                // "current 1920 x 1080"
                var match = Regex.Match(output, @"current (\d+) x (\d+)");
                if (match.Success)
                    return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            }
            catch { }
            return (1920, 1080);
        }

        private static (int Width, int Height) GetMacResolution()
        {
            try
            {
                var output = RunShellCommand("system_profiler", "SPDisplaysDataType");
                // "Resolution: 2560 x 1600"
                var match = Regex.Match(output, @"Resolution: (\d+) x (\d+)");
                if (match.Success)
                    return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            }
            catch { }
            return (1920, 1080);
        }

        private static string RunShellCommand(string fileName, string arguments)
        {
            var info = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                if (process == null)
                    throw new Exception("Failed to start process.");
                return process.StandardOutput.ReadToEnd();
            }
        }
    }
}
