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
using Avalonia.Input;

using Avalonia.Media.Imaging; // For Bitmap
using System.IO;

namespace TagForge.ViewModels
{
    public partial class GenerationViewModel : ViewModelBase
    {
        private readonly SessionService _sessionService;
        private readonly MainViewModel? _mainViewModel;
        private readonly ProviderFactory _providerFactory;
        private readonly HistoryService _historyService;
        private System.Threading.CancellationTokenSource? _cts;

        // Default prompt to auto-insert when attaching an image
        private const string NaturalVisionPrompt = "Describe every aspect of this image.";
        private const string TagVisionPrompt = "Tag every aspect of this image.";
        private const string DefaultVisionPrompt = "Describe every aspect of this image."; // Fallback
        
        private static readonly char[] ErrorMessageSeparators = ['\n', '\r'];



        [ObservableProperty]
        private string _prompt = string.Empty;

        [ObservableProperty]
        private ChatSession? _currentSession;

        [ObservableProperty]
        private bool _isGenerating;

        public event Action? RequestScroll;

        public ObservableCollection<ChatMessage> Messages { get; } = [];

        public GenerationViewModel(SessionService sessionService, MainViewModel mainVm)
        {
            _sessionService = sessionService;
            _mainViewModel = mainVm;
            _providerFactory = new ProviderFactory();
            _historyService = new HistoryService();
            
            _sessionService.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(SessionService.ActiveProfile))
                {
                    CheckVisionCapabilities();
                    // Also listen to model changes inside the profile
                    if (_sessionService.ActiveProfile != null)
                    {
                        _sessionService.ActiveProfile.PropertyChanged += (ps, pe) => 
                        {
                            if (pe.PropertyName == nameof(AgentProfile.SelectedModel) || pe.PropertyName == nameof(AgentProfile.VisionOverride))
                            {
                                CheckVisionCapabilities();
                            }
                        };
                    }
                }
                else if (e.PropertyName == nameof(SessionService.ActivePersona))
                {
                     HandlePersonaChanged();
                }
            };
            
            LoadHistory();
            CheckVisionCapabilities();
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

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasImage))]
        private string? _selectedImagePath;

        [ObservableProperty]
        private Bitmap? _selectedImageBitmap;

        partial void OnSelectedImagePathChanged(string? value)
        {
            // Dispose old if needed? Avalonia Bitmaps are usually managed but good to be careful.
            // Actually, we just replace the reference. The GC/Avalonia handles the rest mostly, 
            // but Explicit disposal is good if we hold open file handles. 
            // Bitmap(path) does not lock file usually? It might.
            // Let's just load the new one.
            
            if (string.IsNullOrEmpty(value) || !System.IO.File.Exists(value))
            {
                SelectedImageBitmap = null;
            }
            else
            {
                try
                {
                    // Create bitmap
                    SelectedImageBitmap = new Bitmap(value);
                }
                catch
                {
                    SelectedImageBitmap = null;
                }
                
                // Trigger prompt update when image is attached/changed
                UpdateVisionPrompt();
            }
        }

        private void HandlePersonaChanged()
        {
            UpdateVisionPrompt();
        }

        private void UpdateVisionPrompt()
        {
            var activePersona = _sessionService.ActivePersona;
            if (activePersona == null) return;
            
            // Only auto-fill if we have an image attached
            if (!HasImage) return;

            string? newPrompt;

            if (activePersona.Name.Contains("Natural Vision")) 
            { 
                newPrompt = NaturalVisionPrompt; 
            }
            else if (activePersona.Name.Contains("Tag Vision")) 
            { 
                newPrompt = TagVisionPrompt; 
            }
            else
            {
                // Request: Other persona: remove/clear default prompts
                if (Prompt == NaturalVisionPrompt || Prompt == TagVisionPrompt || Prompt == DefaultVisionPrompt)
                {
                    Prompt = string.Empty;
                }
                return;
            }
            
            if (!string.IsNullOrEmpty(newPrompt))
            {
                 // Overwrite if empty OR if it's one of the other default prompts
                 if (string.IsNullOrWhiteSpace(Prompt) || 
                     Prompt == NaturalVisionPrompt || 
                     Prompt == TagVisionPrompt || 
                     Prompt == DefaultVisionPrompt)
                 {
                     Prompt = newPrompt;
                 }
            }
        }

        public bool HasImage => !string.IsNullOrEmpty(SelectedImagePath);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VisionToolTipText))]
        private bool _isVisionEnabled;
        
        public string VisionToolTipText => IsVisionEnabled 
            ? LocalizationService.Instance["Vision.Attach"]
            : (_sessionService.ActiveProfile?.VisionOverride == true 
                ? LocalizationService.Instance["Vision.Override"]
                : string.Format(LocalizationService.Instance["Vision.NotSupported"], _sessionService.ActiveProfile?.SelectedModel ?? "Unknown"));

        [RelayCommand]
        private async Task AttachImage()
        {
             if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
             {
                 var window = desktop.MainWindow;
                 if (window == null) return;
                 
                 var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
                 {
                     Title = "Select Image for Analysis",
                     AllowMultiple = false,
                     FileTypeFilter = [Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll]
                 };
                 
                 var result = await window.StorageProvider.OpenFilePickerAsync(options);
                 if (result != null && result.Count > 0)
                 {
                     var file = result[0];
                     if (file != null)
                     {
                         SelectedImagePath = file.Path.LocalPath;
                     }
                 }
             }
        }



        public void CheckVisionCapabilities()
        {
             if (_sessionService.ActiveProfile == null) 
             {
                 IsVisionEnabled = false;
                 return;
             }
             
             // Check Override first (Layer 2)
             if (_sessionService.ActiveProfile.VisionOverride)
             {
                 IsVisionEnabled = true;
                 return;
             }

             // Check Keywords (Layer 3)
             IsVisionEnabled = ModelCapabilities.SupportsVision(_sessionService.ActiveProfile.SelectedModel, _sessionService.ActiveProfile.Provider);
        }
        
        private async void LoadHistory()
        {
            // Clean up any empty sessions from previous app sessions
            await _historyService.CleanupEmptySessionsAsync("generator");
            
            // Create a fresh session on startup
            await CreateNewSession();
        }

        [RelayCommand]
        public async Task CreateNewSession()
        {
            if (CurrentSession != null && Messages.Count > 0)
            {
                // Save current session before creating new one
                await PerformSaveHistory();
            }

            // Service will reuse empty sessions automatically
            var newSession = await _historyService.CreateNewSessionAsync("generator");
            if (CurrentSession != null)
            {
                CurrentSession.LastModified = DateTime.Now;
                CurrentSession.MessageCount = Messages.Count;
            }
            
            // Set as current session
            CurrentSession = newSession;
            Messages.Clear(); // Start empty
            
            // Clear image and prompt
            Prompt = string.Empty;
            RemoveImage();
        }

        [RelayCommand]
        private void RemoveImage()
        {
            SelectedImagePath = null;
            
            // Clear prompt if it was a default vision prompt
            if (Prompt == NaturalVisionPrompt || Prompt == TagVisionPrompt || Prompt == DefaultVisionPrompt)
            {
               Prompt = string.Empty;
            }
        }

        private async Task PerformSaveHistory()
        {
             if (CurrentSession != null)
             {
                   await _historyService.SaveSessionAsync(CurrentSession.Id, [.. Messages], "generator");
             }
        }

        private async Task SaveHistory()
        {
            await PerformSaveHistory();
        }

        [RelayCommand]
        public async Task LoadSession(ChatSession session)
        {
            if (session == null) return;

            // Save previous if needed
            if (CurrentSession != null && CurrentSession.Id != session.Id)
            {
                await SaveHistory();
            }

            CurrentSession = session;
            
            // Load messages
            if (string.IsNullOrEmpty(CurrentSession?.Id)) return;
            var messages = await _historyService.LoadSessionMessagesAsync(CurrentSession.Id);
            Messages.Clear();
            foreach (var msg in messages)
            {
                Messages.Add(msg);
            }
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
               await SaveHistory();
               return;
            }
            if (persona == null)
            {
                 Messages.Add(new ChatMessage("Error", "No Persona selected."));
                 await SaveHistory();
                 return;
            }

            // User Message (VISIBLE - Requested Feature)
            string currentPrompt = Prompt;
            string? currentImagePath = SelectedImagePath; // Capture locally
            var userMsg = new ChatMessage("User", currentPrompt, imagePath: currentImagePath);
            Messages.Add(userMsg);

            Prompt = string.Empty;
            // Retain image until explicitly cleared or just clear it? 
            // Usually chat inputs clear after sending.
            SelectedImagePath = null; 
            
            IsGenerating = true;
            _cts = new System.Threading.CancellationTokenSource();
            
            // Placeholder for AI Response (to update iteratively)
            // Placeholder for AI Response (to update iteratively)
            var aiMsg = new ChatMessage("Assistant", "") { IsThinking = true };
            Messages.Add(aiMsg);

            var sw = Stopwatch.StartNew();

            await Task.Run(async () => 
            {
                // Consumer Task: Updates UI smoothly
                var tokenQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
                bool networkFinished = false;
                Task? displayTask = null;

                try 
                {
                    var provider = _providerFactory.CreateProvider(profile.Provider) 
                        ?? throw new Exception($"Provider '{profile.Provider}' implementation not found.");

                    string systemPrompt = persona.SystemPrompt;
                    string finalSystemMessage = systemPrompt.Contains("{input}") ? systemPrompt.Replace("{input}", currentPrompt) : systemPrompt;


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
                    
                    // Log Request
                    _sessionService.Log($"Generating with Model: {profile.SelectedModel} (Vision: {currentImagePath != null})", LogLevel.Info);
                    
                    // Producer Loop: Fetches from Network
                    void Debugger(string title, string msg, bool isSuccess)
                    {
                        var level = isSuccess ? LogLevel.Success : LogLevel.Info;
                        if (title.Contains("Request")) level = LogLevel.ApiRequest;
                        else if (title.Contains("Response")) level = LogLevel.ApiResponse;
                        else if (title.Contains("Error") || title.Contains("Failed")) level = LogLevel.Error;

                        if (!string.IsNullOrWhiteSpace(msg))
                            _sessionService.Log($"{title}: {msg}", level);
                    }

                    await foreach (var (token, isReasoning) in provider.GenerateStreamingAsync(finalSystemMessage, currentPrompt, profile.SelectedModel, profile.ApiKey, profile.EndpointUrl, imagePath: currentImagePath, maxTokens: profile.MaxTokens, cancellationToken: _cts.Token, logger: Debugger))
                    {
                         if (isReasoning)
                         {
                             await Dispatcher.UIThread.InvokeAsync(() => 
                             {
                                 aiMsg.IsThinking = true;
                                 aiMsg.Reasoning += token;
                             });
                         }
                         else
                         {
                             if (!string.IsNullOrEmpty(token))
                             {
                                 tokenQueue.Enqueue(token);
                             }
                         }
                    }
                    
                    networkFinished = true;
                    await displayTask; 

                    // Fallback for empty responses
                    if (string.IsNullOrEmpty(aiMsg.Content) && !aiMsg.IsError)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => 
                        {
                             if (!string.IsNullOrEmpty(aiMsg.Reasoning))
                             {
                                 aiMsg.Content = "The model completed its thinking process but did not provide a final response. This often happens if the request was filtered or the model failed to generate tags for this specific file.";
                             }
                             else
                             {
                                 aiMsg.Content = "The model returned an empty response. This might be due to content filtering or a failure to process the input file. Try another model or check the API logs.";
                             }
                        });
                    }

                    sw.Stop();
                    _sessionService.LastLatency = sw.ElapsedMilliseconds;
                }
                catch (OperationCanceledException)
                {
                    networkFinished = true; // Ensure display task finishes
                    
                    // Check if IT WAS user cancelled
                    if (_cts != null && _cts.IsCancellationRequested)
                    {
                        Dispatcher.UIThread.Invoke(() => 
                        {
                            aiMsg.Content += "\n\n[Generation Stopped]";
                        });
                    }
                    else
                    {
                        // Unexpected Timeout
                         _sessionService.LogError("Tag Generation", "Operation Timed Out (Server did not respond in time).");
                         Dispatcher.UIThread.Invoke(() => 
                         {
                            aiMsg.Content += "\n\n[Error: Server Timeout - Check logs]";
                         });
                    }
                }
                catch (Exception ex)
                {
                    networkFinished = true;
                    var errorDetails = $"Exception Type: {ex.GetType().Name}\nMessage: {ex.Message}\nStackTrace: {ex.StackTrace}";
                    _sessionService.LogError("Tag Generation", errorDetails);
                    
                    var userMessage = ParseErrorMessage(ex);
                    Dispatcher.UIThread.Invoke(() =>
                    {
                         // Sanitize user message
                        aiMsg.IsError = true;
                        aiMsg.Content += $"\n\n{userMessage}";
                    });
                }
                finally
                {
                    // GUARANTEED CLEANUP
                    networkFinished = true;
                    Dispatcher.UIThread.Invoke(() => {
                        aiMsg.IsThinking = false;
                    });
                }
            });
            
            IsGenerating = false;
            _cts?.Dispose();
            _cts = null;
            
            await SaveHistory();
        }

        private static string ParseErrorMessage(Exception ex)
        {
            var message = ex.Message;
            
            // Try to extract JSON from provider error messages
            // Most providers format as: "Provider API Error: {json}"
            var jsonStart = message.IndexOf('{');
            if (jsonStart >= 0)
            {
                try
                {
                    var jsonPart = message[jsonStart..];
                    dynamic? errorObj = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonPart);
                    
                    // Try various common API error formats
                    string? apiError = null;
                    
                    // OpenAI/OpenRouter format: { "error": { "message": "..." } }
                    if (errorObj?.error?.message != null)
                        apiError = errorObj.error.message?.ToString();
                    
                    // Gemini format: { "error": { "message": "..." } }
                    else if (errorObj?.error?.message != null)
                        apiError = errorObj.error.message?.ToString();
                    
                    // HuggingFace format: { "error": "..." }
                    object? hfErr = errorObj?.error;
                    if (hfErr != null && hfErr is string)
                        apiError = hfErr.ToString();
                    
                    // Some providers: { "message": "..." }
                    else if (errorObj?.message != null)
                        apiError = errorObj.message?.ToString();
                    
                    // Ollama format: { "error": "..." }
                    else if (errorObj?.error != null)
                        apiError = errorObj.error?.ToString();
                    
                    if (!string.IsNullOrWhiteSpace(apiError))
                        return apiError;
                }
                catch
                {
                    // JSON parsing failed, continue to fallback
                }
            }
            
            // Fallback: Extract first meaningful line
            var lines = message.Split(ErrorMessageSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Skip provider prefixes
                if (trimmed.Contains("API Error:")) continue;
                if (trimmed.StartsWith("HTTP ")) continue;
                if (!string.IsNullOrWhiteSpace(trimmed))
                    return trimmed;
            }
            
            return "An error occurred.";
        }

        [RelayCommand]
        private void StopGeneration()
        {
            _cts?.Cancel();
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
