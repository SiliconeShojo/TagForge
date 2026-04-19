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
        public string Name => "Ollama (Local)";
        private const string DefaultBaseUrl = "http://localhost:11434/api/chat";
        private const string TagsUrl = "http://localhost:11434/api/tags";

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = string.IsNullOrEmpty(baseUrl) ? TagsUrl : baseUrl.Replace("/chat", "/tags");
            logger?.Invoke("Ping Request", $"GET {url}", false);

            using var client = new RestClient(url);
            var request = new RestRequest("", Method.Get);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            if (!response.IsSuccessful)
            {
                logger?.Invoke("Ping Failed", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

                string errorMessage = "Unknown error";
                try
                {
                    dynamic json = JsonConvert.DeserializeObject(response.Content);
                    errorMessage = json?.error?.message ?? response.Content?.Substring(0, Math.Min(100, response.Content?.Length ?? 0));
                }
                catch
                {
                    errorMessage = response.Content?.Substring(0, Math.Min(100, response.Content?.Length ?? 0)) ?? "Unknown error";
                }
                
                var errorMsg = $"HTTP {(int)response.StatusCode} {response.StatusCode}: {errorMessage}";
                throw new Exception(errorMsg);
            }
            
            logger?.Invoke("Ping Success", "Connection verified.", true);
            return true;
        }

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            using var client = new RestClient(url);
            var request = new RestRequest("", Method.Post);
            
            // Ollama specific payload
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                string base64Image = Convert.ToBase64String(imageBytes);
                
                // Ollama expects "images": ["base64"] within the message object
                messages.Add(new 
                { 
                    role = "user", 
                    content = userPrompt,
                    images = new[] { base64Image }
                });
            }
            else
            {
                 messages.Add(new { role = "user", content = userPrompt });
            }

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "llama3" : model,
                messages = messages,
                stream = false
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.AddJsonBody(payload);

            if (jsonBody.Length > 2000 && jsonBody.Contains("images"))
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody.Substring(0, 500)} ... [IMAGE DATA] ...", false);
            else
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody}", false);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Generate Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful)
            {
                throw new Exception($"Ollama API Error: {response.Content}");
            }

            dynamic json = JsonConvert.DeserializeObject(response.Content);
            string text = json?.message?.content;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = string.IsNullOrEmpty(baseUrl) ? TagsUrl : baseUrl.Replace("/chat", "/tags");
            if (url.EndsWith("/api")) url += "/tags"; // basic heuristic correction

            logger?.Invoke("Fetch Models Request", $"GET {url}", false);

            using var client = new RestClient(url);
            var request = new RestRequest("", Method.Get);
            
            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Fetch Models Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            var models = new List<string>();

            if (response.IsSuccessful)
            {
                try
                {
                    dynamic json = JsonConvert.DeserializeObject(response.Content);
                    if (json?.models != null)
                    {
                        foreach (var m in json.models)
                        {
                            models.Add((string)m.name);
                        }
                    }
                }
                catch { }
            }
            
            return models.Count > 0 ? models : new List<string> { "llama3", "mistral", "gemma" };
        }

        public async IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            using var client = new HttpClient();

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }
            
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                string base64Image = Convert.ToBase64String(imageBytes);
                
                messages.Add(new 
                { 
                    role = "user", 
                    content = userPrompt,
                    images = new[] { base64Image }
                });
            }
            else
            {
                messages.Add(new { role = "user", content = userPrompt });
            }

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "llama3" : model,
                messages = messages,
                stream = true
            };
            
            var jsonBody = JsonConvert.SerializeObject(payload);
            if (jsonBody.Length > 2000 && jsonBody.Contains("images"))
                 logger?.Invoke("Stream Request", $"POST {url}\n{jsonBody.Substring(0, 1000)} ... [IMAGE DATA] ...", false);
            else
                 logger?.Invoke("Stream Request", $"POST {url}\n{jsonBody}", false);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
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
                 
                 string content = null;
                 bool done = false;
                 try 
                 {
                     dynamic json = JsonConvert.DeserializeObject(line);
                     content = json?.message?.content;
                     if (json?.done == true) done = true;
                 }
                 catch {}
                 
                 if (!string.IsNullOrEmpty(content)) yield return content;
                 if (done) break;
            }
            logger?.Invoke("Stream Completed", "Stream finished successfully.", true);
        }
        public async Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            // Ollama loads model on first request or via specific endpoint. 
            // We can send an empty keep-alive request to force load.
            var url = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            using var client = new RestClient(url);
            var request = new RestRequest("", Method.Post);

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "llama3" : model,
                keep_alive = -1 // Load and keep in memory
            };

            request.AddJsonBody(payload);
            try 
            {
                // We don't care about the response much, just triggering the load
                await client.ExecuteAsync(request, cancellationToken);
            }
            catch { /* Best effort */ }
        }
    }
}
