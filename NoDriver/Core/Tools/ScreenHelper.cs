using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace NoDriver.Core.Tools
{
    public static class ScreenHelper
    {
        public static async Task<(int Width, int Height)> GetResolutionAsync(CancellationToken token = default)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetWindowsResolution();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return await GetLinuxResolutionAsync();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GetMacResolution();
            return (1920, 1080);
        }

        //----- Windows -----
        private static readonly int SM_CXSCREEN = 0;
        private static readonly int SM_CYSCREEN = 1;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static (int Width, int Height) GetWindowsResolution()
        {
            try
            {
                var width = GetSystemMetrics(SM_CXSCREEN);
                var height = GetSystemMetrics(SM_CYSCREEN);
                if (width > 0 && height > 0)
                    return (width, height);
            }
            catch { }
            return (1920, 1080);
        }

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        //----- Windows -----

        //----- Mac -----
        [DllImport("CoreGraphics")]
        private static extern uint CGMainDisplayID();

        [DllImport("CoreGraphics")]
        private static extern nint CGDisplayPixelsWide(uint display);

        [DllImport("CoreGraphics")]
        private static extern nint CGDisplayPixelsHigh(uint display);

        private static (int Width, int Height) GetMacResolution()
        {
            try
            {
                var mainDisplay = CGMainDisplayID();
                var width = (int)CGDisplayPixelsWide(mainDisplay);
                var height = (int)CGDisplayPixelsHigh(mainDisplay);
                if (width > 0 && height > 0)
                    return (width, height);
            }
            catch { }
            return (1920, 1080);
        }

        //private static (int Width, int Height) GetMacResolution()
        //{
        //    try
        //    {
        //        var output = RunShellCommand("system_profiler", "SPDisplaysDataType");
        //        // "Resolution: 2560 x 1600"
        //        var match = Regex.Match(output, @"Resolution: (\d+) x (\d+)");
        //        if (match.Success)
        //            return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        //    }
        //    catch { }
        //    return (1920, 1080);
        //}
        //----- Mac -----

        //----- Linux -----
        private static async Task<(int Width, int Height)> GetLinuxResolutionAsync(CancellationToken token = default)
        {
            try
            {
                var output = await RunShellCommandAsync("xrandr", "--current", token);
                // "current 1920 x 1080"
                var match = Regex.Match(output, @"current (\d+) x (\d+)");
                if (match.Success)
                    return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            }
            catch { }
            return (1920, 1080);
        }

        private static async Task<string> RunShellCommandAsync(string fileName, string arguments, CancellationToken token = default)
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
                try
                {
                    using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
                        var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                        await process.WaitForExitAsync(timeoutCts.Token);
                        return output;
                    }
                }
                catch
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill();
                    }
                    catch { }
                    throw;
                }
            }
        }
        //----- Linux -----
    }
}
