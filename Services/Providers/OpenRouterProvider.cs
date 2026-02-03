using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RestSharp;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;

namespace TagForge.Services.Providers
{
    public class OpenRouterProvider : IAIProvider
    {
        public string Name => "OpenRouter";
        private const string DefaultBaseUrl = "https://openrouter.ai/api/v1/chat/completions";
        
        // OpenRouter doesn't have a standard models endpoint in the OpenAI path usually, 
        // but often supports /models at the root API level or via the openai-compatible route.
        // https://openrouter.ai/api/v1/models is the correct one.
        private const string ModelsUrl = "https://openrouter.ai/api/v1/models";

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            logger?.Invoke("Ping Request", $"GET {ModelsUrl}", false);

            using var client = new RestClient(ModelsUrl);
            var request = new RestRequest("", Method.Get);
            request.AddHeader("Authorization", $"Bearer {apiKey}");

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
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            using var client = new RestClient(targetUrl);
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Authorization", $"Bearer {apiKey}");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("HTTP-Referer", "https://github.com/TagForge/TagForge"); 
            request.AddHeader("X-Title", "TagForge");

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                string base64Image = Convert.ToBase64String(imageBytes);
                string mimeType = "image/jpeg"; // Default
                if (imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) mimeType = "image/png";
                if (imagePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) mimeType = "image/webp";

                messages.Add(new 
                { 
                    role = "user", 
                    content = new object[] 
                    {
                        new { type = "text", text = userPrompt },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Image}" } }
                    }
                });
            }
            else
            {
                messages.Add(new { role = "user", content = userPrompt });
            }

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "openai/gpt-3.5-turbo" : model,
                messages = messages
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.AddJsonBody(payload);

            if (jsonBody.Length > 2000 && jsonBody.Contains("image_url"))
                logger?.Invoke("Generate Request", $"POST {targetUrl}\n{jsonBody.Substring(0, 500)} ... [IMAGE DATA] ...", false);
            else
                logger?.Invoke("Generate Request", $"POST {targetUrl}\n{jsonBody}", false);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Generate Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful)
            {
                throw new Exception($"OpenRouter API Error: {response.Content}");
            }

            dynamic json = JsonConvert.DeserializeObject(response.Content);
            string text = json?.choices?[0]?.message?.content;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            logger?.Invoke("Fetch Models Request", $"GET {ModelsUrl}", false);

            using var client = new RestClient(ModelsUrl);
            var request = new RestRequest("", Method.Get);
            request.AddHeader("Authorization", $"Bearer {apiKey}");

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Fetch Models Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            var models = new List<string>();

            if (response.IsSuccessful)
            {
                try
                {
                    dynamic json = JsonConvert.DeserializeObject(response.Content);
                    if (json?.data != null)
                    {
                        foreach (var m in json.data)
                        {
                            models.Add((string)m.id);
                        }
                    }
                }
                catch { }
            }
            
            return models.Count > 0 ? models : new List<string> { "openai/gpt-3.5-turbo", "anthropic/claude-3-haiku", "google/gemini-flash-1.5" };
        }

        public async IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/TagForge/TagForge");
            client.DefaultRequestHeaders.Add("X-Title", "TagForge");

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }
            
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                string base64Image = Convert.ToBase64String(imageBytes);
                string mimeType = "image/jpeg";
                if (imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) mimeType = "image/png";
                if (imagePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) mimeType = "image/webp";

                messages.Add(new 
                { 
                    role = "user", 
                    content = new object[] 
                    {
                        new { type = "text", text = userPrompt },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Image}" } }
                    }
                });
            }
            else
            {
                messages.Add(new { role = "user", content = userPrompt });
            }

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "openai/gpt-3.5-turbo" : model,
                messages = messages,
                stream = true
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            
            // Prep Log (Truncate Base64 for sanity in logs, keeping user happy but app responsive)
            if (jsonBody.Length > 2000 && jsonBody.Contains("image_url"))
            {
                 logger?.Invoke("Stream Request", $"POST {targetUrl}\n{jsonBody.Substring(0, 1000)} ... [IMAGE DATA] ...", false);
            }
            else
            {
                 logger?.Invoke("Stream Request", $"POST {targetUrl}\n{jsonBody}", false);
            }

            var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                 var err = await response.Content.ReadAsStringAsync();
                 logger?.Invoke("Stream Failed", $"HTTP {(int)response.StatusCode}\n{err}", true);
                 throw new Exception($"OpenRouter API Error: {err}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            
            logger?.Invoke("Stream Started", "HTTP 200 OK - Streaming...", true);

            while (!reader.EndOfStream)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Trim() == "data: [DONE]") break;
                
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6).Trim();
                    if (data == "[DONE]") break; 
                    
                    string content = null;
                    try {
                        dynamic json = JsonConvert.DeserializeObject(data);
                        content = json?.choices?[0]?.delta?.content;
                    } catch {}
                    
                    if (!string.IsNullOrEmpty(content)) yield return content;
                }
            }
            logger?.Invoke("Stream Completed", "Stream finished successfully.", true);
        }
        public Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
