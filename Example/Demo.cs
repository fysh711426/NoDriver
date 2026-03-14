using NoDriver.Core.Runtime;
using NoDriver.Core.Tools;
using System.Diagnostics;

namespace Example
{
    public class Timing : IDisposable
    {
        private readonly Stopwatch _stopwatch;

        public Timing()
        {
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            Console.WriteLine($"Taken: {_stopwatch.Elapsed.TotalSeconds} seconds.");
        }
    }

    public class Demo
    {
        public static async Task Main()
        {
            using (var t = new Timing())
            {
                await Run();
            }
        }

        private static async Task Run()
        {
            var resolution = ScreenHelper.GetResolution();

            var SCREEN_WIDTH = resolution.Width;
            var NUM_WINS = SCREEN_WIDTH / 325;

            await using (var browser = await Browser.CreateAsync())
            {
                var url1 = "https://www.bet365.com";
                var url2 = "https://www.nowsecure.nl";

                Console.WriteLine("The startup speed of the windows depend on the line speed and availability/load on the servers.");

                await browser.GetAsync(url2);

                for (int i = 0; i < NUM_WINS; i++)
                {
                    if (i % 2 == 0)
                        await browser.GetAsync(url1, newWindow: true);
                    else
                        await browser.GetAsync(url2, newWindow: true);
                }

                await browser.WaitAsync();

                var grid = await browser.TileWindowsAsync(maxColumns: NUM_WINS);
                if (grid == null)
                    throw new Exception("Tile windows failed.");

                await browser.WaitAsync(5);
                foreach (var tab in browser.Tabs)
                {
                    await tab.MaximizeAsync();
                }

                var random = new Random();
                for (var i = 0; i < 15; i++)
                {
                    foreach (var tab in browser.Tabs)
                    {
                        var randomBox = grid[random.Next(grid.Count)];

                        await tab.ActivateAsync();
                        await tab.SetWindowSizeAsync(
                            randomBox.Left, randomBox.Top, randomBox.Width, randomBox.Height);
                    }
                }

                await browser.WaitAsync();

                {
                    var tasks = new List<Task>();
                    for (var i = 0; i < browser.Tabs.Count; i++)
                    {
                        var tab = browser.Tabs[i];
                        tasks.Add(MoveCircle(tab, i % 2));
                    }
                    await Task.WhenAll(tasks);
                }
                {
                    var nowsecurePages = browser.Tabs.Where(
                        it => it.Target?.Url?.Contains("nowsecure") ?? false).ToList();

                    var tasks = new List<Task>();
                    foreach (var tab in nowsecurePages)
                    {
                        tasks.Add(tab.GetAsync("https://nowsecure.nl/mouse.html"));
                    }
                    await Task.WhenAll(tasks);

                    await browser.TileWindowsAsync(maxColumns: NUM_WINS);

                    tasks.Clear();
                    foreach (var tab in nowsecurePages)
                    {
                        tasks.Add(MouseMove(tab));
                    }
                    await Task.WhenAll(tasks);
                }
                {
                    var b365Pages = browser.Tabs.Where(
                        it => it.Target?.Url?.Contains("bet365") ?? false).ToList();

                    await browser.WaitAsync(1);

                    await browser.TileWindowsAsync(b365Pages);

                    var tasks = new List<Task>();
                    for (var i = 0; i < b365Pages.Count; i++)
                    {
                        var tab = b365Pages[i];
                        tasks.Add(FlashSpans(tab, i));
                    }
                    await Task.WhenAll(tasks);

                    tasks.Clear();
                    foreach (var tab in b365Pages)
                    {
                        tasks.Add(ScrollTask(tab));
                    }
                    await Task.WhenAll(tasks);
                }

                foreach (var tab in browser.Tabs)
                {
                    try
                    {
                        await tab.GetAsync("https://www.google.com");
                        await tab.ActivateAsync();

                        if (tab == browser.MainTab)
                        {
                            Console.WriteLine("Skipping main tab.");
                            continue;
                        }
                    }
                    catch { }
                    await tab.CloseAsync();
                }

                for (var i = 0; i < browser.Tabs.Count; i++)
                {
                    var tab = browser.Tabs[i];
                    try
                    {
                        if (i == 0)
                            continue;
                        await tab.ActivateAsync();
                        await tab.CloseAsync();
                    }
                    catch { }
                }
            }
        }

        private static async Task ScrollTask(Tab tab)
        {
            await tab.ScrollUpAsync(200);

            var spans = await tab.SelectAllAsync("span");
            var reversed = Enumerable.Reverse(spans);

            foreach (var s in reversed)
            {
                await s.ScrollIntoViewAsync();
                await s.FlashAsync();
            }

            for (var n = 0; n < 75; n += 15)
            {
                await tab.ScrollUpAsync(n / 2);
                await tab.ScrollDownAsync(n);

                Console.WriteLine($"Tab {tab} scrolling down: {n}");
            }
        }

        private static async Task MouseMove(Tab tab)
        {
            await tab.ActivateAsync();
            var boxes = await tab.SelectAllAsync(".box");

            foreach (var box in boxes)
            {
                await box.MouseMoveAsync();
            }

            foreach (var box in boxes)
            {
                await box.MouseDragAsync((250, 250), relative: true);
            }
        }

        private static async Task MoveCircle(Tab tab, int x = 0)
        {
            var result = await tab.GetWindowAsync();
            if (result == null)
                return;
            var (windowId, bounds) = result.Value;

            var oldLeft = bounds.Left ?? 0;
            var oldTop = bounds.Top ?? 0;
            var oldWidth = bounds.Width ?? 0;
            var oldHeight = bounds.Height ?? 0;

            var center = (x: oldLeft, y: oldTop);

            {
                var coords = Util.Circle(center.x, center.y, radius: 100, num: 1050, dir: x);
                foreach (var coord in coords)
                {
                    var newLeft = (int)coord.X;
                    var newTop = (int)coord.Y;
                    await tab.SetWindowSizeAsync(newLeft, newTop);
                }
            }
            {
                var coords = Util.Circle(center.x, center.y, radius: 250, num: 500, dir: x);
                foreach (var coord in coords)
                {
                    var newLeft = (int)(coord.X / 2);
                    var newTop = (int)(coord.Y / 2);
                    await tab.SetWindowSizeAsync(newLeft, newTop);
                }
            }

            await tab.SetWindowSizeAsync(oldLeft, oldTop, oldWidth, oldHeight);
        }

        private static async Task FlashSpans(Tab tab, int i)
        {
            Console.WriteLine($"Flashing spans. i={i} , tab={tab}, url={tab.Target?.Url ?? ""}");

            var elems = await tab.SelectAllAsync("span");

            await tab.ActivateAsync();

            foreach (var elem in elems)
            {
                await elem.FlashAsync(0.25);
                await elem.ScrollIntoViewAsync();
            }
        }
    }
}
