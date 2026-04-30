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
    public class HuggingFaceProvider : IAIProvider
    {
        public string Name => "Hugging Face";
        private const string DefaultBaseUrl = "https://router.huggingface.co/v1/chat/completions";

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            string url;
            if (string.IsNullOrEmpty(baseUrl))
            {
                url = "https://router.huggingface.co/v1/models";
            }
            else
            {
                url = baseUrl.TrimEnd('/');
                if (url.EndsWith("/chat/completions")) url = url.Replace("/chat/completions", "/models");
                else if (!url.EndsWith("/models") && !url.Contains("api-inference.huggingface.co")) url += "/models";
                else if (url.Contains("api-inference.huggingface.co") && !url.EndsWith("/models")) url = "https://api-inference.huggingface.co/models";
            }

            logger?.Invoke("Ping Request", $"GET {url}", false);

            var options = new RestClientOptions(url);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Get);
            request.AddHeader("Authorization", $"Bearer {apiKey}");
            request.AddHeader("User-Agent", "TagForge/1.0");

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            if (!response.IsSuccessful)
            {
                logger?.Invoke("Ping Failed", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);
                throw new Exception($"Hugging Face API Error: {response.StatusDescription} ({(int)response.StatusCode})\n{response.Content}");
            }
            
            logger?.Invoke("Ping Success", "Connection verified.", true);
            return true;
        }

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, int maxTokens = 4096, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            if (baseUrl == "https://api-inference.huggingface.co/v1/chat/completions") baseUrl = string.Empty;
            var url = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            if (!url.EndsWith("/chat/completions") && !url.Contains("api-inference.huggingface.co")) 
                url = url.TrimEnd('/') + "/chat/completions";
                
            var options = new RestClientOptions(url);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Authorization", $"Bearer {apiKey}");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("User-Agent", "TagForge/1.0");

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
                model = string.IsNullOrEmpty(model) ? "mistralai/Mistral-7B-Instruct-v0.3" : model,
                messages = messages,
                max_tokens = maxTokens
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.AddJsonBody(payload);

            if (jsonBody.Length > 2000 && jsonBody.Contains("image_url"))
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody.Substring(0, 1000)} ... [IMAGE DATA] ...", false);
            else
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody}", false);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Generate Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful)
            {
                throw new Exception($"Hugging Face API Error: {response.Content}");
            }

            dynamic? json = JsonConvert.DeserializeObject(response.Content ?? "{}");
            string? text = json?.choices?[0]?.message?.content;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            if (baseUrl == "https://api-inference.huggingface.co/v1/chat/completions") baseUrl = string.Empty;
            
            string url;
            if (string.IsNullOrEmpty(baseUrl))
            {
                url = "https://router.huggingface.co/v1/models";
            }
            else
            {
                url = baseUrl.TrimEnd('/');
                if (url.EndsWith("/chat/completions")) url = url.Replace("/chat/completions", "/models");
                else if (!url.EndsWith("/models") && !url.Contains("api-inference.huggingface.co")) url += "/models";
                else if (url.Contains("api-inference.huggingface.co") && !url.EndsWith("/models")) url = "https://api-inference.huggingface.co/models";
            }
            
            logger?.Invoke("Fetch Models Request", $"GET {url}", false);

            var options = new RestClientOptions(url);
            using var client = new RestClient(NetworkService.Instance.Client, options, disposeHttpClient: false);
            var request = new RestRequest("", Method.Get);
            request.AddHeader("Authorization", $"Bearer {apiKey}");
            
            var response = await client.ExecuteAsync(request, cancellationToken);
            logger?.Invoke("Fetch Models Response", $"HTTP {(int)response.StatusCode}", true);

            if (!response.IsSuccessful)
            {
                return new List<string> 
                { 
                    "mistralai/Mistral-7B-Instruct-v0.3",
                    "meta-llama/Meta-Llama-3-8B-Instruct",
                    "google/gemma-2-9b-it",
                    "microsoft/Phi-3-mini-4k-instruct"
                };
            }

            try 
            {
                dynamic? json = JsonConvert.DeserializeObject(response.Content ?? "{}");
                var models = new List<string>();
                if (json?.data != null)
                {
                    foreach (var m in json.data)
                    {
                        models.Add((string)m.id);
                    }
                }
                return models.Count > 0 ? models : new List<string> { "mistralai/Mistral-7B-Instruct-v0.3" };
            }
            catch 
            {
                return new List<string> { "mistralai/Mistral-7B-Instruct-v0.3" };
            }
        }

        public async IAsyncEnumerable<(string Token, bool IsReasoning)> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, int maxTokens = 4096, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            if (baseUrl == "https://api-inference.huggingface.co/v1/chat/completions") baseUrl = string.Empty;
            var url = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            if (!url.EndsWith("/chat/completions") && !url.Contains("api-inference.huggingface.co")) 
                url = url.TrimEnd('/') + "/chat/completions";
                
            var client = NetworkService.Instance.Client;
            // client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            // client.DefaultRequestHeaders.Add("User-Agent", "TagForge/1.0");

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
                model = string.IsNullOrEmpty(model) ? "mistralai/Mistral-7B-Instruct-v0.3" : model,
                messages = messages,
                stream = true,
                max_tokens = maxTokens
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            
            if (jsonBody.Length > 2000 && jsonBody.Contains("base64,"))
            {
                 logger?.Invoke("Stream Request", $"POST {url}\n{jsonBody.Substring(0, 1000)} ... [IMAGE DATA] ...", false);
            }
            else
            {
                 logger?.Invoke("Stream Request", $"POST {url}\n{jsonBody}", false);
            }

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.UserAgent.ParseAdd("TagForge/1.0");
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                 var err = await response.Content.ReadAsStringAsync();
                 logger?.Invoke("Stream Failed", $"HTTP {(int)response.StatusCode}\n{err}", true);
                 throw new Exception($"Hugging Face API Error: {err}");
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
