using NoDriver.Core.Runtime;
using Cdp = NoDriver.Cdp;

namespace Example
{
    public class FetchDomain
    {
        public static async Task Main()
        {
            await using (var browser = await Browser.CreateAsync())
            {
                for (var i = 0; i < 10; i++)
                {
                    await browser.GetAsync("https://www.google.com", newWindow: true);
                }

                async Task requestHandler(Cdp.Fetch.RequestPaused e, Connection conn)
                {
                    if (conn is Tab tab)
                    {
                        Console.WriteLine("\nRequestPaused handler");
                        Console.WriteLine($"Event: {e.GetType().FullName}");
                        Console.WriteLine($"TAB = {tab.ToString()})");

                        await tab.FeedCdpAsync(Cdp.Fetch.ContinueRequest(RequestId: e.RequestId));
                    }
                }

                foreach (var tab in browser.Tabs)
                {
                    Console.WriteLine(tab.ToString());
                    tab.AddHandler<Cdp.Fetch.RequestPaused>(requestHandler);
                    await tab.SendAsync(Cdp.Fetch.Enable());
                }

                foreach (var tab in browser.Tabs)
                {
                    await tab.WaitAsync();
                }

                foreach (var tab in browser.Tabs)
                {
                    await tab.ActivateAsync();
                }

                foreach (var tab in Enumerable.Reverse(browser.Tabs))
                {
                    await tab.ActivateAsync();
                    await tab.CloseAsync();
                }
            }
        }
    }
}
