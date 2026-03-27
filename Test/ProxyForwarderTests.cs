using NoDriver.Core.Runtime;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Test
{
    [TestClass]
    public class ProxyForwarderTests
    {
        private Browser? _browser;

        [TestInitialize]
        public async Task Setup()
        {
            var config = new Config
            {
                //Headless = true,
                AutodiscoverTargets = false
            };
            // 告訴瀏覽器略過憑證錯誤
            config.AddArgument("--ignore-certificate-errors");
            _browser = await Browser.CreateAsync(config);
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_browser != null)
                await _browser.DisposeAsync();
        }

        [TestMethod]
        public async Task ProxyForwarder_WithAuth_HttpProxy_HttpsUrl_ShouldForwardCorrectly()
        {
            await ProxyForwarder_WithAuth_ShouldForwardCorrectly("http", "https");
        }

        [TestMethod]
        public async Task ProxyForwarder_WithAuth_HttpsProxy_HttpsUrl_ShouldForwardCorrectly()
        {
            await ProxyForwarder_WithAuth_ShouldForwardCorrectly("https", "https");
        }

        [TestMethod]
        public async Task ProxyForwarder_WithAuth_HttpProxy_HttpUrl_ShouldForwardCorrectly()
        {
            await ProxyForwarder_WithAuth_ShouldForwardCorrectly("http", "http");
        }

        [TestMethod]
        public async Task ProxyForwarder_WithAuth_HttpsProxy_HttpUrl_ShouldForwardCorrectly()
        {
            await ProxyForwarder_WithAuth_ShouldForwardCorrectly("https", "http");
        }

        private async Task ProxyForwarder_WithAuth_ShouldForwardCorrectly(string proxyProtocol, string urlProtocol)
        {
            // Arrange
            var user = "testuser";
            var pass = "testpass";
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                // 啟動微型上游 HTTP 代理伺服器
                var useHttpsForProxy = proxyProtocol == "https";
                var (proxyPort, proxyTask) = StartDummyUpstreamProxy(user, pass, useHttpsForProxy, cts.Token);

                // 建立 ProxyForwarder
                var proxyString = $"{proxyProtocol}://{user}:{pass}@127.0.0.1:{proxyPort}";
                await using (var forwarder = new ProxyForwarder(proxyString, remoteCertificateValidationCallback:
                    useHttpsForProxy ? (sender, cert, chain, err) => true : null))
                {
                    // 建立一個 HttpClient，設定使用剛建立的 ProxyForwarder 作為本機代理
                    var proxy = new WebProxy(forwarder.ProxyServer);
                    using (var handler = new HttpClientHandler
                    {
                        Proxy = proxy,
                        UseProxy = true,
                        ServerCertificateCustomValidationCallback = (sender, cert, chain, err) => true
                    })
                    using (var client = new HttpClient(handler))
                    {
                        // Act: 透過 Forwarder 向隨便一個網域發出請求
                        using (var response = await client.GetAsync($"{urlProtocol}://example.com"))
                        {
                            var html = await response.Content.ReadAsStringAsync();

                            // Assert
                            Assert.IsTrue(response.IsSuccessStatusCode, "代理轉發請求失敗");
                            Assert.AreEqual("<html><body>HTTP Proxy Success</body></html>", html, "代理回傳的內容與預期不符");
                        }
                    }
                }
            }
        }

        [TestMethod]
        public async Task Browser_CreateContextAsync_HttpProxy_HttpsUrl_ShouldSucceed()
        {
            await Browser_CreateContextAsync_ShouldSucceed("http", "https");
        }

        [TestMethod]
        public async Task Browser_CreateContextAsync_HttpsProxy_HttpsUrl_ShouldSucceed()
        {
            await Browser_CreateContextAsync_ShouldSucceed("https", "https");
        }

        [TestMethod]
        public async Task Browser_CreateContextAsync_HttpProxy_HttpUrl_ShouldSucceed()
        {
            await Browser_CreateContextAsync_ShouldSucceed("http", "http");
        }

        [TestMethod]
        public async Task Browser_CreateContextAsync_HttpsProxy_HttpUrl_ShouldSucceed()
        {
            await Browser_CreateContextAsync_ShouldSucceed("https", "http");
        }

        private async Task Browser_CreateContextAsync_ShouldSucceed(string proxyProtocol, string urlProtocol)
        {
            // Arrange
            var user = "browserUser";
            var pass = "browserPass";
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                // 啟動微型上游 HTTP 代理伺服器
                var useHttpsForProxy = proxyProtocol == "https";
                var (proxyPort, proxyTask) = StartDummyUpstreamProxy(user, pass, useHttpsForProxy, cts.Token);

                // Act: 透過 Browser 的 CreateContextAsync 建立 ProxyForwarder 作為本機代理
                var proxyString = $"{proxyProtocol}://{user}:{pass}@127.0.0.1:{proxyPort}";
                var tab = await _browser!.CreateContextAsync(
                    url: $"{urlProtocol}://example.com",
                    newWindow: true,
                    proxyServer: proxyString,
                    proxyBypassList: ["https://www.google.com"],
                    remoteCertificateValidationCallback:
                        useHttpsForProxy ? (sender, cert, chain, err) => true : null,
                    token: cts.Token);

                // 等待一下確保瀏覽器有發送請求
                await tab.WaitAsync(0.5);

                // Assert
                var root = await tab.SelectAsync("body");
                var html = await root!.GetHtmlAsync();
                Assert.AreEqual("<body>HTTP Proxy Success</body>", html, "代理回傳的內容與預期不符");

                // 確認代理伺服器有成功完成任務（沒有拋出錯誤）
                await proxyTask;
            }
        }

        [TestMethod]
        public async Task Browser_CreateContextAsync_WithSocks5Proxy_ShouldSucceed()
        {
            // Arrange
            var user = "socksUser";
            var pass = "socksPass";
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                // 啟動微型上游 SOCKS5 代理伺服器
                var (proxyPort, proxyTask) = StartDummyUpstreamSocksProxy(user, pass, cts.Token);

                // Act
                // 透過 Browser 的 CreateContextAsync 建立綁定代理伺服器的全新 Tab
                // 注意：底層會建立 ProxyForwarder 來處理帶有帳密的 SOCKS 連線
                var proxyString = $"socks5://{user}:{pass}@127.0.0.1:{proxyPort}";
                var tab = await _browser!.CreateContextAsync(
                    url: "http://example.com",
                    newWindow: true,
                    proxyServer: proxyString,
                    token: cts.Token);

                // 等待一下確保瀏覽器有發送請求並完成握手
                await Task.Delay(1500);

                // Assert
                Assert.IsNotNull(tab);
                Assert.IsNotNull(tab.Target);

                // 確認 SOCKS 代理伺服器有成功完成任務（沒有拋出錯誤或認證失敗）
                await proxyTask;
            }
        }

        /// <summary>
        /// 啟動一個微型的本機 TCP Server 扮演上游 HTTP 代理伺服器。<br/>
        /// 它會驗證 HTTP CONNECT 請求中的 Proxy-Authorization 是否正確，<br/>
        /// 並回傳簡單的 HTTP 200 頁面。
        /// </summary>
        private (int Port, Task ServerTask) StartDummyUpstreamProxy(string user, string pass, bool useHttpsForProxy, CancellationToken token)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // 產生一張臨時的自簽憑證，用來應付 HTTPS 的 TLS 握手
            var dummyCert = GetDummyCertificate();
            if (dummyCert == null)
                throw new InvalidOperationException("Not found certificate.");

            var task = Task.Run(async () =>
            {
                try
                {
                    using (var client = await listener.AcceptTcpClientAsync(token))
                    using (var stream = client.GetStream())
                    {
                        // ==========================================
                        // 1. 【外層 TLS】如果 Proxy 自身是 HTTPS
                        // ==========================================

                        Stream proxyStream = stream;
                        if (useHttpsForProxy)
                        {
                            // 一連線進來，立刻以 Server 身份進行 TLS 握手
                            var sslStream = new SslStream(stream, true);
                            await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                            {
                                ServerCertificate = dummyCert,
                                ClientCertificateRequired = false,
                                EnabledSslProtocols =
                                    System.Security.Authentication.SslProtocols.Tls12 |
                                    System.Security.Authentication.SslProtocols.Tls13
                            }, token);
                            // 後續的 Proxy 認證與 CONNECT 請求都在這條加密通道中進行
                            proxyStream = sslStream;
                        }
                        await using (proxyStream)
                        {
                            // 先在明文模式下處理 Proxy 的 CONNECT 請求
                            using (var reader = new StreamReader(proxyStream, Encoding.Latin1, leaveOpen: true))
                            using (var writer = new StreamWriter(proxyStream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                            {
                                // 讀取 CONNECT 請求行 (例如: CONNECT example.com:443 HTTP/1.1)
                                var line = await reader.ReadLineAsync(token);
                                if (string.IsNullOrEmpty(line))
                                    return;

                                var parts = line.Split(' ');

                                // 判斷是 GET, POST, 或 CONNECT
                                var method = parts[0].ToUpper();

                                // 透過 Port 判斷是 HTTP(80) 還是 HTTPS(443)
                                var isHttps = parts.Length > 1 && parts[1].EndsWith(":443");

                                var hasAuth = false;
                                var auth = Convert.ToBase64String(Encoding.Latin1.GetBytes($"{user}:{pass}"));

                                // 解析 HTTP Headers 尋找認證資訊
                                while (!string.IsNullOrWhiteSpace(line = await reader.ReadLineAsync(token)))
                                {
                                    if (line.StartsWith("Proxy-Authorization: Basic", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var tokenStr = line.Substring("Proxy-Authorization: Basic".Length).Trim();
                                        if (tokenStr == auth)
                                            hasAuth = true;
                                    }
                                }

                                if (!hasAuth)
                                {
                                    await writer.WriteAsync("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n".AsMemory(), token);
                                    return;
                                }

                                // 這是一般的 GET/POST，直接回傳模擬的內容
                                if (method != "CONNECT")
                                {
                                    var body = "<html><body>Standard HTTP Proxy Success</body></html>";
                                    var bodyBytes = Encoding.UTF8.GetBytes(body);
                                    var response =
                                            $"HTTP/1.1 200 OK\r\n" +
                                            $"Content-Type: text/html; charset=utf-8\r\n" +
                                            $"Content-Length: {bodyBytes.Length}\r\n" +
                                            $"Connection: close\r\n\r\n" +
                                            $"{body}";
                                    await writer.WriteAsync(response.AsMemory(), token);
                                    await writer.FlushAsync();
                                    return;
                                }

                                // 認證成功：回傳 Connection Established 建立隧道
                                await writer.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n".AsMemory(), token);

                                // ==========================================
                                // 2. 【內層 TLS】如果目標網站也是 HTTPS
                                // ==========================================

                                // 隧道建立完成，決定後續溝通的 Stream
                                Stream targetStream = proxyStream;
                                if (isHttps)
                                {
                                    // 如果是 HTTPS，將 Stream 升級為 SslStream，並以 Server 身分進行 TLS 握手
                                    var sslStream = new SslStream(proxyStream, true);
                                    await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                                    {
                                        ServerCertificate = dummyCert,
                                        ClientCertificateRequired = false,
                                        EnabledSslProtocols =
                                            System.Security.Authentication.SslProtocols.Tls12 |
                                            System.Security.Authentication.SslProtocols.Tls13
                                    }, token);
                                    targetStream = sslStream;
                                }
                                await using (targetStream)
                                {
                                    // ==========================================
                                    // 3. 處理最終的 HTTP GET 請求並回傳結果
                                    // ==========================================

                                    // 使用新的 (解密後的) Stream 讀取真實的 HTTP 請求
                                    using (var targetReader = new StreamReader(targetStream, Encoding.Latin1, leaveOpen: true))
                                    using (var targetWriter = new StreamWriter(targetStream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                                    {
                                        var reqLine = await targetReader.ReadLineAsync(token);
                                        if (string.IsNullOrEmpty(reqLine))
                                            return;

                                        // 將 Header 讀完
                                        while (!string.IsNullOrWhiteSpace(await targetReader.ReadLineAsync(token))) { }

                                        // 模擬目標伺服器回傳假內容
                                        var body = $"<html><body>HTTP Proxy Success</body></html>";
                                        var bodyBytes = Encoding.UTF8.GetBytes(body);
                                        var response =
                                            $"HTTP/1.1 200 OK\r\n" +
                                            $"Content-Type: text/html; charset=utf-8\r\n" +
                                            $"Content-Length: {bodyBytes.Length}\r\n" +
                                            $"Connection: close\r\n\r\n" +
                                            $"{body}";
                                        await targetWriter.WriteAsync(response.AsMemory(), token);
                                        await targetWriter.FlushAsync();
                                    }
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"Dummy Proxy Server Error: {ex.Message}");
                    throw; // 將錯誤拋出讓測試可以捕捉到失敗
                }
                finally
                {
                    listener.Stop();
                }
            }, token);

            return (port, task);
        }

        /// <summary>
        /// 啟動一個微型的本機 TCP Server 扮演上游 SOCKS5 代理伺服器。
        /// 負責驗證 SOCKS5 的 Handshake 流程與帳號密碼認證 (Method 0x02)。
        /// </summary>
        private (int port, Task serverTask) StartDummyUpstreamSocksProxy(string user, string pass, CancellationToken token)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var task = Task.Run(async () =>
            {
                try
                {
                    using (var client = await listener.AcceptTcpClientAsync(token))
                    using (var stream = client.GetStream())
                    {
                        // 1. Handshake (Client 傳送支援的 Methods)
                        var header = await ReadBytesAsync(stream, 2, token);
                        if (header[0] != 5) 
                            throw new Exception("Not SOCKS5");

                        var nMethods = header[1];
                        await ReadBytesAsync(stream, nMethods, token);

                        // 回應 Server 選擇的 Method: 0x02 (Username/Password)
                        await stream.WriteAsync(new byte[] { 5, 2 }, 0, 2, token);

                        // 2. Authentication Phase (Username/Password 驗證)
                        var authHeader = await ReadBytesAsync(stream, 2, token); // [ver(1), ulen]
                        var ulen = authHeader[1];
                        var userBytes = await ReadBytesAsync(stream, ulen, token);
                        var _user = Encoding.Latin1.GetString(userBytes);

                        var passLenByte = await ReadBytesAsync(stream, 1, token);
                        var plen = passLenByte[0];
                        var passBytes = await ReadBytesAsync(stream, plen, token);
                        var _pass = Encoding.Latin1.GetString(passBytes);

                        if (_user != user || _pass != pass)
                        {
                            // 認證失敗
                            await stream.WriteAsync(new byte[] { 1, 1 }, 0, 2, token);
                            throw new Exception($"SOCKS Auth failed. Expected: {user}:{pass}, Got: {_user}:{_pass}");
                        }

                        // 認證成功
                        await stream.WriteAsync(new byte[] { 1, 0 }, 0, 2, token);

                        // 3. Connect Phase (Client 請求連線到目標)
                        var reqHeader = await ReadBytesAsync(stream, 4, token); // [ver(5), cmd(1), rsv(0), atyp]
                        var atyp = reqHeader[3];

                        // 讀取目標位址與 Port (為了讓流程走完，簡單略過這段 Bytes)
                        if (atyp == 1) 
                            await ReadBytesAsync(stream, 6, token); // IPv4 + Port
                        else if (atyp == 3)
                        {
                            var domainLen = await ReadBytesAsync(stream, 1, token);
                            await ReadBytesAsync(stream, domainLen[0] + 2, token); // Domain + Port
                        }
                        else if (atyp == 4) 
                            await ReadBytesAsync(stream, 18, token); // IPv6 + Port

                        // 回傳 Connect 成功
                        // [ver(5), rep(0: success), rsv(0), atyp(1: ipv4), bnd.addr(4 bytes 0), bnd.port(2 bytes 0)]
                        await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, 0, 10, token);

                        // 4. Data Phase (模擬目標網站回傳資料)
                        using (var reader = new StreamReader(stream, Encoding.Latin1, leaveOpen: true))
                        using (var writer = new StreamWriter(stream, Encoding.Latin1, leaveOpen: true) { AutoFlush = true })
                        {
                            var line = "";
                            while (!string.IsNullOrWhiteSpace(line = await reader.ReadLineAsync(token)))
                            {
                                // 將 Header 讀完
                            }

                            var body = "<html><body>SOCKS5 Proxy Success</body></html>";
                            var response = 
                                $"HTTP/1.1 200 OK\r\n" +
                                $"Content-Type: text/html\r\n" +
                                $"Content-Length: {body.Length}\r\n" +
                                $"Connection: close\r\n\r\n" +
                                $"{body}";
                            await writer.WriteAsync(response.AsMemory(), token);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"Dummy SOCKS Server Error: {ex.Message}");
                    throw; // 將錯誤拋出讓測試可以捕捉到失敗
                }
                finally
                {
                    listener.Stop();
                }
            }, token);

            return (port, task);
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

        private X509Certificate2? GetDummyCertificate()
        {
            // ASP.NET Core HTTPS development certificate
            var devCertOid = "1.3.6.1.4.1.311.84.1.1";

            // 到「個人憑證區」搜尋
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);

                var existingCerts = store.Certificates.Find(
                    X509FindType.FindByExtension,
                    devCertOid,
                    validOnly: false);

                foreach (var cert in existingCerts)
                {
                    // 檢查是否還在效期內
                    if (DateTime.Now < cert.NotAfter && DateTime.Now > cert.NotBefore)
                        return cert;
                }
            }
            return null;
        }
    }
}
