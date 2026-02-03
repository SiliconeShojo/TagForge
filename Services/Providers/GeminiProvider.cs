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

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = "https://generativelanguage.googleapis.com/v1beta/models";
            logger?.Invoke("Ping Request", $"GET {url}?key=...", false);

            using var client = new RestClient(url);
            var request = new RestRequest("", Method.Get);
            request.AddQueryParameter("key", apiKey);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            if (!response.IsSuccessful)
            {
                logger?.Invoke("Ping Failed", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

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
            
            logger?.Invoke("Ping Success", "Connection verified.", true);
            return true;
        }

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            // Gemini uses :generateContent
            // Model defaults to gemini-pro if not specified, but usually it should be passed.
            if (string.IsNullOrEmpty(model)) model = "gemini-1.5-flash"; // Fallback

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            using var client = new RestClient(url);
            var request = new RestRequest("", Method.Post);
            request.AddQueryParameter("key", apiKey);
            request.AddHeader("Content-Type", "application/json");

            var parts = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                 // Gemini sometimes prefers system instructions separately but combining is safer for basic implementation
                 parts.Add(new { text = systemPrompt + "\n\n" });
            }

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                string base64Image = Convert.ToBase64String(imageBytes);
                string mimeType = "image/jpeg";
                if (imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) mimeType = "image/png";
                if (imagePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) mimeType = "image/webp";

                parts.Add(new 
                {
                    inlineData = new 
                    {
                        mime_type = mimeType,
                        data = base64Image
                    }
                });
            }

            parts.Add(new { text = userPrompt });

            // Gemini payload structure
            var payload = new
            {
                contents = new[]
                {
                    new {
                        role = "user",
                        parts = parts
                    }
                }
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.AddJsonBody(payload);

            if (jsonBody.Length > 2000 && jsonBody.Contains("inlineData"))
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody.Substring(0, 500)} ... [IMAGE DATA] ...", false);
            else
                logger?.Invoke("Generate Request", $"POST {url}\n{jsonBody}", false);

            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Generate Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

            if (!response.IsSuccessful)
            {
                throw new Exception($"Gemini API Error: {response.Content}");
            }

            dynamic json = JsonConvert.DeserializeObject(response.Content);
            string text = json?.candidates?[0]?.content?.parts?[0]?.text;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var url = "https://generativelanguage.googleapis.com/v1beta/models";
            logger?.Invoke("Fetch Models Request", $"GET {url}", false);

            using var client = new RestClient(url);
            var request = new RestRequest("", Method.Get);
            request.AddQueryParameter("key", apiKey);
            
            var response = await client.ExecuteAsync(request, cancellationToken);
            
            logger?.Invoke("Fetch Models Response", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);

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
        public async IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            if (string.IsNullOrEmpty(model)) model = "gemini-1.5-flash"; 
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}";
            using var client = new HttpClient();

            var parts = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                 parts.Add(new { text = systemPrompt + "\n\n" });
            }

            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                string base64Image = Convert.ToBase64String(imageBytes);
                string mimeType = "image/jpeg";
                if (imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) mimeType = "image/png";
                if (imagePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) mimeType = "image/webp";

                parts.Add(new 
                {
                    inlineData = new 
                    {
                        mime_type = mimeType,
                        data = base64Image
                    }
                });
            }

            parts.Add(new { text = userPrompt });

            var payload = new
            {
                contents = new[]
                {
                    new {
                        role = "user",
                        parts = parts
                    }
                }
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            
            if (jsonBody.Length > 2000 && jsonBody.Contains("inlineData"))
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
                 throw new Exception($"Gemini API Error: {err}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            
            logger?.Invoke("Stream Started", "HTTP 200 OK - Streaming...", true);

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
            logger?.Invoke("Stream Completed", "Stream finished successfully.", true);
        }
        public Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
