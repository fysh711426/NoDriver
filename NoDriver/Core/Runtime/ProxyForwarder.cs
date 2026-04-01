using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoDriver.Core.Runtime
{
    public class ProxyForwarder : IDisposable, IAsyncDisposable
    {
        private TcpListener? _server;
        private Task _listenerTask = Task.CompletedTask;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        protected readonly ILogger? _logger;

        public string Host { get; private set; } = "";
        public int Port { get; private set; } = 0;
        public string Scheme { get; private set; } = "";
        public string FwHost { get; private set; } = "";
        public int FwPort { get; private set; } = 0;
        public string FwScheme { get; private set; } = "";
        public bool UseSsl { get; private set; } = false;
        public string Username { get; private set; } = "";
        public string Password { get; private set; } = "";
        public string ProxyServer { get; private set; } = "";
        public X509Certificate2Collection? ClientCertificates { get; private set; } = null;
        public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; private set; } = null;

        public ProxyForwarder(string proxyServer,
            X509Certificate2Collection? clientCertificates = null,
            RemoteCertificateValidationCallback? remoteCertificateValidationCallback = null,
            ILogger? logger = null)
        {
            _logger = logger;

            ProxyServer = "";
            ClientCertificates = clientCertificates;
            RemoteCertificateValidationCallback = remoteCertificateValidationCallback;

            if (!Uri.TryCreate(proxyServer, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Scheme))
            {
                if (proxyServer.Contains(":"))
                    ProxyServer = proxyServer;
                return;
            }

            Scheme = uri.Scheme;
            UseSsl = uri.Scheme == "https";

            if (string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                ProxyServer = uri.ToString();
                return;
            }

            Port = Util.FreePort();
            Host = "127.0.0.1";
            FwPort = uri.Port;
            FwHost = uri.Host;
            FwScheme = Scheme;

            var credentials = uri.UserInfo.Split(':', 2);
            Username = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : "";
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "";

            if (Scheme.StartsWith("http"))
                ProxyServer = $"http://{Host}:{Port}";
            else
                ProxyServer = $"{Scheme}://{Host}:{Port}";

            _logger?.LogInformation($"{Scheme} proxy with authentication is requested: {ProxyServer}");
            _logger?.LogInformation($"Starting forward proxy on {Host}:{Port} which forwards to {ProxyServer}");

            _listenerTask = ListenLoopAsync(_cts.Token);
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            try
            {
                _server = new TcpListener(IPAddress.Parse(Host), Port);
                _server.Start();

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _server.AcceptTcpClientAsync(token);
                        client.NoDelay = true;
                        client.ReceiveTimeout = 30000;
                        client.SendTimeout = 30000;
                        client.Client.SetSocketOption(
                            SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                        _ = Task.Run(() => HandleRequestAsync(client, token));
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug($"Listener exception: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"Failed to start listener: {ex.Message}");
            }
            finally
            {
                _server?.Stop();
            }
        }

        private async Task HandleRequestAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using (client)
                {
                    using (var clientStream = client.GetStream())
                    {
                        if (Scheme.StartsWith("socks"))
                            await HandleSocksRequestAsync(clientStream, token);
                        else if (Scheme.StartsWith("http"))
                            await HandleHttpRequestAsync(clientStream, token);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"Error handling request: {ex.Message}");
            }
        }

        private async Task HandleHttpsRequestAsync(NetworkStream clientStream, CancellationToken token)
        {
            var MAX_LINE_LENGTH = 8192;
            var REQUEST_TIMEOUT = 5.0;
            var UPSTREAM_CONNECT_TIMEOUT = 30.0;

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                try
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                    var line = await ReadLineAsync(clientStream, MAX_LINE_LENGTH, timeoutCts.Token);
                    if (string.IsNullOrEmpty(line))
                        return;

                    if (!line.StartsWith("CONNECT"))
                    {
                        _logger?.LogWarning($"Non-CONNECT request received: {line.Trim()}");
                        await WriteTextAsync(clientStream, "HTTP/1.1 400 Bad Request\r\n\r\n", timeoutCts.Token);
                        return;
                    }

                    var parts = line.Split(' ');
                    if (parts.Length < 2 || !parts[1].Contains(":"))
                    {
                        _logger?.LogWarning($"Malformed CONNECT request: {line.Trim()}");
                        await WriteTextAsync(clientStream, "HTTP/1.1 400 Bad Request\r\n\r\n", timeoutCts.Token);
                        return;
                    }

                    var targetHostPort = parts[1];

                    while (true)
                    {
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                        var header = await ReadLineAsync(clientStream, MAX_LINE_LENGTH, timeoutCts.Token);
                        if (string.IsNullOrEmpty(header) || header == "\r\n" || header == "\n")
                            break;
                    }

                    using (var remoteClient = new TcpClient())
                    {
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(UPSTREAM_CONNECT_TIMEOUT));

                        Stream? remoteStream = null;
                        try
                        {
                            try
                            {
                                await remoteClient.ConnectAsync(FwHost, FwPort, timeoutCts.Token);
                                remoteStream = remoteClient.GetStream();

                                if (UseSsl)
                                {
                                    var sslStream = new SslStream(remoteStream, false);
                                    await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                                    {
                                        TargetHost = FwHost,
                                        EnabledSslProtocols =
                                            System.Security.Authentication.SslProtocols.Tls12 |
                                            //System.Security.Authentication.SslProtocols.Tls13,
                                            (System.Security.Authentication.SslProtocols)12288,
                                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                                        ClientCertificates = ClientCertificates,
                                        RemoteCertificateValidationCallback = RemoteCertificateValidationCallback
                                    }, timeoutCts.Token);
                                    remoteStream = sslStream;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                _logger?.LogError($"Timeout connecting to upstream proxy {FwHost}:{FwPort}");
                                await WriteTextAsync(clientStream, "HTTP/1.1 504 Gateway Timeout\r\n\r\n", CancellationToken.None);
                                return;
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError($"Failed to connect to upstream proxy: {ex.Message}");
                                await WriteTextAsync(clientStream, "HTTP/1.1 502 Bad Gateway\r\n\r\n", CancellationToken.None);
                                return;
                            }

                            var credentials = $"{Username}:{Password}";
                            var authEncoded = Convert.ToBase64String(EncodingEx.Latin1.GetBytes(credentials));

                            var connectRequest =
                                $"CONNECT {targetHostPort} HTTP/1.1\r\n" +
                                $"Host: {targetHostPort}\r\n" +
                                $"Proxy-Authorization: Basic {authEncoded}\r\n" +
                                $"Proxy-Connection: Keep-Alive\r\n\r\n";

                            await WriteTextAsync(remoteStream, connectRequest, timeoutCts.Token);

                            try
                            {
                                timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                                var responseLine = await ReadLineAsync(remoteStream, MAX_LINE_LENGTH, timeoutCts.Token);
                                if (string.IsNullOrEmpty(responseLine))
                                {
                                    _logger?.LogError("No response from upstream proxy.");
                                    await WriteTextAsync(clientStream, "HTTP/1.1 502 Bad Gateway\r\n\r\n", timeoutCts.Token);
                                    return;
                                }

                                while (true)
                                {
                                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                                    var header = await ReadLineAsync(remoteStream, MAX_LINE_LENGTH, timeoutCts.Token);
                                    if (string.IsNullOrEmpty(header) || header == "\r\n" || header == "\n")
                                        break;
                                }

                                if (!responseLine.Contains(" 200 "))
                                {
                                    _logger?.LogError($"Upstream proxy rejected connection: {responseLine.Trim()}");
                                    await WriteTextAsync(clientStream, "HTTP/1.1 502 Bad Gateway\r\n", timeoutCts.Token);
                                    await WriteTextAsync(clientStream, "Content-Type: text/plain\r\n", timeoutCts.Token);
                                    await WriteTextAsync(clientStream, "\r\n", timeoutCts.Token);
                                    await WriteTextAsync(clientStream, "Upstream proxy rejected the connection\r\n", timeoutCts.Token);
                                    return;
                                }

                                await WriteTextAsync(clientStream, "HTTP/1.1 200 Connection Established\r\n\r\n", timeoutCts.Token);

                                timeoutCts.CancelAfter(Timeout.Infinite);
                                await PipeAsync(clientStream, remoteStream, timeoutCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                _logger?.LogError("Timeout reading upstream proxy response.");
                                await WriteTextAsync(clientStream, "HTTP/1.1 504 Gateway Timeout\r\n\r\n", CancellationToken.None);
                            }
                            catch (InvalidDataException ex)
                            {
                                // 這裡應該要處理
                                _logger?.LogWarning(ex.Message);
                            }
                        }
                        finally
                        {
                            if (remoteStream != null)
                                await remoteStream.DisposeAsync();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogWarning("Client request timeout.");
                    await WriteTextAsync(clientStream, "HTTP/1.1 408 Request Timeout\r\n\r\n", CancellationToken.None);
                }
                catch (InvalidDataException)
                {
                    _logger?.LogWarning("Oversized header line.");
                    await WriteTextAsync(clientStream, "HTTP/1.1 431 Request Header Fields Too Large\r\n\r\n", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Error handling HTTP proxy request: {ex.Message}");
                    await WriteTextAsync(clientStream, "HTTP/1.1 500 Internal Server Error\r\n\r\n", CancellationToken.None);
                }
            }
        }

        private async Task HandleHttpRequestAsync(NetworkStream clientStream, CancellationToken token)
        {
            var MAX_LINE_LENGTH = 8192;
            var REQUEST_TIMEOUT = 5.0;
            var UPSTREAM_CONNECT_TIMEOUT = 30.0;

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                try
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                    var line = await ReadLineAsync(clientStream, MAX_LINE_LENGTH, timeoutCts.Token);
                    if (string.IsNullOrEmpty(line))
                        return;

                    // 判斷客戶端請求是 CONNECT 還是 一般的 HTTP Method (GET, POST...)
                    var isConnect = line.StartsWith("CONNECT", StringComparison.OrdinalIgnoreCase);

                    var parts = line.Split(' ');
                    if (isConnect)
                    {
                        if (parts.Length < 2 || !parts[1].Contains(":"))
                        {
                            _logger?.LogWarning($"Malformed CONNECT request: {line.Trim()}");
                            await WriteTextAsync(clientStream, "HTTP/1.1 400 Bad Request\r\n\r\n", timeoutCts.Token);
                            return;
                        }
                    }

                    // 讀取並保存客戶端送來的 Headers
                    var clientHeaders = new StringBuilder();
                    while (true)
                    {
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                        var header = await ReadLineAsync(clientStream, MAX_LINE_LENGTH, timeoutCts.Token);
                        if (string.IsNullOrEmpty(header) || header == "\r\n" || header == "\n")
                            break;

                        if (!isConnect)
                        {
                            // 針對一般 HTTP，我們要過濾掉會衝突的連線標頭，待會補上我們自己的
                            if (header.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase) ||
                                header.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase) ||
                                header.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase))
                                continue;
                            clientHeaders.Append(header);
                        }
                    }

                    using (var remoteClient = new TcpClient())
                    {
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(UPSTREAM_CONNECT_TIMEOUT));

                        Stream? remoteStream = null;
                        try
                        {
                            try
                            {
                                await remoteClient.ConnectAsync(FwHost, FwPort, timeoutCts.Token);
                                remoteStream = remoteClient.GetStream();

                                if (UseSsl)
                                {
                                    var sslStream = new SslStream(remoteStream, false);
                                    await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                                    {
                                        TargetHost = FwHost,
                                        EnabledSslProtocols =
                                            System.Security.Authentication.SslProtocols.Tls12 |
                                            //System.Security.Authentication.SslProtocols.Tls13,
                                            (System.Security.Authentication.SslProtocols)12288,
                                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                                        ClientCertificates = ClientCertificates,
                                        RemoteCertificateValidationCallback = RemoteCertificateValidationCallback
                                    }, timeoutCts.Token);
                                    remoteStream = sslStream;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                _logger?.LogError($"Timeout connecting to upstream proxy {FwHost}:{FwPort}");
                                await WriteTextAsync(clientStream, "HTTP/1.1 504 Gateway Timeout\r\n\r\n", CancellationToken.None);
                                return;
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError($"Failed to connect to upstream proxy: {ex.Message}");
                                await WriteTextAsync(clientStream, "HTTP/1.1 502 Bad Gateway\r\n\r\n", CancellationToken.None);
                                return;
                            }

                            var credentials = $"{Username}:{Password}";
                            var authEncoded = Convert.ToBase64String(EncodingEx.Latin1.GetBytes(credentials));

                            if (isConnect)
                            {
                                var targetHostPort = parts[1];
                                var connectRequest =
                                    $"CONNECT {targetHostPort} HTTP/1.1\r\n" +
                                    $"Host: {targetHostPort}\r\n" +
                                    $"Proxy-Authorization: Basic {authEncoded}\r\n" +
                                    $"Proxy-Connection: Keep-Alive\r\n\r\n";

                                await WriteTextAsync(remoteStream, connectRequest, timeoutCts.Token);
                            }

                            try
                            {
                                if (isConnect)
                                {
                                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                                    var responseLine = await ReadLineAsync(remoteStream, MAX_LINE_LENGTH, timeoutCts.Token);
                                    if (string.IsNullOrEmpty(responseLine))
                                    {
                                        _logger?.LogError("No response from upstream proxy.");
                                        await WriteTextAsync(clientStream, "HTTP/1.1 502 Bad Gateway\r\n\r\n", timeoutCts.Token);
                                        return;
                                    }

                                    while (true)
                                    {
                                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                                        var header = await ReadLineAsync(remoteStream, MAX_LINE_LENGTH, timeoutCts.Token);
                                        if (string.IsNullOrEmpty(header) || header == "\r\n" || header == "\n")
                                            break;
                                    }

                                    if (!responseLine.Contains(" 200 "))
                                    {
                                        _logger?.LogError($"Upstream proxy rejected connection: {responseLine.Trim()}");
                                        await WriteTextAsync(clientStream, "HTTP/1.1 502 Bad Gateway\r\n", timeoutCts.Token);
                                        await WriteTextAsync(clientStream, "Content-Type: text/plain\r\n", timeoutCts.Token);
                                        await WriteTextAsync(clientStream, "\r\n", timeoutCts.Token);
                                        await WriteTextAsync(clientStream, "Upstream proxy rejected the connection\r\n", timeoutCts.Token);
                                        return;
                                    }

                                    await WriteTextAsync(clientStream, "HTTP/1.1 200 Connection Established\r\n\r\n", timeoutCts.Token);
                                }
                                else
                                {
                                    // 重新組合「請求行」+「原本的 Headers」+「上游 Proxy 認證」+「空行」
                                    var forwardRequest =
                                        $"{line}" +
                                        $"{clientHeaders}" +
                                        $"Connection: close\r\n" +
                                        $"Proxy-Authorization: Basic {authEncoded}\r\n" +
                                        $"Proxy-Connection: close\r\n\r\n";

                                    // 強制設定為 close。因為轉發完這一次 Req/Res 後，PipeAsync 就會結束關閉連線
                                    // 若保持 Keep-Alive 會導致後續請求直接跑進純 TCP Pipe 裡
                                    // 而沒有被補上 Proxy-Authorization 導致上游拒絕
                                    await WriteTextAsync(remoteStream, forwardRequest, timeoutCts.Token);
                                }

                                timeoutCts.CancelAfter(Timeout.Infinite);
                                await PipeAsync(clientStream, remoteStream, timeoutCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                _logger?.LogError("Timeout reading upstream proxy response.");
                                await WriteTextAsync(clientStream, "HTTP/1.1 504 Gateway Timeout\r\n\r\n", CancellationToken.None);
                            }
                            catch (InvalidDataException ex)
                            {
                                // 這裡應該要處理
                                _logger?.LogWarning(ex.Message);
                            }
                        }
                        finally
                        {
                            if (remoteStream != null)
                                await remoteStream.DisposeAsync();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogWarning("Client request timeout.");
                    await WriteTextAsync(clientStream, "HTTP/1.1 408 Request Timeout\r\n\r\n", CancellationToken.None);
                }
                catch (InvalidDataException)
                {
                    _logger?.LogWarning("Oversized header line.");
                    await WriteTextAsync(clientStream, "HTTP/1.1 431 Request Header Fields Too Large\r\n\r\n", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Error handling HTTP proxy request: {ex.Message}");
                    await WriteTextAsync(clientStream, "HTTP/1.1 500 Internal Server Error\r\n\r\n", CancellationToken.None);
                }
            }
        }

        private async Task HandleSocksRequestAsync(NetworkStream clientStream, CancellationToken token)
        {
            const byte ATYP_IPv4 = 0x01;
            const byte ATYP_IPv6 = 0x04;
            const byte ATYP_DNS = 0x03;

            try
            {
                // 握手與協商 (Handshake & Method Selection)
                byte version, numMethods = 0;
                try
                {
                    // 讀取前兩個位元組: [版本號, 支援的驗證方法數量]
                    var bytes = await ReadBytesAsync(clientStream, 2, token);
                    version = bytes[0];    // 預期為 0x05 (SOCKS5)
                    numMethods = bytes[1]; // 客戶端提供的驗證方法總數
                }
                catch
                {
                    _logger?.LogDebug("Failed to read Socks header.");
                    return;
                }

                // 讀取客戶端提供的所有驗證方法 (Methods list)，雖然這裡直接讀完但不做檢查
                await ReadBytesAsync(clientStream, numMethods, token);

                // 回應客戶端: 選擇「免驗證」(0x00)
                await clientStream.WriteAsync(new byte[] { version, 0 }, token);

                // 讀取連線請求 (Read Connect Request)
                byte cmd, resv, atyp = 0;
                {
                    // 讀取請求標頭的前 4 位元組: [版本, 指令(1=CONNECT), 保留位, 地址類型]
                    var bytes = await ReadBytesAsync(clientStream, 4, token);
                    version = bytes[0];
                    cmd = bytes[1];     // 通常 0x01 代表 CONNECT 請求
                    resv = bytes[2];
                    atyp = bytes[3];    // 地址類型: IPv4, IPv6 或 DNS
                }

                var targetHost = "";
                var targetHostBytes = Array.Empty<byte>();

                // 根據地址類型 (ATYP) 讀取目標主機位址
                if (atyp == ATYP_IPv4)
                {
                    targetHostBytes = await ReadBytesAsync(clientStream, 4, token);
                    targetHost = new IPAddress(targetHostBytes).ToString();
                }
                else if (atyp == ATYP_IPv6)
                {
                    targetHostBytes = await ReadBytesAsync(clientStream, 16, token);
                    targetHost = new IPAddress(targetHostBytes).ToString();
                }
                else if (atyp == ATYP_DNS)
                {
                    var lenByte = await ReadBytesAsync(clientStream, 1, token);
                    var domainLen = lenByte[0];
                    targetHostBytes = await ReadBytesAsync(clientStream, domainLen, token);
                    targetHost = EncodingEx.Latin1.GetString(targetHostBytes);
                }

                // 讀取目標埠號 (2 bytes, 大端序)
                var portBytes = await ReadBytesAsync(clientStream, 2, token);
                var port = BinaryPrimitives.ReadUInt16BigEndian(portBytes);

                // 向「上游代理伺服器」發起連線 (Connect to Upstream Proxy)
                using (var remoteClient = new TcpClient())
                {
                    // 連接到上游代理伺服器 (FwHost:FwPort)
                    await remoteClient.ConnectAsync(FwHost, FwPort, token);
                    await using (var remoteStream = remoteClient.GetStream())
                    {
                        // 與上游進行握手，告知我們支援「帳密驗證」(2)
                        await remoteStream.WriteAsync(new byte[] { version, 1, 2 }, token);

                        // 讀取上游的選擇結果
                        byte serverVersion, serverAuthMethod = 0;
                        {
                            var bytes = await ReadBytesAsync(remoteStream, 2, token);
                            serverVersion = bytes[0];
                            serverAuthMethod = bytes[1];
                        }

                        // 若上游要求帳密驗證 (Method 0x02)
                        if (serverAuthMethod == 2)
                        {
                            var uBytes = EncodingEx.Latin1.GetBytes(Username);
                            var pBytes = EncodingEx.Latin1.GetBytes(Password);

                            using (var authTicket = new MemoryStream())
                            {
                                authTicket.WriteByte(1);                    // 帳密驗證協議版本
                                authTicket.WriteByte((byte)uBytes.Length);  // 帳號長度
                                authTicket.Write(uBytes, 0, uBytes.Length); // 帳號內容
                                authTicket.WriteByte((byte)pBytes.Length);  // 密碼長度
                                authTicket.Write(pBytes, 0, pBytes.Length); // 密碼內容

                                await remoteStream.WriteAsync(authTicket.ToArray(), token);
                            }

                            // 讀取上游的驗證結果
                            byte ver, result = 0;
                            {
                                var bytes = await ReadBytesAsync(remoteStream, 2, token);
                                ver = bytes[0];
                                result = bytes[1];
                            }

                            // 0 代表驗證成功
                            if (result != 0)
                                throw new Exception($"Socks authentication error: {result}");
                        }

                        // 將客戶端的連線請求轉發給上游
                        await remoteStream.WriteAsync(new byte[] { version, cmd, resv, atyp }, token);

                        // 若是 DNS 類型，要先傳送域名長度
                        if (atyp == ATYP_DNS)
                            await remoteStream.WriteAsync(new byte[] { (byte)targetHostBytes.Length }, token);

                        // 傳送目標地址與埠號
                        await remoteStream.WriteAsync(targetHostBytes, token);
                        await remoteStream.WriteAsync(portBytes, token);

                        // 讀取並驗證上游的回應 (確保上游連線成功)
                        var upResponse = await ReadBytesAsync(remoteStream, 4, token);
                        if (upResponse[1] != 0) // 第 2 byte 為狀態碼，0 代表連線成功
                            throw new Exception($"Socks upstream connection error: {upResponse[1]}");

                        // 消耗掉上游回傳的 BND.ADDR 和 BND.PORT (把它們讀完但不使用)
                        var addrLen = upResponse[3] switch
                        {
                            ATYP_IPv4 => 4 + 2,                                                // IPv4 (4)  + Port (2)
                            ATYP_IPv6 => 16 + 2,                                               // IPv6 (16) + Port (2)
                            ATYP_DNS => (await ReadBytesAsync(remoteStream, 1, token))[0] + 2, // Len + Port
                            _ => throw new Exception("Unknown ATYP")
                        };
                        var upResponseAddr = await ReadBytesAsync(remoteStream, addrLen, token); // 徹底清空上游的 Header 緩衝區

                        // 將結果寫回給 Chrome
                        await clientStream.WriteAsync(upResponse, token);
                        if (upResponse[3] == ATYP_DNS)
                            await clientStream.WriteAsync(new byte[] { (byte)(addrLen - 2) }, token);
                        await clientStream.WriteAsync(upResponseAddr, token);

                        await PipeAsync(clientStream, remoteStream, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Socks handling canceled.");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error handling Socks proxy request: {ex.Message}");
            }
        }

        private async Task<string> ReadLineAsync(Stream stream, int limit, CancellationToken token)
        {
            var count = 0;
            var buffer = ArrayPool<byte>.Shared.Rent(limit);
            try
            {
                while (count < limit)
                {
                    var read = await stream.ReadAsync(buffer, count, 1, token);
                    if (read == 0)
                        break;

                    var b = buffer[count];
                    count++;

                    if (b == (byte)'\n')
                        break;
                }

                if (count == limit)
                    throw new InvalidDataException("Header line exceeded maximum limit.");
                return EncodingEx.Latin1.GetString(buffer, 0, count);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task WriteTextAsync(Stream stream, string text, CancellationToken token)
        {
            var bytes = EncodingEx.Latin1.GetBytes(text);
            await stream.WriteAsync(bytes, 0, bytes.Length, token);
        }

        private async Task<byte[]> ReadBytesAsync(Stream stream, int size, CancellationToken token)
        {
            var buffer = new byte[size];
            var count = 0;
            while (count < size)
            {
                var read = await stream.ReadAsync(buffer, count, size - count, token);
                if (read == 0)
                    throw new EndOfStreamException("Socket connection closed unexpectedly.");
                count += read;
            }
            return buffer;
        }

        private async Task PipeAsync(Stream client, Stream remote, CancellationToken token)
        {
            _logger?.LogDebug("Client proxy to authenticated proxy pipe.");

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var task1 = client.CopyToAsync(remote, cts.Token);
                var task2 = remote.CopyToAsync(client, cts.Token);

                var completedTask = await Task.WhenAny(task1, task2);
                cts.Cancel();

                try
                {
                    await Task.WhenAll(task1, task2);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger?.LogDebug($"Piping error: {ex.Message}");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            try
            {
                _cts.Cancel();
                await _listenerTask;
            }
            catch { }
            _server?.Stop();
            _cts.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _cts.Cancel();
                }
                catch { }
                _server?.Stop();
                _cts.Dispose();
            }
        }
    }
}
