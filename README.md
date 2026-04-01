# NoDriver  

This repo is a C# implementation of [nodriver](https://github.com/ultrafunkamsterdam/nodriver).  

It is an async web scraping and browser automation library. This package is optimized to stay undetected by most anti-bot solutions.  

---  

### Nuget install  

```
PM> Install-Package NoDriver
```

---  

### Simple  

```C#
await using (var browser = await Browser.CreateAsync())
{
    await browser.GetAsync("https://www.nowsecure.nl");

    // ...
}
```

### Using Config  

```C#
var config = new Config
{
    Headless = false,
    UserDataDir = @"C:\path\to\existing\profile",
    BrowserExecutablePath = @"C:\path\to\some\other\browser",
    Lang = "en-US"
};
config.AddArgument("--some-browser-arg=true");
config.AddArgument("--some-other-option");

await using (var browser = await Browser.CreateAsync(config))
{
    // ...
}
```

### Using Logger  

```C#
using (var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug)))
{
    var logger = loggerFactory.CreateLogger<Program>();
    var config = new Config(logger);
    await using (var browser = await Browser.CreateAsync(config))
    {
        // ...
    }
}
```

### Some Impression  

```C#
await using (var browser = await Browser.CreateAsync())
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
```

---  

More examples in the Example project.  