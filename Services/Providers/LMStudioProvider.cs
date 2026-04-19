using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RestSharp;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;

namespace TagForge.Services.Providers
{
    public class LMStudioProvider : IAIProvider
    {
        public string Name => "LM Studio";
        // Default LM Studio local server URL
        private const string DefaultBaseUrl = "http://localhost:1234/v1/chat/completions";
        private const string DefaultModelsUrl = "http://localhost:1234/v1/models";

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            // If baseUrl is provided, try to infer the models endpoint, otherwise use default
            // LM Studio usually follows OpenAI standard: /v1/models
            string targetUrl = DefaultModelsUrl;
            
            if (!string.IsNullOrEmpty(baseUrl))
            {
                // Try to construct models url from base url
                // if base is .../v1/chat/completions, we want .../v1/models
                if (baseUrl.Contains("/chat/completions"))
                {
                    targetUrl = baseUrl.Replace("/chat/completions", "/models");
                }
                else
                {
                    // Fallback or assume root
                     targetUrl = baseUrl.TrimEnd('/') + "/models";
                }
            }

            logger?.Invoke("Ping Request", $"GET {targetUrl}", false);

            using var client = new RestClient(targetUrl);
            var request = new RestRequest("", Method.Get);
            // LM Studio often doesn't require a key, but we send it if provided
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.AddHeader("Authorization", $"Bearer {apiKey}");
            }

            try 
            {
                var response = await client.ExecuteAsync(request, cancellationToken);
                
                if (response.IsSuccessful)
                {
                    logger?.Invoke("Ping Success", "Connection verified.", true);
                    return true;
                }
                // Determine if it was a connection error or just a 404/etc
                // If it's 404, maybe the path is wrong but server is there? 
                // Any response is better than no connection for 'Ping' usually, 
                // but we want to verify API compatibility.
                logger?.Invoke("Ping Failed", $"HTTP {(int)response.StatusCode}\n{response.Content}", true);
                return false; 
            }
            catch (Exception ex)
            {
                logger?.Invoke("Ping Error", ex.Message, true);
                return false;
            }
        }

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            // Append /chat/completions if not present and not just a root
            if (!targetUrl.Contains("/chat/completions") && !targetUrl.EndsWith("/chat/completions"))
            {
                targetUrl = targetUrl.TrimEnd('/') + "/chat/completions";
            }
            using var client = new RestClient(targetUrl);
            var request = new RestRequest("", Method.Post);
            
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.AddHeader("Authorization", $"Bearer {apiKey}");
            }
            
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
                model = string.IsNullOrEmpty(model) ? "local-model" : model,
                messages = messages,
                stream = false
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
                throw new Exception($"LM Studio API Error: {response.Content}");
            }

            dynamic json = JsonConvert.DeserializeObject(response.Content);
            string text = json?.choices?[0]?.message?.content;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            string targetUrl = DefaultModelsUrl;
             if (!string.IsNullOrEmpty(baseUrl))
            {
                if (baseUrl.Contains("/chat/completions"))
                {
                    targetUrl = baseUrl.Replace("/chat/completions", "/models");
                }
                else
                {
                     targetUrl = baseUrl.TrimEnd('/') + "/models";
                }
            }
            
            logger?.Invoke("Fetch Models Request", $"GET {targetUrl}", false);

            using var client = new RestClient(targetUrl);
            var request = new RestRequest("", Method.Get);
             if (!string.IsNullOrEmpty(apiKey))
            {
                request.AddHeader("Authorization", $"Bearer {apiKey}");
            }

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
            
            return models.Count > 0 ? models : new List<string> { "local-model" };
        }

        public async IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, string? imagePath = null, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default, Action<string, string, bool>? logger = null)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            // Append /chat/completions if not present
            if (!targetUrl.Contains("/chat/completions") && !targetUrl.EndsWith("/chat/completions"))
            {
                targetUrl = targetUrl.TrimEnd('/') + "/chat/completions";
            }
            using var client = new HttpClient();
            
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }

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
                model = string.IsNullOrEmpty(model) ? "local-model" : model,
                messages = messages,
                stream = true
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            if (jsonBody.Length > 2000 && jsonBody.Contains("image_url"))
                logger?.Invoke("Stream Request", $"POST {targetUrl}\n{jsonBody.Substring(0, 1000)} ... [IMAGE DATA] ...", false);
            else
                logger?.Invoke("Stream Request", $"POST {targetUrl}\n{jsonBody}", false);

            var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                 var err = await response.Content.ReadAsStringAsync();
                 logger?.Invoke("Stream Failed", $"HTTP {(int)response.StatusCode}\n{err}", true);
                 throw new Exception($"LM Studio API Error: {err}");
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
        public async Task LoadModelAsync(string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
        {
            var targetUrl = string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
            var hostUrl = targetUrl;
            
            // Extract root/host for the /api/v0/models call if possible
             if (targetUrl.Contains("/v1"))
            {
               hostUrl = targetUrl.Substring(0, targetUrl.IndexOf("/v1"));
            }
            else
            {
                // Fallback attempt to guess host
                 hostUrl = targetUrl.Replace("/chat/completions", "").Replace("/v1", "");
            }
            hostUrl = hostUrl.TrimEnd('/');

            try 
            {
                // Simple blocking load via generation test.
                // We use a long timeout because loading a large model from HDD can take time.
                var options = new RestClientOptions(targetUrl) { Timeout = TimeSpan.FromMinutes(10) };
                using var client = new RestClient(options);
                var request = new RestRequest("", Method.Post);
                
                var payload = new
                {
                    model = string.IsNullOrEmpty(model) ? "local-model" : model,
                    messages = new [] { new { role = "user", content = "hi" } },
                    max_tokens = 1
                };
                
                request.AddJsonBody(payload);
                var response = await client.ExecuteAsync(request, cancellationToken);

                if (!response.IsSuccessful)
                {
                     // If it fails, we throw to let the UI know. 
                     // Common failure: 404 (model not found), 500 (server error).
                     throw new Exception($"Load check failed ({response.StatusCode}): {response.ErrorMessage ?? response.Content}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Model load failed: {ex.Message}", ex);
            }
        }
    }
}
