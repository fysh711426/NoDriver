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

        public async Task<List<Cdp.Network.Cookie>> GetAllAsync()
        {
            var connection = _browser.Tabs.FirstOrDefault(it => !it.Closed) as Connection;
            if (connection == null)
                connection = _browser.Connection;

            if (connection != null)
            {
                var result = await connection.SendAsync(Cdp.Storage.GetCookies());
                var cookies = result.Cookies;
                return cookies.ToList();
            }
            return new();
        }

        public async Task SetAllAsync(List<Cdp.Network.CookieParam> cookies)
        {
            var connection = _browser.Tabs.FirstOrDefault(it => !it.Closed) as Connection;
            if (connection == null)
                connection = _browser.Connection;

            if (connection != null)
                await connection.SendAsync(Cdp.Storage.SetCookies(cookies));
        }

        public async Task SaveAsync(string file = ".session.dat", string pattern = ".*")
        {
            var regex = new Regex(pattern);
            var cookies = await GetAllAsync();
            var includedCookies = cookies.Where(it => regex.IsMatch(JsonSerializer.Serialize(it))).ToList();

            var json = JsonSerializer.Serialize(includedCookies);
            await File.WriteAllTextAsync(file, json);
        }

        public async Task LoadAsync(string file = ".session.dat", string pattern = ".*")
        {
            if (!File.Exists(file)) 
                return;

            var regex = new Regex(pattern);
            var json = await File.ReadAllTextAsync(file);
            var cookies = JsonSerializer.Deserialize<List<Cdp.Network.CookieParam>>(json);

            if (cookies != null)
            {
                var includedCookies = cookies.Where(it => regex.IsMatch(JsonSerializer.Serialize(it))).ToList();

                var connection = _browser.Tabs.FirstOrDefault(it => !it.Closed) as Connection;
                if (connection == null)
                    connection = _browser.Connection;

                if (connection != null)
                    await connection.SendAsync(Cdp.Storage.SetCookies(includedCookies));
            }
        }

        public async Task ClearAsync()
        {
            var connection = _browser.Tabs.FirstOrDefault(it => !it.Closed) as Connection;
            if (connection == null)
                connection = _browser.Connection;

            if (connection != null)
                await connection.SendAsync(Cdp.Storage.ClearCookies());
        }
    }
}
