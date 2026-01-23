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

        public async Task<bool> PingAsync(string apiKey, string baseUrl)
        {
            using var client = new RestClient(ModelsUrl);
            var request = new RestRequest("", Method.Get);
            request.AddHeader("Authorization", $"Bearer {apiKey}");

            var response = await client.ExecuteAsync(request);
            
            if (!response.IsSuccessful)
            {
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
            
            return true;
        }

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            using var client = new RestClient(targetUrl);
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Authorization", $"Bearer {apiKey}");
            request.AddHeader("Content-Type", "application/json");
            // Good practice for OpenRouter to identify app
            request.AddHeader("HTTP-Referer", "https://github.com/TagForge/TagForge"); 
            request.AddHeader("X-Title", "TagForge");

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }
            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "openai/gpt-3.5-turbo" : model,
                messages = messages
            };

            request.AddJsonBody(payload);

            var response = await client.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"OpenRouter API Error: {response.Content}");
            }

            dynamic json = JsonConvert.DeserializeObject(response.Content);
            string text = json?.choices?[0]?.message?.content;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl)
        {
            using var client = new RestClient(ModelsUrl);
            var request = new RestRequest("", Method.Get);
            request.AddHeader("Authorization", $"Bearer {apiKey}");

            var response = await client.ExecuteAsync(request);
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
        public async IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl)
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
            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "openai/gpt-3.5-turbo" : model,
                messages = messages,
                stream = true
            };

            var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                 var err = await response.Content.ReadAsStringAsync();
                 throw new Exception($"OpenRouter API Error: {err}");
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
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
    }
}
