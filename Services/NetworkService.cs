using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace TagForge.Services
{
    public sealed class NetworkService : IDisposable
    {
        private static readonly Lazy<NetworkService> _instance = new(() => new NetworkService());
        public static NetworkService Instance => _instance.Value;

        public HttpClient Client { get; }

        private NetworkService()
        {
            var handler = new HttpClientHandler
            {
                // You can configure proxy or other handler settings here
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            Client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5) // Long timeout for large image generation/processing
            };

            // Common headers
            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            Client.DefaultRequestHeaders.UserAgent.ParseAdd("TagForge/1.0");
        }

        public void Dispose()
        {
            Client.Dispose();
        }
    }
}
