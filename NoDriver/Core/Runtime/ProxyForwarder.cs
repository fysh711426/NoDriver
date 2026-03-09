using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace NoDriver.Core.Runtime
{
    public class ProxyForwarder : IDisposable, IAsyncDisposable
    {
        private TcpListener? _server = null;
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
        public X509Certificate2Collection? ClientCertificates { get; private set; } = null;

        public ProxyForwarder(string proxyServerUrl, X509Certificate2Collection? clientCertificates = null)
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

            var credentials = url.UserInfo.Split(':', 2);
            Username = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : "";
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "";

            if (Scheme.StartsWith("http"))
                ProxyServerUrl = $"http://{Host}:{Port}";
            else
                ProxyServerUrl = $"{Scheme}://{Host}:{Port}";

            Console.WriteLine($"{Scheme} proxy with authentication is requested: {ProxyServerUrl}");
            Console.WriteLine($"Starting forward proxy on {Host}:{Port} which forwards to {ProxyServerUrl}");

            _ = ListenAsync(_cts.Token);
        }

        private async Task ListenAsync(CancellationToken token)
        {
            try
            {
                _server = new TcpListener(IPAddress.Parse(Host), Port);
                _server.Start();

                while (true)
                {
                    var client = await _server.AcceptTcpClientAsync(token);
                    _ = HandleRequestAsync(client, token);
                }
            }
            catch (OperationCanceledException) {}
            catch (Exception ex)
            {
                Console.WriteLine($"Listener exception: {ex.Message}");
            }
        }

        private async Task HandleRequestAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    using (var clientStream = client.GetStream())
                    {
                        if (Scheme.StartsWith("socks"))
                            await HandleSocksRequestAsync(clientStream, token);
                        else if (Scheme.StartsWith("http"))
                            await HandleHttpRequestAsync(clientStream, token);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling request: {ex.Message}");
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
                var clientReader = PipeReader.Create(clientStream);
                try
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                    var line = await ReadLineAsync(clientReader, MAX_LINE_LENGTH, timeoutCts.Token);
                    if (string.IsNullOrEmpty(line))
                        return;

                    if (!line.StartsWith("CONNECT"))
                    {
                        Console.WriteLine($"Non-CONNECT request received: {line.Trim()}");
                        await WriteTextAsync(clientStream, "HTTP/1.1 400 Bad Request\r\n\r\n", timeoutCts.Token);
                        return;
                    }

                    var parts = line.Split(' ');
                    if (parts.Length < 2 || !parts[1].Contains(":"))
                    {
                        Console.WriteLine($"Malformed CONNECT request: {line.Trim()}");
                        await WriteTextAsync(clientStream, "HTTP/1.1 400 Bad Request\r\n\r\n", timeoutCts.Token);
                        return;
                    }

                    var targetHostPort = parts[1];

                    while (true)
                    {
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                        var header = await ReadLineAsync(clientReader, MAX_LINE_LENGTH, timeoutCts.Token);
                        if (string.IsNullOrEmpty(header) || header == "\r\n" || header == "\n")
                            break;
                    }

                    using (var remoteClient = new TcpClient())
                    {
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(UPSTREAM_CONNECT_TIMEOUT));

                        var remoteStream = null as Stream;
                        try
                        {
                            try
                            {
                                await remoteClient.ConnectAsync(FwHost, FwPort, timeoutCts.Token);

                                remoteStream = remoteClient.GetStream();
                                if (UseSsl)
                                {
                                    var sslStream = new SslStream(remoteStream, false, (sender, cert, chain, err) => true);
                                    await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                                    {
                                        TargetHost = FwHost,
                                        ClientCertificates = ClientCertificates,
                                        EnabledSslProtocols =
                                            System.Security.Authentication.SslProtocols.Tls12 |
                                            System.Security.Authentication.SslProtocols.Tls13,
                                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                                    }, timeoutCts.Token);
                                    remoteStream = sslStream;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to connect to upstream proxy: {ex.Message}");
                                await WriteTextAsync(clientStream, "HTTP/1.1 502 Bad Gateway\r\n\r\n", timeoutCts.Token);
                                return;
                            }

                            var credentials = $"{Username}:{Password}";
                            var authEncoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));

                            var connectRequest =
                                $"CONNECT {targetHostPort} HTTP/1.1\r\n" +
                                $"Host: {targetHostPort}\r\n" +
                                $"Proxy-Authorization: Basic {authEncoded}\r\n" +
                                $"Proxy-Connection: Keep-Alive\r\n\r\n";

                            await WriteTextAsync(remoteStream, connectRequest, timeoutCts.Token);

                            var remoteReader = PipeReader.Create(remoteStream);
                            try
                            {
                                timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                                var responseLine = await ReadLineAsync(remoteReader, MAX_LINE_LENGTH, timeoutCts.Token);
                                if (string.IsNullOrEmpty(responseLine))
                                {
                                    Console.WriteLine("No response from upstream proxy.");
                                    await WriteTextAsync(clientStream, "HTTP/1.1 502 Bad Gateway\r\n\r\n", timeoutCts.Token);
                                    return;
                                }

                                while (true)
                                {
                                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT));
                                    var header = await ReadLineAsync(remoteReader, MAX_LINE_LENGTH, timeoutCts.Token);
                                    if (string.IsNullOrEmpty(header) || header == "\r\n" || header == "\n")
                                        break;
                                }

                                if (!responseLine.Contains(" 200 "))
                                {
                                    Console.WriteLine($"Upstream proxy rejected connection: {responseLine.Trim()}");
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
                                Console.WriteLine("Timeout reading upstream proxy response.");
                                await WriteTextAsync(clientStream, "HTTP/1.1 504 Gateway Timeout\r\n\r\n");
                            }
                            catch (InvalidDataException ex)
                            {
                                Console.WriteLine(ex.Message);
                            }
                            finally
                            {
                                await remoteReader.CompleteAsync();
                            }
                        }
                        finally
                        {
                            remoteStream?.Dispose();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Client request timeout.");
                    await WriteTextAsync(clientStream, "HTTP/1.1 408 Request Timeout\r\n\r\n");
                }
                catch (InvalidDataException ex)
                {
                    Console.WriteLine(ex.Message);
                    await WriteTextAsync(clientStream, "HTTP/1.1 431 Request Header Fields Too Large\r\n\r\n", timeoutCts.Token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling HTTP proxy request: {ex.Message}");
                    try
                    {
                        await WriteTextAsync(clientStream, "HTTP/1.1 500 Internal Server Error\r\n\r\n", timeoutCts.Token);
                    }
                    catch { }
                }
                finally
                {
                    await clientReader.CompleteAsync();
                }
            }
        }

        private async Task HandleSocksRequestAsync(NetworkStream clientStream, CancellationToken token)
        {

        }

        private async Task<string> ReadLineAsync(PipeReader reader, int limit, CancellationToken token)
        {
            while (true)
            {
                var result = await reader.ReadAsync(token);
                var buffer = result.Buffer;

                var position = buffer.PositionOf((byte)'\n');
                if (position != null)
                {
                    var linePosition = position.Value;
                    if (buffer.TryGet(ref linePosition, out _, advance: true))
                    {
                        var line = buffer.Slice(0, linePosition);
                        reader.AdvanceTo(linePosition);
                        return Encoding.UTF8.GetString(line);
                    }
                }

                if (buffer.Length > limit)
                    throw new InvalidDataException($"Oversized header line: {limit} bytes.");

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    reader.AdvanceTo(buffer.End);
                    return Encoding.UTF8.GetString(buffer);
                }
            }
        }

        private async Task WriteTextAsync(Stream stream, string text, CancellationToken token)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await stream.WriteAsync(bytes, 0, bytes.Length, token);
        }

        private async Task PipeAsync(Stream stream1, Stream stream2, CancellationToken token)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var task1 = stream1.CopyToAsync(stream2, cts.Token);
                var task2 = stream2.CopyToAsync(stream1, cts.Token);

                try
                {
                    var completedTask = await Task.WhenAny(task1, task2);
                }
                catch
                {
                    cts.Cancel();
                    try
                    {
                        await Task.WhenAll(task1, task2);
                    }
                    catch { }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            await Task.CompletedTask;
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
                _cts.Cancel();
                _server?.Stop();
                _cts.Dispose();
            }
        }
    }
}
