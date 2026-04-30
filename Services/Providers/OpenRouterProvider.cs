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
    public class OpenRouterProvider : IAIProvider
    {
        public string Name => "OpenRouter";
        private const string DefaultBaseUrl = "https://openrouter.ai/api/v1/chat/completions";

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = "https://openrouter.ai/api/v1/auth/key";
            logger?.Invoke("Ping Request", $"GET {url}", false);

            var options = new RestClientOptions(url);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Get);
            request.AddHeader("Authorization", $"Bearer {apiKey}");

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            if (!response.IsSuccessful)
            {
                logger?.Invoke("Ping Failed", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);
                throw new Exception($"OpenRouter API Error: {response.Content}");
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
            request.AddHeader("Authorization", $"Bearer {apiKey}");
            request.AddHeader("Content-Type", "application/json");

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
                model = string.IsNullOrEmpty(model) ? "mistralai/mistral-7b-instruct:free" : model,
                messages = messages,
                max_tokens = maxTokens
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.AddJsonBody(payload);

            if (jsonBody.Length > 2000 && jsonBody.Contains("image_url"))
                logger?.Invoke("Generate Request", $"POST {targetUrl}\n{jsonBody.Substring(0, 1000)} ... [IMAGE DATA] ...", false);
            else
                logger?.Invoke("Generate Request", $"POST {targetUrl}\n{jsonBody}", false);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Generate Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful)
            {
                throw new Exception($"OpenRouter API Error: {response.Content}");
            }

            dynamic? json = JsonConvert.DeserializeObject(response.Content ?? "{}");
            string? text = json?.choices?[0]?.message?.content;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = "https://openrouter.ai/api/v1/models";
            logger?.Invoke("Fetch Models Request", $"GET {url}", false);

            var options = new RestClientOptions(url);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Get);
            
            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Fetch Models Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful) return new List<string> { "mistralai/mistral-7b-instruct:free", "google/gemma-7b-it:free" };

            var models = new List<string>();
            try 
            {
                dynamic? json = JsonConvert.DeserializeObject(response.Content ?? "{}");
                if (json?.data != null)
                {
                    foreach (var m in json.data)
                    {
                        models.Add((string)m.id);
                    }
                }
            }
            catch {}
            
            return models.Count > 0 ? models : new List<string> { "mistralai/mistral-7b-instruct:free" };
        }

        public async IAsyncEnumerable<(string Token, bool IsReasoning)> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, int maxTokens = 4096, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            var client = NetworkService.Instance.Client;
            
            // We need to be careful with DefaultRequestHeaders on a shared client
            // Better to add Authorization to the request message directly
            // client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

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
                model = string.IsNullOrEmpty(model) ? "mistralai/mistral-7b-instruct:free" : model,
                messages = messages,
                stream = true,
                max_tokens = maxTokens
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            
            if (jsonBody.Length > 2000 && jsonBody.Contains("image_url"))
                 logger?.Invoke("Stream Request", $"POST {targetUrl}\n{jsonBody.Substring(0, 1000)} ... [IMAGE DATA] ...", false);
            else
                 logger?.Invoke("Stream Request", $"POST {targetUrl}\n{jsonBody}", false);

            var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
                    
                    string? content = null;
                    bool isReasoning = false;
                    try {
                        dynamic? json = JsonConvert.DeserializeObject(data);
                        content = json?.choices?[0]?.delta?.content;
                        
                        if (string.IsNullOrEmpty(content))
                        {
                            content = json?.choices?[0]?.delta?.thought;
                            if (!string.IsNullOrEmpty(content)) isReasoning = true;
                            
                            if (string.IsNullOrEmpty(content))
                            {
                                content = json?.choices?[0]?.delta?.reasoning;
                                if (!string.IsNullOrEmpty(content)) isReasoning = true;
                                
                                if (string.IsNullOrEmpty(content))
                                {
                                    content = json?.choices?[0]?.delta?.reasoning_content;
                                    if (!string.IsNullOrEmpty(content)) isReasoning = true;
                                }
                            }
                        }
                    } catch {}
                    
                    if (!string.IsNullOrEmpty(content)) yield return (content!, isReasoning);
                }
            }
            logger?.Invoke("Stream Completed", "Stream finished successfully.", true);
        }
        public Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
