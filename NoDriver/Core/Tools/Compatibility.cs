using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
#if !NET6_0_OR_GREATER
    /// <summary>
    /// 手動定義 IsExternalInit 以支援在舊框架中使用 C# init 屬性和 record
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
#endif
}

namespace System.Collections.Generic
{
    internal static class CompatibilityExtensions
    {
#if !NET6_0_OR_GREATER
        /// <summary>
        /// 為舊框架手動補上 KeyValuePair 的解構功能
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kvp, out TKey key, out TValue value)
        {
            key = kvp.Key;
            value = kvp.Value;
        }
#endif
    }
}

namespace System.Text
{
    internal static class EncodingEx
    {
#if NET6_0_OR_GREATER
        public static Encoding Latin1 => Encoding.Latin1;
#else
        private static readonly Lazy<Encoding> _latin1 = 
            new Lazy<Encoding>(() => Encoding.GetEncoding("ISO-8859-1"));
        public static Encoding Latin1 => _latin1.Value;
#endif
    }
}

namespace System.Diagnostics
{
    internal static class ProcessExtension
    {
#if !NET6_0_OR_GREATER
        // refer: https://stackoverflow.com/questions/470256/process-waitforexit-asynchronously
        public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            if (process.HasExited)
                return Task.FromResult(true);

            var tcs = new TaskCompletionSource<bool>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(true);
            if (cancellationToken != default)
                cancellationToken.Register(() => tcs.SetCanceled());

            return process.HasExited ? Task.FromResult(true) : tcs.Task;
        }
#endif
#if !NET6_0_OR_GREATER
        public static void Kill(this Process process, bool entireProcessTree)
        {
            if (entireProcessTree)
            {
                // 在舊版中，原生 API 不支援殺掉樹狀子程序
                // 如果你真的需要，通常要透過執行 Windows 命令 "taskkill /f /t /pid ..."
                // 但簡單起見，這裡通常只能退而求其次殺掉自己
                process.Kill();
                return;
            }
            process.Kill();
        }
#endif
        public static void AddArguments(this ProcessStartInfo info, IEnumerable<string> args)
        {
            if (args == null)
                return;

#if NET6_0_OR_GREATER
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }
#else
            var escapedArgs = args.Select(it =>
            {
                if (string.IsNullOrEmpty(it))
                    return "\"\"";

                // 如果沒有空白或引號，其實不用包裝
                if (!it.Any(c => char.IsWhiteSpace(c) || c == '\"'))
                    return it;

                // 實作標準的 Windows CommandLineToArgvW 轉義規則
                var sb = new StringBuilder();
                sb.Append('"');
                for (var i = 0; i < it.Length; i++)
                {
                    var backslashCount = 0;

                    // 統計連續的反斜線
                    while (i < it.Length && it[i] == '\\')
                    {
                        backslashCount++;
                        i++;
                    }
                    if (i == it.Length)
                    {
                        // 已經到了字串結尾。因為我們最後要補上一個 '"'，所以這裡的反斜線必須翻倍
                        sb.Append('\\', backslashCount * 2);
                        break;
                    }
                    if (it[i] == '"')
                    {
                        // 遇到雙引號。將前面的反斜線翻倍，再多加一個反斜線來跳脫雙引號
                        sb.Append('\\', backslashCount * 2 + 1);
                        sb.Append('"');
                    }
                    else
                    {
                        // 遇到一般字元。反斜線數量照舊，並補上該字元
                        sb.Append('\\', backslashCount);
                        sb.Append(it[i]);
                    }
                }
                sb.Append('"');
                return sb.ToString();
            });
            var joined = string.Join(" ", escapedArgs);
            if (string.IsNullOrWhiteSpace(joined))
                return;

            if (string.IsNullOrWhiteSpace(info.Arguments))
                info.Arguments = joined;
            else
                info.Arguments = info.Arguments.TrimEnd() + " " + joined;
#endif
        }
    }
}

namespace System.Net.Http
{
    internal static class HttpContentExtension
    {
#if !NET6_0_OR_GREATER
        public static async Task<Stream> ReadAsStreamAsync(this HttpContent content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (cancellationToken.Register(() => content.Dispose()))
            {
                try
                {
                    return await content.ReadAsStreamAsync();
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }
#endif
    }
}

namespace System.IO
{
    internal static class StreamReaderExtension
    {
#if !NET8_0_OR_GREATER
        public static async Task<string> ReadToEndAsync(this StreamReader reader, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (cancellationToken.Register(() => reader.Dispose()))
            {
                try
                {
                    return await reader.ReadToEndAsync();
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }
#endif
    }
}

namespace System.Net.Sockets
{
    internal static class TcpClientExtension
    {
#if !NET6_0_OR_GREATER
        public static async Task ConnectAsync(this TcpClient client, string host, int port, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (cancellationToken.Register(() => client.Dispose()))
            {
                try
                {
                    await client.ConnectAsync(host, port);
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }
#endif
    }

    internal static class TcpListenerExtension
    {
#if !NET6_0_OR_GREATER
        public static async Task<TcpClient> AcceptTcpClientAsync(this TcpListener listener, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (cancellationToken.Register(() => listener.Stop()))
            {
                try
                {
                    return await listener.AcceptTcpClientAsync();
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }
#endif
    }
}
