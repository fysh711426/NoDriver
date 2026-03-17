using NoDriver.Core.Runtime;

namespace Example
{
    public class ImgurUploadImage
    {
        private static readonly int DELAY = 2;

        public static async Task Main()
        {
            await using (var browser = await Browser.CreateAsync())
            {
                var tab = await browser.GetAsync("https://imgur.com");

                var savePath = "screenshot.jpg";
                var tempTab = await browser.GetAsync("https://github.com/ultrafunkamsterdam/undetected-chromedriver", newTab: true);

                await tempTab.WaitAsync();
                savePath = await tempTab.SaveScreenshotAsync(savePath);
                await tempTab.CloseAsync();

                var consent = await tab.FindAsync("Consent", bestMatch: true);
                if (consent == null)
                    throw new InvalidOperationException("Consent button is null.");
                await consent.ClickAsync();

                var newPost = await tab.FindAsync("new post", bestMatch: true);
                if (newPost == null)
                    throw new InvalidOperationException("NewPost button is null.");
                await newPost.ClickAsync();

                var fileInput = await tab.SelectAsync("input[type=file]");
                if (fileInput == null)
                    throw new InvalidOperationException("File input is null.");
                await fileInput.SendFileAsync(savePath);

                await tab.WaitAsync(DELAY);
                await tab.SelectAsync(".Toast-message--check");

                var titleField = await tab.FindAsync("give your post a unique title", bestMatch: true);
                if (titleField == null)
                    throw new InvalidOperationException("Title field is null.");

                Console.WriteLine($"{titleField}");
                await titleField.SendKeysAsync("undetected nodriver");

                var grabLink = await tab.FindAsync("grab a link", bestMatch: true);
                if (grabLink == null)
                    throw new InvalidOperationException("Grab link is null.");
                await grabLink.ClickAsync();

                await tab.WaitAsync(DELAY);

                var inputThing = await tab.SelectAsync("input[value^=https]");
                if (inputThing == null)
                    throw new InvalidOperationException("InputThing is null.");

                if (!inputThing.Attrs.TryGetValue("value", out var myLink))
                    throw new InvalidOperationException("MyLink is null.");

                Console.WriteLine($"{myLink}");
                await tab.WaitAsync();
            }
        }
    }
}
