using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RestSharp;
using Newtonsoft.Json;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace TagForge.Services.Providers
{
    public class GeminiProvider : IAIProvider
    {
        public string Name => "Gemini";
        private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = "https://generativelanguage.googleapis.com/v1beta/models?key=" + apiKey;
            logger?.Invoke("Ping Request", $"GET {url}", false);

            var options = new RestClientOptions(url);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Get);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            if (!response.IsSuccessful)
            {
                logger?.Invoke("Ping Failed", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);
                throw new Exception($"Gemini API Error: {response.Content}");
            }
            
            logger?.Invoke("Ping Success", "Connection verified.", true);
            return true;
        }

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, int maxTokens = 4096, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var modelId = string.IsNullOrEmpty(model) ? "gemini-1.5-flash" : model;
            var url = (string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl) + modelId + ":generateContent?key=" + apiKey;
            
            var options = new RestClientOptions(url);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Content-Type", "application/json");

            var contents = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                // Gemini doesn't always handle 'system' role in generateContent the same way as OpenAI
                // Newer models support system_instruction in the payload
            }

            var parts = new List<object>();
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                string base64Image = Convert.ToBase64String(imageBytes);
                string mimeType = "image/jpeg";
                if (imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) mimeType = "image/png";
                if (imagePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) mimeType = "image/webp";

                parts.Add(new { inline_data = new { mime_type = mimeType, data = base64Image } });
            }
            parts.Add(new { text = userPrompt });

            contents.Add(new { role = "user", parts = parts });

            var payload = new
            {
                contents = contents,
                generationConfig = new { maxOutputTokens = maxTokens },
                system_instruction = string.IsNullOrEmpty(systemPrompt) ? null : new { parts = new[] { new { text = systemPrompt } } }
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.AddJsonBody(payload);

            if (jsonBody.Length > 2000 && jsonBody.Contains("inline_data"))
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody.Substring(0, 1000)} ... [IMAGE DATA] ...", false);
            else
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody}", false);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Generate Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful)
            {
                throw new Exception($"Gemini API Error: {response.Content}");
            }

            dynamic? json = JsonConvert.DeserializeObject(response.Content ?? "{}");
            string? text = json?.candidates?[0]?.content?.parts?[0]?.text;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            return new List<string> { "gemini-1.5-flash", "gemini-1.5-pro", "gemini-pro", "gemini-pro-vision" };
        }

        public async IAsyncEnumerable<(string Token, bool IsReasoning)> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, int maxTokens = 4096, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var modelId = string.IsNullOrEmpty(model) ? "gemini-1.5-flash" : model;
            var url = (string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl) + modelId + ":streamGenerateContent?alt=sse&key=" + apiKey;
            
            var client = NetworkService.Instance.Client;
            var parts = new List<object>();
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                string base64Image = Convert.ToBase64String(imageBytes);
                string mimeType = "image/jpeg";
                parts.Add(new { inline_data = new { mime_type = mimeType, data = base64Image } });
            }
            parts.Add(new { text = userPrompt });

            var payload = new
            {
                contents = new[] { new { role = "user", parts = parts } },
                generationConfig = new { maxOutputTokens = maxTokens },
                system_instruction = string.IsNullOrEmpty(systemPrompt) ? null : new { parts = new[] { new { text = systemPrompt } } }
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            logger?.Invoke("Stream Request", $"POST {url}\n{jsonBody}", false);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                 var err = await response.Content.ReadAsStringAsync();
                 logger?.Invoke("Stream Failed", $"HTTP {(int)response.StatusCode}\n{err}", true);
                 throw new Exception($"Gemini API Error: {err}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6).Trim();
                    string? content = null;
                    bool isReasoning = false;
                    try {
                        dynamic? json = JsonConvert.DeserializeObject(data);
                        content = json?.candidates?[0]?.content?.parts?[0]?.text;
                        if (string.IsNullOrEmpty(content))
                        {
                            content = json?.candidates?[0]?.content?.parts?[0]?.thought;
                            if (!string.IsNullOrEmpty(content)) isReasoning = true;
                        }
                    } catch {}
                    
                    if (!string.IsNullOrEmpty(content)) yield return (content!, isReasoning);
                }
            }
        }
        public Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
