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

        public async Task<bool> PingAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
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
                    return true;
                }
                // Determine if it was a connection error or just a 404/etc
                // If it's 404, maybe the path is wrong but server is there? 
                // Any response is better than no connection for 'Ping' usually, 
                // but we want to verify API compatibility.
                return false; 
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
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
            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "local-model" : model,
                messages = messages,
                stream = false
            };

            request.AddJsonBody(payload);

            var response = await client.ExecuteAsync(request, cancellationToken);
            if (!response.IsSuccessful)
            {
                throw new Exception($"LM Studio API Error: {response.Content}");
            }

            dynamic json = JsonConvert.DeserializeObject(response.Content);
            string text = json?.choices?[0]?.message?.content;
            return text ?? string.Empty;
        }

        public async Task<List<string>> FetchModelsAsync(string apiKey, string baseUrl, System.Threading.CancellationToken cancellationToken = default)
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

            using var client = new RestClient(targetUrl);
            var request = new RestRequest("", Method.Get);
             if (!string.IsNullOrEmpty(apiKey))
            {
                request.AddHeader("Authorization", $"Bearer {apiKey}");
            }

            var response = await client.ExecuteAsync(request, cancellationToken);
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

        public async IAsyncEnumerable<string> GenerateStreamingAsync(string systemPrompt, string userPrompt, string model, string apiKey, string baseUrl, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
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
            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model = string.IsNullOrEmpty(model) ? "local-model" : model,
                messages = messages,
                stream = true
            };

            var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                 var err = await response.Content.ReadAsStringAsync();
                 throw new Exception($"LM Studio API Error: {err}");
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
