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

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            // Validate API key is provided
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new Exception("HTTP 401 Unauthorized: API key is required");
            }

            // Revert to verifying /models as it's the standard discovery endpoint.
            // Some endpoints (like TGI) might be public, but checking for empty key (above) prevents obvious errors.
            var url = GetModelsUrl(baseUrl);
            using var client = new RestClient(url);
            var request = new RestRequest("", Method.Get);
            request.AddHeader("Authorization", $"Bearer {apiKey}");

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            if (!response.IsSuccessful)
            {
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
            
            return true;
        }


        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            var client = new RestClient(GetChatUrl(baseUrl));
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Authorization", $"Bearer {apiKey}");
            request.AddHeader("Content-Type", "application/json");

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }
            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "mistralai/Mistral-7B-Instruct-v0.3" : model,
                messages = messages,
                max_tokens = 1024
            };

            request.AddJsonBody(payload);

            var response = await client.ExecuteAsync(request, cancellationToken);
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

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            using var client = new RestClient(GetModelsUrl(baseUrl));
            var request = new RestRequest("", Method.Get);
            request.AddHeader("Authorization", $"Bearer {apiKey}");

            var response = await client.ExecuteAsync(request, cancellationToken);
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

        public async IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }
            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "mistralai/Mistral-7B-Instruct-v0.3" : model,
                messages = messages,
                stream = true,
                max_tokens = 1024
            };

            var request = new HttpRequestMessage(HttpMethod.Post, GetChatUrl(baseUrl));
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                 var err = await response.Content.ReadAsStringAsync();
                 throw new Exception($"Hugging Face API Error: {err}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

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
        }
        public Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
