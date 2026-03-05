using NoDriver.Core.Runtime;

namespace Example
{
    public class MouseDragBoxes
    {
        public static async Task Main()
        {
            await using (var browser = await Browser.CreateAsync())
            {
                await DemoDragToTarget(browser);
                await DemoDragToTargetInSteps(browser);
                await DemoDragToAbsolutePosition(browser);
                await DemoDragToAbsolutePositionInSteps(browser);
                await DemoDragToRelativePosition(browser);
                await DemoDragToRelativePositionInSteps(browser);
            }
        }

        private static async Task DemoDragToTarget(Browser browser)
        {
            var tab = await browser.GetAsync("https://nowsecure.nl/mouse.html?boxes=50");
            var boxes = await tab.SelectAllAsync(".box");

            var area = await tab.SelectAsync(".area-a");
            if (area == null)
                throw new InvalidOperationException("Area is null.");

            foreach (var box in boxes)
            {
                await box.MouseDragAsync(area);
            }
        }

        private static async Task DemoDragToTargetInSteps(Browser browser)
        {
            var tab = await browser.GetAsync("https://nowsecure.nl/mouse.html");
            var boxes = await tab.SelectAllAsync(".box");

            var area = await tab.SelectAsync(".area-a");
            if (area == null)
                throw new InvalidOperationException("Area is null.");

            foreach (var box in boxes)
            {
                await box.MouseDragAsync(area, steps: 100);
            }
        }

        private static async Task DemoDragToAbsolutePosition(Browser browser)
        {
            var tab = await browser.GetAsync("https://nowsecure.nl/mouse.html?boxes=50");
            var boxes = await tab.SelectAllAsync(".box");

            var area = await tab.SelectAsync(".area-a");
            if (area == null)
                throw new InvalidOperationException("Area is null.");

            foreach (var box in boxes)
            {
                await box.MouseDragAsync((500, 500));
            }
        }

        private static async Task DemoDragToAbsolutePositionInSteps(Browser browser)
        {
            var tab = await browser.GetAsync("https://nowsecure.nl/mouse.html");
            var boxes = await tab.SelectAllAsync(".box");

            var area = await tab.SelectAsync(".area-a");
            if (area == null)
                throw new InvalidOperationException("Area is null.");

            foreach (var box in boxes)
            {
                await box.MouseDragAsync((500, 500), steps: 50);
            }
        }

        private static async Task DemoDragToRelativePosition(Browser browser)
        {
            var tab = await browser.GetAsync("https://nowsecure.nl/mouse.html?boxes=50");
            var boxes = await tab.SelectAllAsync(".box");

            foreach (var box in boxes)
            {
                await box.MouseDragAsync((500, 500), relative: true);
            }
        }

        private static async Task DemoDragToRelativePositionInSteps(Browser browser)
        {
            var tab = await browser.GetAsync("https://nowsecure.nl/mouse.html");
            var boxes = await tab.SelectAllAsync(".box");

            foreach (var box in boxes)
            {
                await box.MouseDragAsync((500, 500), relative: true, steps: 50);
            }
        }
    }
}
