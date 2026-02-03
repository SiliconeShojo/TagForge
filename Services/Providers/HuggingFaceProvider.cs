using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RestSharp;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;

namespace TagForge.Services.Providers
{
    public class HuggingFaceProvider : IAIProvider
    {
        public string Name => "Hugging Face";

        // If the user provides just the root (e.g. https://router.huggingface.co/v1), we append endpoints.
        
        private string GetChatUrl(string baseUrl)
        {
            var url = baseUrl.TrimEnd('/');
            // If the user pasted the full chat completions URL, use it
            if (url.EndsWith("/chat/completions")) return url;
            
            // Otherwise assume it's the base (e.g. /v1) and append
            return $"{url}/chat/completions";
        }

        private string GetModelsUrl(string baseUrl)
        {
            var url = baseUrl.TrimEnd('/');
            // Try to infer models endpoint relative to base
            if (url.EndsWith("/chat/completions"))
            {
                return url.Replace("/chat/completions", "/models");
            }
            return $"{url}/models";
        }

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            // Validate API key is provided
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new Exception("HTTP 401 Unauthorized: API key is required");
            }

            // Revert to verifying /models as it's the standard discovery endpoint.
            var url = GetModelsUrl(baseUrl);
            logger?.Invoke("Ping Request", $"GET {url}", false);

            using var client = new RestClient(url);
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
                    errorMessage = json?.error?.message ?? json?.error ?? response.Content?.Substring(0, Math.Min(100, response.Content?.Length ?? 0));
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
            var url = GetChatUrl(baseUrl);
            var client = new RestClient(url);
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
                model = string.IsNullOrEmpty(model) ? "mistralai/Mistral-7B-Instruct-v0.3" : model,
                messages = messages,
                max_tokens = 1024
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.AddJsonBody(payload);

            // Log Request (Truncate Base64)
            if (jsonBody.Length > 2000 && jsonBody.Contains("base64,"))
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody.Substring(0, 500)} ... [IMAGE DATA] ...", false);
            else
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody}", false);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            // Log Response
            logger?.Invoke("Generate Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful)
            {
                var errorMsg = $"HTTP {(int)response.StatusCode} {response.StatusCode}\n" +
                             $"Endpoint: {GetChatUrl(baseUrl)}\n" +
                             $"Model: {(string.IsNullOrEmpty(model) ? "mistralai/Mistral-7B-Instruct-v0.3" : model)}\n" +
                             $"Response: {response.Content?.Substring(0, Math.Min(500, response.Content?.Length ?? 0))}";
                throw new Exception($"Hugging Face API Error\n{errorMsg}");
            }

            try 
            {
                dynamic json = JsonConvert.DeserializeObject(response.Content);
                return json?.choices?[0]?.message?.content ?? "";
            }
            catch
            {
                return response.Content; // Fallback
            }
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = GetModelsUrl(baseUrl);
            logger?.Invoke("Fetch Models Request", $"GET {url}", false);

            using var client = new RestClient(url);
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
                    // Standard OpenAI list format { data: [ { id: "..." } ] }
                    if (json?.data != null)
                    {
                        foreach (var m in json.data)
                        {
                            models.Add((string)m.id);
                        }
                    }
                    // Some TGI endpoints might return array directly? 
                    else if (json is Newtonsoft.Json.Linq.JArray arr)
                    {
                        foreach (var m in arr)
                        {
                            if (m["id"] != null) models.Add(m["id"].ToString());
                        }
                    }
                }
                catch { }
            }
            
            // Fallback list if fetch fails or returns empty
            if (models.Count == 0)
            {
                return new List<string> { 
                    "mistralai/Mistral-7B-Instruct-v0.3",
                    "meta-llama/Meta-Llama-3-8B-Instruct",
                    "HuggingFaceH4/zephyr-7b-beta"
                };
            }
            return models;
        }

        public async IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = GetChatUrl(baseUrl);
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

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
                max_tokens = 1024
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
