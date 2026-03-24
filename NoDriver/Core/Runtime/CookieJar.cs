using System.Text.Json;
using System.Text.RegularExpressions;

namespace NoDriver.Core.Runtime
{
    public class CookieJar
    {
        private readonly Browser _browser;

        public CookieJar(Browser browser)
        {
            _browser = browser;
        }

        /// <summary>
        /// Get all cookies.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<List<Cdp.Network.Cookie>> GetAllAsync(CancellationToken token = default)
        {
            var connection = GetActiveConnection();
            if (connection != null)
            {
                var result = await connection.SendAsync(Cdp.Storage.GetCookies(), token: token);
                var cookies = result.Cookies;
                return cookies.ToList();
            }
            return [];
        }

        /// <summary>
        /// Get all cookies.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<IEnumerable<System.Net.Cookie>> GetAllAsHttpCookiesAsync(CancellationToken token = default)
        {
            var cookies = await GetAllAsync(token);
            return cookies.Select(it => new System.Net.Cookie
            {
                Name = it.Name,
                Value = it.Value,
                Domain = it.Domain,
                Path = it.Path,
                Secure = it.Secure,
                // 注意：C# 的 Expires 是 DateTime 類型
                // 如果 it.Expires 是 Unix 時間戳，需要轉換
                Expires = it.Expires != null && it.Expires > 0 ?
                    DateTimeOffset.FromUnixTimeSeconds((long)it.Expires).UtcDateTime : DateTime.MinValue
            });
        }

        /// <summary>
        /// Set cookies.
        /// </summary>
        /// <param name="cookies">List of cookies.</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task SetAllAsync(List<Cdp.Network.CookieParam> cookies, CancellationToken token = default)
        {
            var connection = GetActiveConnection();
            if (connection != null)
                await connection.SendAsync(Cdp.Storage.SetCookies(cookies), token: token);
        }

        /// <summary>
        /// Save all cookies (or a subset, controlled by `pattern`) to a file to be restored later.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="pattern">Regex style pattern string.<br/>
        ///    Any cookie that has a  domain, key or value field which matches the pattern will be included.<br/>
        ///    default = ".*"  (all)<br/>
        /// <br/>
        ///    eg: the pattern "(cf|.com|nowsecure)" will include those cookies which:<br/>
        ///         - have a string "cf" (cloudflare)<br/>
        ///         - have ".com" in them, in either domain, key or value field.<br/>
        ///         - contain "nowsecure"</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task SaveAsync(string file = ".session.dat", string pattern = ".*", CancellationToken token = default)
        {
            var regex = new Regex(pattern);
            var cookies = await GetAllAsync(token);
            var includedCookies = cookies.Where(it => regex.IsMatch(JsonSerializer.Serialize(it))).ToList();

            var json = JsonSerializer.Serialize(includedCookies);
            await File.WriteAllTextAsync(file, json, token);
        }

        /// <summary>
        /// Load all cookies (or a subset, controlled by `pattern`) from a file created by `SaveAsync`.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="pattern">Regex style pattern string.<br/>
        ///    Any cookie that has a  domain, key or value field which matches the pattern will be included.<br/>
        ///    default = ".*"  (all)<br/>
        /// <br/>
        ///    eg: the pattern "(cf|.com|nowsecure)" will include those cookies which:<br/>
        ///         - have a string "cf" (cloudflare)<br/>
        ///         - have ".com" in them, in either domain, key or value field.<br/>
        ///         - contain "nowsecure"</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task LoadAsync(string file = ".session.dat", string pattern = ".*", CancellationToken token = default)
        {
            if (!File.Exists(file)) 
                return;

            var regex = new Regex(pattern);
            var json = await File.ReadAllTextAsync(file, token);
            var cookies = JsonSerializer.Deserialize<List<Cdp.Network.CookieParam>>(json);

            if (cookies != null)
            {
                var includedCookies = cookies.Where(it => regex.IsMatch(JsonSerializer.Serialize(it))).ToList();

                var connection = GetActiveConnection();
                if (connection != null)
                    await connection.SendAsync(Cdp.Storage.SetCookies(includedCookies), token: token);
            }
        }

        /// <summary>
        /// Clear current cookies.<br/>
        /// <br/>
        /// note: this includes all open tabs/windows for this browser
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task ClearAsync(CancellationToken token = default)
        {
            var connection = GetActiveConnection();
            if (connection != null)
                await connection.SendAsync(Cdp.Storage.ClearCookies(), token: token);
        }

        private Connection? GetActiveConnection()
        {
            var connection = _browser.Tabs.FirstOrDefault(it => !it.Closed) as Connection;
            if (connection == null)
                connection = _browser.Connection;
            return connection;
        }
    }
}
