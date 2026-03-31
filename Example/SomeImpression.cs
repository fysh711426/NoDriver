using Microsoft.Extensions.Logging;
using NoDriver.Core.Runtime;

namespace Example
{
    public class SomeImpression
    {
        public static async Task Main()
        {
            using (var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug)))
            {
                var logger = loggerFactory.CreateLogger<SomeImpression>();

                var config = new Config(logger);
                await using (var browser = await Browser.CreateAsync(config))
                {
                    var page = await browser.GetAsync("https://www.nowsecure.nl");

                    await page.SaveScreenshotAsync();
                    await page.GetContentAsync();
                    await page.ScrollDownAsync(150);
                    var elems = await page.SelectAllAsync("*[src]");

                    foreach (var elem in elems)
                    {
                        await elem.FlashAsync();
                    }

                    var page2 = await browser.GetAsync("https://x.com", newTab: true);
                    var page3 = await browser.GetAsync("https://github.com/ultrafunkamsterdam/nodriver", newWindow: true);

                    var pages = new List<Tab>()
                    {
                        page, page2, page3
                    };

                    foreach (var p in pages)
                    {
                        await p.BringToFrontAsync();
                        await p.ScrollDownAsync(200);
                        await p.WaitAsync();
                        await p.ReloadAsync();
                        if (p != page3)
                            await p.CloseAsync();
                    }
                }
            }
        }
    }
}
