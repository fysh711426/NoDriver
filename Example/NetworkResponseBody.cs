using NoDriver.Core.Messaging;
using NoDriver.Core.Runtime;
using System.Text;
using Cdp = NoDriver.Cdp;

namespace Example
{
    public class NetworkResponseBody
    {
        public static async Task Main()
        {
            await using (var browser = await Browser.CreateAsync())
            {
                if (browser.MainTab == null)
                    throw new InvalidOperationException("MainTab is null.");

                async Task responseReceivedHandler(Cdp.Network.ResponseReceived e, Tab tab)
                {
                    try
                    {
                        var result = await tab.SendAsync(Cdp.Network.GetResponseBody(e.RequestId));
                        if (string.IsNullOrWhiteSpace(result.Body))
                            return;

                        var body = result.Body;
                        if (result.Base64Encoded)
                            body = Encoding.UTF8.GetString(Convert.FromBase64String(body));

                        Console.WriteLine($"{e.Response.Url}\nbody[:100]:\n{body.Take(100)}");
                    }
                    catch (ProtocolErrorException) { }
                };

                browser.MainTab.AddHandler<Cdp.Network.ResponseReceived>(responseReceivedHandler);
                while (true)
                {
                    var tab = await browser.MainTab.GetAsync("https://google.com");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        }
    }
}
