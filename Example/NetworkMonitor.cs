using NoDriver.Core.Runtime;
using Cdp = NoDriver.Cdp;

namespace Example
{
    public class NetworkMonitor
    {
        public static async Task Main()
        {
            await using (var browser = await Browser.CreateAsync())
            {
                if (browser.MainTab == null)
                    throw new InvalidOperationException("MainTab is null.");

                void receiveHandler(Cdp.Network.ResponseReceived e, Connection _)
                {
                    Console.WriteLine(e.Response);
                }

                void sendHandler(Cdp.Network.RequestWillBeSent e, Connection _)
                {
                    var r = e.Request;
                    var s = $"{r.Method} {r.Url}";

                    foreach (var prop in r.Headers.Properties)
                    {
                        var value = "";
                        if (prop.Value != null)
                            value = prop.Value.ToString();

                        s += $"\n\t{prop.Key} : {value}";
                    }
                    Console.WriteLine(s);
                }

                browser.MainTab.AddHandler<Cdp.Network.RequestWillBeSent>(sendHandler);
                browser.MainTab.AddHandler<Cdp.Network.ResponseReceived>(receiveHandler);

                var tab = await browser.GetAsync("https://www.google.com/?hl=en");

                var rejectBtn = await tab.FindAsync("reject all", bestMatch: true);
                if (rejectBtn != null)
                    await rejectBtn.ClickAsync();

                var searchInp = await tab.SelectAsync("textarea");
                if (searchInp != null)
                    await searchInp.SendKeysAsync("undetected nodriver");

                var searchBtn = await tab.FindAsync("google search", bestMatch: true);
                if (searchBtn != null)
                    await searchBtn.ClickAsync();

                for (int i = 0; i < 10; i++)
                {
                    await tab.ScrollDownAsync(50);
                }

                await tab.WaitAsync();
                await tab.BackAsync();

                searchInp = await tab.SelectAsync("textarea");
                if (searchInp != null)
                {
                    var text = "undetected nodriver";
                    foreach (var letter in text)
                    {
                        await searchInp.ClearInputAsync();
                        await searchInp.SendKeysAsync(
                            text.Replace(letter.ToString(), letter.ToString().ToUpperInvariant()));
                        await tab.WaitAsync(0.1);
                    }
                }

                var allUrls = await tab.GetAllUrlsAsync();
                foreach (var u in allUrls)
                {
                    Console.WriteLine($"Downloading {u}");
                    await tab.DownloadFileAsync(u);
                }
                await tab.WaitAsync(10);
            }
        }
    }
}
