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
        Task<bool> PingAsync(string apiKey, string baseUrl);

        /// <summary>
        /// Core generation logic.
        /// </summary>
        Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl);

        /// <summary>
        /// Returns list of models (if API supports it, otherwise return static list).
        /// </summary>
        Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl);

        /// <summary>
        /// Generates response token by token.
        /// </summary>
        IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl);
    }
}
