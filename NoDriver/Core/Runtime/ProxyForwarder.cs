using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace NoDriver.Core.Runtime
{
    public class ProxyForwarder
    {
        private TcpListener _server;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public string Host { get; private set; } = "";
        public int Port { get; private set; } = 0;
        public string Scheme { get; private set; } = "";
        public string FwHost { get; private set; } = "";
        public int FwPort { get; private set; } = 0;
        public string FwScheme { get; private set; } = "";
        public bool UseSsl { get; private set; } = false;
        public string Username { get; private set; } = "";
        public string Password { get; private set; } = "";
        public string ProxyServerUrl { get; private set; } = "";
        public X509Certificate2Collection ClientCertificates { get; set; }

        public ProxyForwarder(string proxyServerUrl, X509Certificate2Collection clientCertificates = null)
        {
            ProxyServerUrl = "";
            ClientCertificates = clientCertificates;

            if (!Uri.TryCreate(proxyServerUrl, UriKind.Absolute, out var url) || string.IsNullOrWhiteSpace(url.Scheme))
            {
                if (proxyServerUrl.Contains(":"))
                    ProxyServerUrl = proxyServerUrl;
                return;
            }

            Scheme = url.Scheme;
            UseSsl = url.Scheme == "https";

            if (string.IsNullOrWhiteSpace(url.UserInfo))
            {
                ProxyServerUrl = url.ToString();
                return;
            }

            Port = Util.FreePort();
            Host = "127.0.0.1";
            FwPort = url.Port;
            FwHost = url.Host;
            FwScheme = Scheme;

            var userInfoParts = url.UserInfo.Split(':');
            Username = userInfoParts.Length > 0 ? userInfoParts[0] : "";
            Password = userInfoParts.Length > 1 ? userInfoParts[1] : "";

            if (Scheme.StartsWith("http"))
                ProxyServerUrl = $"http://{Host}:{Port}";
            else
                ProxyServerUrl = $"{Scheme}://{Host}:{Port}";

            Console.WriteLine($"{Scheme} proxy with authentication is requested: {ProxyServerUrl}");
            Console.WriteLine($"Starting forward proxy on {Host}:{Port} which forwards to {ProxyServerUrl}");

            _ = ListenAsync();
        }
    }
}
