using System.Runtime.InteropServices;

namespace NoDriver.Core
{
    internal class PlatformHelper
    {
        public static bool IsPosix()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        }

        [DllImport("libc")]
        private static extern uint getuid();

        public static bool IsRoot()
        {
            if (IsPosix())
            {
                try
                {
                    return getuid() == 0;
                }
                catch { }
            }
            return false;
        }
    }
}
