using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RestSharp;
using Newtonsoft.Json;
using System.Linq;
using System.IO;
using System.Net.Http;

namespace TagForge.Services.Providers
{
    public class OllamaProvider : IAIProvider
    {
        public string Name => "Ollama";
        private const string DefaultBaseUrl = "http://localhost:11434/api/chat";

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? "http://localhost:11434/api/tags" : baseUrl.Replace("/chat", "/tags");
            logger?.Invoke("Ping Request", $"GET {targetUrl}", false);

            var options = new RestClientOptions(targetUrl);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Get);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            if (!response.IsSuccessful)
            {
                logger?.Invoke("Ping Failed", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);
                return false;
            }
            
            logger?.Invoke("Ping Success", "Connection verified.", true);
            return true;
        }

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, int maxTokens = 4096, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            var options = new RestClientOptions(targetUrl);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Content-Type", "application/json");

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }
            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model = model,
                messages = messages,
                stream = false,
                options = new { num_predict = maxTokens }
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.AddJsonBody(payload);

            logger?.Invoke("Generate Request", $"POST {targetUrl}\n{jsonBody}", false);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Generate Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful)
            {
                throw new Exception($"Ollama API Error: {response.Content}");
            }

            dynamic? json = JsonConvert.DeserializeObject(response.Content ?? "{}");
            string? text = json?.message?.content;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? "http://localhost:11434/api/tags" : baseUrl.Replace("/chat", "/tags");
            logger?.Invoke("Fetch Models Request", $"GET {targetUrl}", false);

            var options = new RestClientOptions(targetUrl);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Get);
            
            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Fetch Models Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful) return new List<string> { "llama3", "mistral", "phi3" };

            var models = new List<string>();
            try 
            {
                dynamic? json = JsonConvert.DeserializeObject(response.Content ?? "{}");
                if (json?.models != null)
                {
                    foreach (var m in json.models)
                    {
                        models.Add((string)m.name);
                    }
                }
            }
            catch {}
            
            return models.Count > 0 ? models : new List<string> { "llama3" };
        }

        public async IAsyncEnumerable<(string Token, bool IsReasoning)> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, int maxTokens = 4096, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            var client = NetworkService.Instance.Client;

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }
            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model = model,
                messages = messages,
                stream = true,
                options = new { num_predict = maxTokens }
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            logger?.Invoke("Stream Request", $"POST {targetUrl}\n{jsonBody}", false);

            var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                 var err = await response.Content.ReadAsStringAsync();
                 logger?.Invoke("Stream Failed", $"HTTP {(int)response.StatusCode}\n{err}", true);
                 throw new Exception($"Ollama API Error: {err}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            logger?.Invoke("Stream Started", "HTTP 200 OK - Streaming...", true);

            while (!reader.EndOfStream)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                string? content = null;
                bool isReasoning = false;
                try {
                    dynamic? json = JsonConvert.DeserializeObject(line);
                    content = json?.message?.content;
                    if (string.IsNullOrEmpty(content))
                    {
                        content = json?.message?.reasoning_content;
                        if (!string.IsNullOrEmpty(content)) isReasoning = true;
                    }
                } catch {}
                
                if (!string.IsNullOrEmpty(content)) yield return (content!, isReasoning);
            }
            logger?.Invoke("Stream Completed", "Stream finished successfully.", true);
        }

        public async Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
             var targetUrl = string.IsNullOrEmpty(baseUrl) ? "http://localhost:11434/api/generate" : baseUrl.Replace("/chat", "/generate");
             var options = new RestClientOptions(targetUrl);
             using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
             var request = new RestRequest("", Method.Post);
             request.AddJsonBody(new { model = model });
             await client.ExecuteAsync(request, cancellationToken);
        }
    }
}
