namespace NoDriver.Core.Runtime
{
    public class BrowserContext : IAsyncDisposable
    {
        private readonly Config _config;
        private readonly bool _keepOpen;

        private Browser? _instance;

        public BrowserContext(Config config, bool keepOpen = false)
        {
            _config = config;
            _keepOpen = keepOpen;
        }

        public async Task<Browser> GetBrowserAsync()
        {
            if (_instance == null)
                _instance = await Browser.CreateAsync(_config);
            return _instance;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_keepOpen)
            {
                _instance.Stop();
                await Task.CompletedTask; // 模擬 Util.deconstruct_browser
                //if not self._keep_open:
                //    await util.deconstruct_browser(self._instance)
            }
        }
    }
}
