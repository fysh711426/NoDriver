namespace NoDriver.Core.Runtime
{
    public class BrowserContext : IDisposable, IAsyncDisposable
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
                if (_instance != null)
                    await _instance.DisposeAsync();
            }
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_keepOpen)
                {
                    _instance?.Dispose();
                }
            }
        }
    }
}
