using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using TagForge.Services;
using TagForge.Models;
using System;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace TagForge.ViewModels
{
    public partial class GenerationViewModel : ViewModelBase
    {
        private readonly SessionService _sessionService;
        private readonly MainViewModel _mainViewModel;
        private readonly ProviderFactory _providerFactory;
        private readonly HistoryService _historyService;

        [ObservableProperty]
        private string _prompt;

        [ObservableProperty]
        private bool _isGenerating;

        public event Action? RequestScroll;

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public GenerationViewModel(SessionService sessionService, MainViewModel mainVm)
        {
            _sessionService = sessionService;
            _mainViewModel = mainVm;
            _providerFactory = new ProviderFactory();
            _historyService = new HistoryService();
            
            LoadHistory();
        }

        public GenerationViewModel()
        {
             var settings = new SettingsService();
             _sessionService = new SessionService(settings);
             _providerFactory = new ProviderFactory();
             _historyService = new HistoryService();
        }

        public ObservableCollection<Persona> Personas => _sessionService.Personas;
        
        public Persona SelectedPersona
        {
            get => _sessionService.ActivePersona;
            set
            {
                _sessionService.ActivePersona = value;
                OnPropertyChanged(nameof(SelectedPersona));
            }
        }
        
        private async void LoadHistory()
        {
            var history = await _historyService.LoadHistoryAsync("generator.json");
            Messages.Clear();
            foreach (var msg in history)
            {
                Messages.Add(msg);
            }
        }

        private async Task SaveHistory()
        {
            await _historyService.SaveHistoryAsync("generator.json", Messages);
        }

        [RelayCommand]
        private async Task Generate()
        {
            if (string.IsNullOrWhiteSpace(Prompt)) return;
            
            var profile = _sessionService.ActiveProfile;
            var persona = _sessionService.ActivePersona;

            if (profile == null) 
            {
               Messages.Add(new ChatMessage("Error", "No Active Agent selected.", "Please select an agent in the Agent Manager."));
               SaveHistory();
               return;
            }
            if (persona == null)
            {
                 Messages.Add(new ChatMessage("Error", "No Persona selected."));
                 SaveHistory();
                 return;
            }

            // User Message (VISIBLE - Requested Feature)
            string currentPrompt = Prompt;
            var userMsg = new ChatMessage("User", currentPrompt);
            Messages.Add(userMsg);

            Prompt = string.Empty;
            IsGenerating = true;
            _cts = new System.Threading.CancellationTokenSource();
            
            // Placeholder for AI Response (to update iteratively)
            // Placeholder for AI Response (to update iteratively)
            var aiMsg = new ChatMessage("Assistant", "");
            aiMsg.IsThinking = true;
            Messages.Add(aiMsg);

            var sw = Stopwatch.StartNew();

            await Task.Run(async () => 
            {
                // Consumer Task: Updates UI smoothly
                var tokenQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
                bool networkFinished = false;
                Task displayTask = null;

                try 
                {
                    var provider = _providerFactory.CreateProvider(profile.Provider);
                    if (provider == null) throw new Exception($"Provider '{profile.Provider}' implementation not found.");

                    string systemPrompt = persona.SystemPrompt;
                    string finalSystemMessage = systemPrompt.Contains("{input}") ? systemPrompt.Replace("{input}", currentPrompt) : systemPrompt;

                    bool isThinking = false;
                    
                    displayTask = Task.Run(async () => 
                    {
                        int tickCount = 0;
                        while (!networkFinished || !tokenQueue.IsEmpty)
                        {
                            if (tokenQueue.TryDequeue(out var str))
                            {
                                var sb = new StringBuilder(str);
                                
                                // Adaptive Batching: Speed up if falling behind, but Cap to prevent freeze
                                // Base 3 + 1 per 30 pending. Cap at 15 strings per tick.
                                int batchLimit = 3 + (tokenQueue.Count / 30);
                                batchLimit = System.Math.Min(batchLimit, 15);

                                int count = 0;
                                while (count < batchLimit && tokenQueue.TryDequeue(out var next))
                                {
                                    sb.Append(next);
                                    count++;
                                }

                                await Dispatcher.UIThread.InvokeAsync(() => 
                                {
                                    if (aiMsg.IsThinking) aiMsg.IsThinking = false;
                                    aiMsg.Content += sb.ToString();
                                    
                                    // Throttled Scroll (Prevent Layout Thrashing)
                                    if (tickCount % 5 == 0) RequestScroll?.Invoke();
                                }, DispatcherPriority.Background);
                            }
                            else if (networkFinished)
                            {
                                break; 
                            }
                            else
                            {
                                // Queue empty, waiting for network
                                await Task.Delay(10);
                                continue;
                            }
                            
                            // 30fps Target
                            tickCount++;
                            await Task.Delay(35); 
                        }
                    });
                    
                    // Producer Loop: Fetches from Network
                    await foreach (var token in provider.GenerateStreamingAsync(finalSystemMessage, currentPrompt, profile.SelectedModel, profile.ApiKey, profile.EndpointUrl, _cts.Token))
                    {
                         string tempToken = token;
                         
                         if (tempToken.Contains("<think>")) 
                         {
                             isThinking = true;
                             tempToken = tempToken.Replace("<think>", "");
                             _ = Dispatcher.UIThread.InvokeAsync(() => aiMsg.IsThinking = true);
                         }
                         
                         if (tempToken.Contains("</think>"))
                         {
                             isThinking = false;
                             var parts = tempToken.Split(new[] { "</think>" }, StringSplitOptions.None);
                             if (parts.Length > 1) tempToken = parts[1]; 
                             else tempToken = ""; 
                             _ = Dispatcher.UIThread.InvokeAsync(() => aiMsg.IsThinking = false);
                         }
                         else if (isThinking)
                         {
                             continue;
                         }
                         
                         if (!string.IsNullOrEmpty(tempToken))
                         {
                             tokenQueue.Enqueue(tempToken);
                         }
                    }
                    
                    networkFinished = true;
                    await displayTask; 

                    sw.Stop();
                    _sessionService.LastLatency = sw.ElapsedMilliseconds;
                }
                catch (OperationCanceledException)
                {
                    networkFinished = true; // Ensure display task finishes
                    Dispatcher.UIThread.Invoke(() => 
                        Messages.Add(new ChatMessage("System", "Generation Stopped", "")));
                }
                catch (Exception ex)
                {
                    networkFinished = true;
                    var errorDetails = $"Exception Type: {ex.GetType().Name}\nMessage: {ex.Message}\nStackTrace: {ex.StackTrace}";
                    _sessionService.LogError("Tag Generation", errorDetails);
                    
                    var userMessage = ParseErrorMessage(ex);
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        aiMsg.IsThinking = false;
                        aiMsg.Content = $"Generation Failed\n\n{userMessage}";
                    });
                }
            });
            
            IsGenerating = false;
            _cts?.Dispose();
            _cts = null;
            
            SaveHistory();
        }

        private string ParseErrorMessage(Exception ex)
        {
            var message = ex.Message;
            
            // Try to extract JSON from provider error messages
            // Most providers format as: "Provider API Error: {json}"
            var jsonStart = message.IndexOf("{");
            if (jsonStart >= 0)
            {
                try
                {
                    var jsonPart = message.Substring(jsonStart);
                    dynamic errorObj = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonPart);
                    
                    // Try various common API error formats
                    string apiError = null;
                    
                    // OpenAI/OpenRouter format: { "error": { "message": "..." } }
                    if (errorObj?.error?.message != null)
                        apiError = (string)errorObj.error.message;
                    
                    // Gemini format: { "error": { "message": "..." } }
                    else if (errorObj?.error?.message != null)
                        apiError = (string)errorObj.error.message;
                    
                    // HuggingFace format: { "error": "..." }
                    else if (errorObj?.error != null && errorObj.error is string)
                        apiError = (string)errorObj.error;
                    
                    // Groq/some others: { "message": "..." }
                    else if (errorObj?.message != null)
                        apiError = (string)errorObj.message;
                    
                    // Ollama format: { "error": "..." }
                    else if (errorObj?.error != null)
                        apiError = errorObj.error.ToString();
                    
                    if (!string.IsNullOrWhiteSpace(apiError))
                        return apiError;
                }
                catch
                {
                    // JSON parsing failed, continue to fallback
                }
            }
            
            // Fallback: Extract first meaningful line
            var lines = message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Skip provider prefixes
                if (trimmed.Contains("API Error:")) continue;
                if (trimmed.StartsWith("HTTP ")) continue;
                if (!string.IsNullOrWhiteSpace(trimmed))
                    return trimmed;
            }
            
            return "An error occurred. Check the Logs tab for details.";
        }

        private System.Threading.CancellationTokenSource? _cts;

        [RelayCommand]
        private void StopGeneration()
        {
            _cts?.Cancel();
        }

        [RelayCommand]
        private void ClearHistory()
        {
            Messages.Clear();
            _historyService.ClearHistory("generator.json");
        }

        [RelayCommand]
        private async Task CopyMessage(string content)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard != null && !string.IsNullOrEmpty(content))
                {
                    await clipboard.SetTextAsync(content);
                    _mainViewModel?.ShowNotification("Copied Message!", false);
                }
            }
        }
    }
}
