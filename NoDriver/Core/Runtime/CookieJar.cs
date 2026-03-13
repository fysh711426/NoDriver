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

        public async Task SetAllAsync(List<Cdp.Network.CookieParam> cookies, CancellationToken token = default)
        {
            var connection = GetActiveConnection();
            if (connection != null)
                await connection.SendAsync(Cdp.Storage.SetCookies(cookies), token: token);
        }

        public async Task SaveAsync(string file = ".session.dat", string pattern = ".*", CancellationToken token = default)
        {
            var regex = new Regex(pattern);
            var cookies = await GetAllAsync(token);
            var includedCookies = cookies.Where(it => regex.IsMatch(JsonSerializer.Serialize(it))).ToList();

            var json = JsonSerializer.Serialize(includedCookies);
            await File.WriteAllTextAsync(file, json, token);
        }

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
