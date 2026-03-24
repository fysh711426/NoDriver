using NoDriver.Core.Runtime;
using System.Text.Json;
using Cdp = NoDriver.Cdp;

namespace Test
{
    [TestClass]
    public class CookieJarTests
    {
        private static readonly string _testHtml =
            "data:text/html," +
            "<html><body>" +
            "  <div></div>" +
            "</body></html>";

        private Browser? _browser;
        private CookieJar? _cookies;

        [TestInitialize]
        public async Task Setup()
        {
            var config = new Config
            {
                Headless = true,
                AutodiscoverTargets = false
            };
            _browser = await Browser.CreateAsync(config);
            await _browser.MainTab!.GetAsync(_testHtml);
            await _browser.MainTab.WaitAsync(0.5);
            _cookies = _browser.Cookies;
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_browser != null)
                await _browser.DisposeAsync();
        }

        [TestMethod]
        public async Task SetAllAsync_And_GetAllAsync_ShouldAddAndRetrieveCookies()
        {
            // Arrange
            var cookieParam = new Cdp.Network.CookieParam
            (
                Name: "test_cookie",
                Value: "test_value",
                Domain: "example.com"
            );

            // Act
            await _cookies!.SetAllAsync(new List<Cdp.Network.CookieParam> { cookieParam });
            var result = await _cookies.GetAllAsync();

            // Assert
            Assert.AreEqual(1, result.Count, "應回傳剛設定的 Cookie");
            Assert.AreEqual("test_cookie", result.First().Name, "Name 不符合預期");
            Assert.AreEqual("test_value", result.First().Value, "Value 不符合預期");
            Assert.AreEqual("example.com", result.First().Domain, "Domain 不符合預期");
        }

        [TestMethod]
        public async Task ClearAsync_ShouldRemoveAllCookies()
        {
            // Arrange
            var cookieParam = new Cdp.Network.CookieParam
            (
                Name: "cookie_to_clear",
                Value: "test_value",
                Domain: "example.com"
            );
            await _cookies!.SetAllAsync(new List<Cdp.Network.CookieParam> { cookieParam });

            // Act
            await _cookies.ClearAsync();
            var result = await _cookies.GetAllAsync();

            // Assert
            Assert.IsFalse(result.Any(it => it.Name == "cookie_to_clear"), "清除後，該 cookie 應不再存在");
        }

        [TestMethod]
        public async Task SaveAsync_And_LoadAsync_ShouldPersistAndRestoreCookies()
        {
            // Arrange
            var path = Path.Combine(AppContext.BaseDirectory, "test_session.dat");

            var cookieParam = new Cdp.Network.CookieParam
            (
                Name: "test_cookie",
                Value: "test_value",
                Domain: "example.com"
            );
            await _cookies!.SetAllAsync(new List<Cdp.Network.CookieParam> { cookieParam });

            // Act: 驗證載入功能
            await _cookies.SaveAsync(path);
            var json = await File.ReadAllTextAsync(path);

            // Assert
            Assert.IsTrue(File.Exists(path), "儲存後，檔案應存在");
            Assert.IsTrue(json.Contains("test_cookie"), "應包含 Name");
            Assert.IsTrue(json.Contains("test_value"), "應包含 Value");
            Assert.IsTrue(json.Contains("example.com"), "應包含 Domain");

            // Act: 驗證載入功能
            await _cookies.ClearAsync();
            await _cookies.LoadAsync(path);
            var result = await _cookies.GetAllAsync();

            // Assert
            Assert.AreEqual(1, result.Count, "應回傳剛載入的 Cookie");
            Assert.AreEqual("test_cookie", result.First().Name, "Name 不符合預期");
            Assert.AreEqual("test_value", result.First().Value, "Value 不符合預期");
            Assert.AreEqual("example.com", result.First().Domain, "Domain 不符合預期");

            // Cleanup
            if (File.Exists(path))
                File.Delete(path);
        }

        [TestMethod]
        public async Task SaveAsync_WithRegexPattern_ShouldOnlySaveMatchingCookies()
        {
            // Arrange
            var path = Path.Combine(AppContext.BaseDirectory, "test_save_regex.dat");

            var targetCookie = new Cdp.Network.CookieParam
            (
                Name: "target_cookie",
                Value: "value1",
                Domain: "target-domain.com"
            );
            var ignoredCookie = new Cdp.Network.CookieParam
            (
                Name: "ignored_cookie",
                Value: "value2",
                Domain: "ignored-domain.com"
            );
            await _cookies!.SetAllAsync(new List<Cdp.Network.CookieParam> { targetCookie, ignoredCookie });

            // Act: 儲存時使用 Regex 過濾
            await _cookies.SaveAsync(path, pattern: @"target-domain\.com");

            // Assert
            var json = await File.ReadAllTextAsync(path);
            var result = JsonSerializer.Deserialize<List<Cdp.Network.CookieParam>>(json);

            Assert.AreEqual(1, result!.Count, "檔案應只包含一個符合的 cookie");
            Assert.AreEqual("target_cookie", result.First().Name, "Name 應為 target_cookie");

            // Cleanup
            if (File.Exists(path))
                File.Delete(path);
        }

        [TestMethod]
        public async Task LoadAsync_WithRegexPattern_ShouldOnlyLoadMatchingCookies()
        {
            // Arrange
            var path = Path.Combine(AppContext.BaseDirectory, "test_load_regex.dat");

            var targetCookie = new Cdp.Network.CookieParam
            (
                Name: "target_cookie",
                Value: "value1",
                Domain: "target-domain.com"
            );
            var ignoredCookie = new Cdp.Network.CookieParam
            (
                Name: "ignored_cookie",
                Value: "value2",
                Domain: "ignored-domain.com"
            );
            await _cookies!.SetAllAsync(new List<Cdp.Network.CookieParam> { targetCookie, ignoredCookie });
            await _cookies.SaveAsync(path, pattern: ".*");
            await _cookies.ClearAsync();

            // Act: 讀取時使用 Regex 過濾
            await _cookies.LoadAsync(path, pattern: "target_cookie");

            // Assert
            var result = await _cookies.GetAllAsync();

            Assert.AreEqual(1, result.Count, "瀏覽器應只載入一個符合的 cookie");
            Assert.AreEqual("target_cookie", result.First().Name, "Name 應為 target_cookie");

            // Cleanup
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
