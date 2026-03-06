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
        public static Cdp.DOM.Node? FilterRecurse(Cdp.DOM.Node? doc, Func<Cdp.DOM.Node, bool> predicate)
        {
            if (doc?.Children != null)
            {
                foreach (var child in doc.Children)
                {
                    if (predicate(child))
                        return child;

                    if (child.ShadowRoots != null && child.ShadowRoots.Count > 0)
                    {
                        var shadowRootResult = FilterRecurse(child.ShadowRoots[0], predicate);
                        if (shadowRootResult != null)
                            return shadowRootResult;
                    }

                    var result = FilterRecurse(child, predicate);
                    if (result != null)
                        return result;
                }
            }
            return null;
        }

        //ok
        public static Element? FilterRecurse(Element? doc, Func<Element, bool> predicate)
        {
            if (doc?.Children != null)
            {
                foreach (var child in doc.Children)
                {
                    if (predicate(child))
                        return child;

                    if (child.ShadowChildren != null && child.ShadowChildren.Count > 0)
                    {
                        var shadowRootResult = FilterRecurse(child.ShadowChildren[0], predicate);
                        if (shadowRootResult != null)
                            return shadowRootResult;
                    }

                    var result = FilterRecurse(child, predicate);
                    if (result != null)
                        return result;
                }
            }
            return null;
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
