using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NoDriver.Core.Runtime
{
    internal class HTTPApi
    {
        private readonly string _apiBase;

        private static readonly HttpClient _httpClient = new();

        public HTTPApi(string host, int port)
        {
            _apiBase = $"http://{host}:{port}";
        }

        public async Task<JsonNode?> GetAsync(string endpoint, CancellationToken token = default)
            => await RequestAsync(endpoint, "GET", null, token);

        public async Task<JsonNode?> PostAsync(string endpoint, object? data = null, CancellationToken token = default)
            => await RequestAsync(endpoint, "POST", data, token);

        private async Task<JsonNode?> RequestAsync(
            string endpoint, string method = "GET", object? data = null, CancellationToken token = default)
        {
            endpoint = endpoint.TrimStart('/');

            var url = $"{_apiBase}/json";
            if (!string.IsNullOrWhiteSpace(endpoint))
                url = $"{_apiBase}/json/{endpoint}";

            method = method.ToUpperInvariant();
            if (data != null && method == "GET")
                throw new ArgumentException("GET requests cannot contain data.");

            using (var request = new HttpRequestMessage(new HttpMethod(method), url))
            {
                if (data != null)
                {
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
                }

                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                    {
                        try
                        {
                            using (var response = await _httpClient.SendAsync(request, cts.Token))
                            {
                                response.EnsureSuccessStatusCode();
                                using (var stream = await response.Content.ReadAsStreamAsync(cts.Token))
                                {
                                    return await JsonSerializer.DeserializeAsync<JsonNode>(stream, cancellationToken: cts.Token);
                                }
                            }
                        }
                        catch (OperationCanceledException ex)
                        {
                            if (timeoutCts.IsCancellationRequested)
                                throw new TimeoutException("The request timed out after 30 seconds.", ex);
                            throw;
                        }
                    }
                }
            }
        }
    }
}
