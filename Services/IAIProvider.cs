using System.Collections.Generic;
using System.Threading.Tasks;

namespace TagForge.Services
{
    public interface IAIProvider
    {
        string Name { get; }

        /// <summary>
        /// Validates the API Key and connection.
        /// </summary>
        Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Core generation logic.
        /// </summary>
        Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns list of models (if API supports it, otherwise return static list).
        /// </summary>
        Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates response token by token.
        /// </summary>
        IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Pre-loads a model if supported by the provider.
        /// </summary>
        Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default);
    }
}
