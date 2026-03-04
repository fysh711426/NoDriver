using System.Net;
using System.Net.Sockets;

namespace NoDriver.Core.Runtime
{
    public static class Util
    {
        //ok
        public static int FreePort()
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

        //ok
        public static IEnumerable<(double x, double y)> Circle(int x, int? y = null, int radius = 10, int num = 10, int dir = 0)
        {
            var r = radius;
            var w = num;
            if (y == null)
                y = x;
            var a = (int)(x - r * 2);
            var b = (int)(y.Value - r * 2);
            var m = (double)(2 * MathF.PI) / w;

            (double x, double y) calculatePoint(int i)
            {
                var px = a + r * Math.Sin(m * i);
                var py = b + r * Math.Cos(m * i);
                return (px, py);
            }

            if (dir == 0)
            {
                for (var i = 0; i < w + 1; i++)
                {
                    yield return calculatePoint(i);
                }
            }
            else
            {
                for (var i = w + 1; i > 0; i--)
                {
                    yield return calculatePoint(i);
                }
            }
        }
    }
}
