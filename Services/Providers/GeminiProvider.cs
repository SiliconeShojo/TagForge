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
    public class GeminiProvider : IAIProvider
    {
        public string Name => "Google Gemini";

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            using var client = new RestClient("https://generativelanguage.googleapis.com/v1beta/models");
            var request = new RestRequest("", Method.Get);
            request.AddQueryParameter("key", apiKey);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            if (!response.IsSuccessful)
            {
                string errorMessage = "Unknown error";
                try
                {
                    // Try to parse JSON and extract error.message
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

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            // Gemini uses :generateContent
            // Model defaults to gemini-pro if not specified, but usually it should be passed.
            if (string.IsNullOrEmpty(model)) model = "gemini-1.5-flash"; // Fallback

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            using var client = new RestClient(url);
            var request = new RestRequest("", Method.Post);
            request.AddQueryParameter("key", apiKey);
            request.AddHeader("Content-Type", "application/json");

            // Gemini payload structure
            var payload = new
            {
                contents = new[]
                {
                    new {
                        role = "user",
                        parts = new[] {
                            new { text = string.IsNullOrEmpty(systemPrompt) ? userPrompt : $"{systemPrompt}\n\n{userPrompt}" } 
                        }
                    }
                }
            };

            request.AddJsonBody(payload);

            var response = await client.ExecuteAsync(request, cancellationToken);
            if (!response.IsSuccessful)
            {
                throw new Exception($"Gemini API Error: {response.Content}");
            }

            dynamic json = JsonConvert.DeserializeObject(response.Content);
            string text = json?.candidates?[0]?.content?.parts?[0]?.text;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            using var client = new RestClient("https://generativelanguage.googleapis.com/v1beta/models");
            var request = new RestRequest("", Method.Get);
            request.AddQueryParameter("key", apiKey);
            
            var response = await client.ExecuteAsync(request, cancellationToken);
            if (!response.IsSuccessful) return new List<string> { "gemini-1.5-flash", "gemini-1.5-pro", "gemini-pro" };

            var models = new List<string>();
            try 
            {
                dynamic json = JsonConvert.DeserializeObject(response.Content);
                if (json?.models != null)
                {
                    foreach (var m in json.models)
                    {
                        string name = m.name; // e.g. "models/gemini-1.5-pro"
                        models.Add(name.Replace("models/", ""));
                    }
                }
            }
            catch {}
            
            return models.Count > 0 ? models : new List<string> { "gemini-1.5-flash", "gemini-1.5-pro" };
        }
        public async IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(model)) model = "gemini-1.5-flash"; 
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}";
            using var client = new HttpClient();

            var payload = new
            {
                contents = new[]
                {
                    new {
                        role = "user",
                        parts = new[] {
                            new { text = string.IsNullOrEmpty(systemPrompt) ? userPrompt : $"{systemPrompt}\n\n{userPrompt}" } 
                        }
                    }
                }
            };
            
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                 var err = await response.Content.ReadAsStringAsync();
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
                     if (data == "[DONE]") break;
                     
                     string content = null;
                     try {
                         dynamic json = JsonConvert.DeserializeObject(data);
                         content = json?.candidates?[0]?.content?.parts?[0]?.text;
                     } catch {}
                     
                     if (!string.IsNullOrEmpty(content)) yield return content;
                 }
            }
        }
        public Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
