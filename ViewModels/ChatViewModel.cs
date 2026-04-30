using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using TagForge.Services;
using TagForge.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using System.Text;
using Avalonia.Input;

namespace TagForge.ViewModels
{
    public partial class ChatViewModel : ViewModelBase
    {
        private readonly SessionService _sessionService;
        private readonly MainViewModel? _mainViewModel;
        private readonly ProviderFactory _providerFactory;
        private readonly HistoryService _historyService; // Reuse history? Or separate chat history? Let's assume shared for now or separate. User requested separate Tab.

        [ObservableProperty]
        private string? _attachedFilePath;

        [ObservableProperty]
        private string? _attachedFileName;

        [ObservableProperty]
        private bool _isImageAttached;

        [ObservableProperty]
        private bool _isVisionWarningVisible;

        [ObservableProperty]
        private string _prompt = string.Empty;

        [ObservableProperty]
        private bool _isGenerating;

        public event Action? RequestScroll;

        // Session management
        [ObservableProperty]
        private ChatSession? _currentSession;
        
        // Track which session is actively generating (for background generation)
        private string? _generatingSessionId = null;
        private ChatMessage? _generatingMessage = null;
        private DateTime _lastBackgroundSave = DateTime.MinValue;

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public ObservableCollection<ChatRule> ChatRules => _sessionService.ChatRules;

        public ChatRule ActiveChatRule
        {
            get => _sessionService.ActiveChatRule;
            set 
            {
                _sessionService.ActiveChatRule = value;
                OnPropertyChanged(nameof(ActiveChatRule));
            }
        }

        public ChatViewModel(SessionService sessionService, MainViewModel mainVm)
        {
            _sessionService = sessionService;
            _mainViewModel = mainVm;
            _providerFactory = new ProviderFactory();
            _historyService = new HistoryService(); 
            _ = InitializeAsync();
        }

        public ChatViewModel()
        {
             // Design time
             var settings = new SettingsService();
             _sessionService = new SessionService(settings);
             _providerFactory = new ProviderFactory();
             _historyService = new HistoryService();
        }

        private bool IsVisionModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            var lower = modelName.ToLower();
            return lower.Contains("vision") || 
                   lower.Contains("llava") || 
                   lower.Contains("gemini") || 
                   lower.Contains("gpt-4o") || 
                   lower.Contains("pixtral") || 
                   lower.Contains("moondream");
        }

        [RelayCommand]
        private async Task PickFile()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Attach File",
                    AllowMultiple = false,
                    FileTypeFilter = new[] 
                    { 
                        new Avalonia.Platform.Storage.FilePickerFileType("All Supported") 
                        { 
                            Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.txt", "*.md", "*.json", "*.cs", "*.axaml", "*.xml", "*.js", "*.ts", "*.py", "*.html", "*.css", "*.yaml", "*.yml", "*.toml" } 
                        },
                        new Avalonia.Platform.Storage.FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp" } },
                        new Avalonia.Platform.Storage.FilePickerFileType("Text & Code") { Patterns = new[] { "*.txt", "*.md", "*.json", "*.cs", "*.axaml", "*.xml", "*.js", "*.ts", "*.py", "*.html", "*.css", "*.yaml", "*.yml", "*.toml" } }
                    }
                });

                if (files.Count > 0)
                {
                    var file = files[0];
                    AttachedFilePath = file.Path.LocalPath;
                    AttachedFileName = file.Name;
                    
                    var ext = System.IO.Path.GetExtension(AttachedFileName).ToLower();
                    IsImageAttached = ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp";
                    
                    if (IsImageAttached)
                    {
                        var profile = _sessionService.ActiveProfile;
                        IsVisionWarningVisible = profile != null && !IsVisionModel(profile.SelectedModel);
                    }
                    else
                    {
                        IsVisionWarningVisible = false;
                    }
                }
            }
        }

        [RelayCommand]
        private void RemoveAttachment()
        {
            AttachedFilePath = null;
            AttachedFileName = null;
            IsImageAttached = false;
            IsVisionWarningVisible = false;
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(Prompt) && string.IsNullOrEmpty(AttachedFilePath)) return;
            
            var profile = _sessionService.ActiveProfile;
            
            if (profile == null) 
            {
               Messages.Add(new ChatMessage("Error", "No Active Agent selected.", "Please select an agent in the Agent Manager."));
               return;
            }

            // Auto-create session if none exists
            if (CurrentSession == null)
            {
                await CreateNewSessionCommand.ExecuteAsync(null);
            }

            string finalPrompt = Prompt ?? string.Empty;
            string? imagePath = null;

            // Process attachment
            if (!string.IsNullOrEmpty(AttachedFilePath))
            {
                if (IsImageAttached)
                {
                    imagePath = AttachedFilePath;
                }
                else
                {
                    try
                    {
                        var content = await System.IO.File.ReadAllTextAsync(AttachedFilePath);
                        finalPrompt = $"[Attached File: {AttachedFileName}]\n```\n{content}\n```\n\n{finalPrompt}";
                    }
                    catch (Exception ex)
                    {
                        Messages.Add(new ChatMessage("Error", $"Failed to read file: {AttachedFileName}", ex.Message));
                        return;
                    }
                }
            }

            // User Message (VISIBLE)
            var userMsg = new ChatMessage("User", Prompt ?? string.Empty, imagePath: imagePath);
            Messages.Add(userMsg);
            
            Prompt = string.Empty;
            RemoveAttachment(); // Clear after sending
            
            // Save immediately after user message to prevent loss
            await SaveHistory();
            
            IsGenerating = true;
            
            // Track which session is generating (for background generation support)
            _generatingSessionId = CurrentSession?.Id;
            
            _cts = new System.Threading.CancellationTokenSource();
            
            var aiMsg = new ChatMessage("Assistant", "");
            
            aiMsg.IsThinking = true;
             
            Messages.Add(aiMsg);
            
            // Store reference for background updates
            _generatingMessage = aiMsg;

            var sw = Stopwatch.StartNew();
            
            try 
            {
                // Background Thread for heavy lifting
                await Task.Run(async () => 
                {
                    // Consumer Task: Updates UI smoothly with batching
                    var tokenQueue = new System.Collections.Concurrent.ConcurrentQueue<(string Token, bool IsReasoning)>();
                    bool networkFinished = false;
                    Task? displayTask = null;

                try 
                {
                    var provider = _providerFactory.CreateProvider(profile.Provider);
                    if (provider == null) throw new Exception($"Provider '{profile.Provider}' implementation not found.");

                    // Standard Chat system prompt
                    string systemPrompt = ActiveChatRule?.Instruction ?? "You are a helpful AI assistant.";
                    
                    displayTask = Task.Run(async () => 
                    {
                        while (!networkFinished || !tokenQueue.IsEmpty)
                        {
                            if (tokenQueue.TryDequeue(out var part))
                            {
                                var sbContent = new StringBuilder();
                                var sbReasoning = new StringBuilder();
                                
                                if (part.IsReasoning) sbReasoning.Append(part.Token);
                                else sbContent.Append(part.Token);

                                // Batching
                                int count = 0;
                                while (count < 20 && tokenQueue.TryDequeue(out var extra))
                                {
                                    if (extra.IsReasoning) sbReasoning.Append(extra.Token);
                                    else sbContent.Append(extra.Token);
                                    count++;
                                }

                                var batchContent = sbContent.ToString();
                                var batchReasoning = sbReasoning.ToString();
                                
                                // Check if we're generating in background (different session is active)
                                bool isBackground = _generatingSessionId != null && 
                                                   CurrentSession?.Id != _generatingSessionId;
                                
                                if (isBackground)
                                {
                                    // Background mode: update message content directly without UI
                                    if (_generatingMessage != null)
                                    {
                                        _generatingMessage.Content += batchContent;
                                        
                                        // Debounced save to disk (every 2 seconds)
                                        if (DateTime.Now - _lastBackgroundSave > TimeSpan.FromSeconds(2))
                                        {
                                            var sessionId = _generatingSessionId;
                                            if (string.IsNullOrEmpty(sessionId)) return;
                                            var currentContent = _generatingMessage?.Content ?? string.Empty;
                                            
                                            // Save to generating session in background
                                            _ = Task.Run(async () =>
                                            {
                                                try
                                                {
                                                    // Load current session messages
                                                    var sessionMessages = await _historyService.LoadSessionMessagesAsync(sessionId);
                                                    
                                                    // Update the AI message with latest content
                                                    var aiMessage = sessionMessages.LastOrDefault(m => m.Role == "Assistant");
                                                    if (aiMessage != null)
                                                    {
                                                        aiMessage.Content = currentContent;
                                                    }
                                                    
                                                    // Save back to disk
                                                    await _historyService.SaveSessionAsync(sessionId, sessionMessages, "chat");
                                                }
                                                catch { /* Background save failed, will try again */ }
                                            });
                                            _lastBackgroundSave = DateTime.Now;
                                        }
                                    }
                                }
                                else
                                {
                                    // Foreground mode: normal UI updates
                                    await Dispatcher.UIThread.InvokeAsync(() => 
                                    {
                                        if (!string.IsNullOrEmpty(batchReasoning))
                                        {
                                            aiMsg.IsThinking = true;
                                            aiMsg.Reasoning += batchReasoning;
                                        }
                                        
                                        if (!string.IsNullOrEmpty(batchContent))
                                        {
                                            if (aiMsg.IsThinking) aiMsg.IsThinking = false;
                                            aiMsg.Content += batchContent;
                                        }
                                        RequestScroll?.Invoke();
                                    });
                                }
                                
                                await Task.Delay(15); // Smooth type delay
                            }
                            else
                            {
                                 await Task.Delay(10);
                            }
                        }
                    });
                    
                    // Log Request
                    _sessionService.Log($"Chat Request (Model: {profile.SelectedModel})", LogLevel.Info);

                    // Producer Loop: Fetches from Network
                    Action<string, string, bool> debugger = (title, msg, isSuccess) => 
                    {
                        var level = isSuccess ? LogLevel.Success : LogLevel.Info;
                        if (title.Contains("Request")) level = LogLevel.ApiRequest;
                        else if (title.Contains("Response")) level = LogLevel.ApiResponse;
                        else if (title.Contains("Error") || title.Contains("Failed")) level = LogLevel.Error;

                        if (!string.IsNullOrWhiteSpace(msg))
                            _sessionService.Log($"{title}: {msg}", level);
                    };

                    bool firstToken = true;
                     await foreach (var (token, isReasoning) in provider.GenerateStreamingAsync(systemPrompt, finalPrompt, profile.SelectedModel, profile.ApiKey, profile.EndpointUrl, imagePath: imagePath, maxTokens: profile.MaxTokens, cancellationToken: _cts.Token, logger: debugger))
                     {
                          if (firstToken)
                          {
                              firstToken = false;
                              _ = Dispatcher.UIThread.InvokeAsync(() => 
                              {
                                  aiMsg.IsLoadingModel = false;
                              });
                          }
                          
                          string tempToken = token;
                          bool tempIsReasoning = isReasoning;
                          
                          // Manual tag detection for models that don't use reasoning fields but use markers
                          if (tempToken.Contains("<think>")) 
                          {
                              tempIsReasoning = true;
                              _ = Dispatcher.UIThread.InvokeAsync(() => aiMsg.IsThinking = true);
                          }
                          
                          if (tempToken.Contains("</think>"))
                          {
                              tempIsReasoning = true; // Still part of reasoning marker
                              _ = Dispatcher.UIThread.InvokeAsync(() => aiMsg.IsThinking = false);
                          } 

                          if (!string.IsNullOrEmpty(tempToken))
                          {
                               tokenQueue.Enqueue((tempToken, tempIsReasoning));
                          }
                     }
                    
                    networkFinished = true;
                    await displayTask;

                    // Fallback for empty responses (common with image processing failures on some models)
                    if (string.IsNullOrEmpty(aiMsg.Content) && !aiMsg.IsError)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => 
                        {
                             if (!string.IsNullOrEmpty(aiMsg.Reasoning))
                             {
                                 aiMsg.Content = "The model completed its thinking process but did not provide a final response. This often happens if the model believes the request violates its safety guidelines or if it's specialized for reasoning only.";
                             }
                             else
                             {
                                 aiMsg.Content = "The model returned an empty response. This might be due to content filtering, model unavailability, or a failure to process the specific input (e.g., an unsupported file format).";
                             }
                        });
                    }

                    sw.Stop();
                    _sessionService.LastLatency = sw.ElapsedMilliseconds;
                }
                catch (OperationCanceledException)
                {
                    // Graceful cancellation
                    networkFinished = true;
                    Dispatcher.UIThread.Invoke(() => 
                         Messages.Add(new ChatMessage("System", "Generation Stopped", "")));
                }
                catch (Exception ex)
                {
                    networkFinished = true;
                    var errorDetails = $"Exception Type: {ex.GetType().Name}\nMessage: {ex.Message}\nStackTrace: {ex.StackTrace}";
                    _sessionService.LogError("Chat Generation", errorDetails);
                    
                    // Sanitize user message
                    var rawError = ParseErrorMessage(ex);
                    Dispatcher.UIThread.Invoke(() => 
                    {
                        aiMsg.IsThinking = false;
                        aiMsg.IsError = true;
                        
                        if (string.IsNullOrWhiteSpace(aiMsg.Content))
                        {
                            aiMsg.Content = rawError;
                        }
                        else
                        {
                            aiMsg.Content += $"\n\n---\n**[Error during generation]**\n{rawError}";
                        }
                    });
                }
                });
            }
            finally
            {
                IsGenerating = false;
                _cts?.Dispose();
                _cts = null;
                
                // key fix: ensure UI state is clean even if error occurred
                await Dispatcher.UIThread.InvokeAsync(() => 
                {
                    aiMsg.IsLoadingModel = false;
                    aiMsg.IsThinking = false;
                });
                
                await SaveHistory();
                
                // Clear generation tracking (background generation complete)
                _generatingSessionId = null;
                _generatingMessage = null;
            }
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
            
            return "An error occurred.";
        }

        private System.Threading.CancellationTokenSource? _cts;

        [RelayCommand]
        private void StopGeneration()
        {
            _cts?.Cancel();
        }

        [RelayCommand]
        private async Task CreateNewSession()
        {
            // Service will reuse empty sessions automatically
            var newSession = await _historyService.CreateNewSessionAsync("chat");
            await LoadSessionCommand.ExecuteAsync(newSession);
        }
        
        [RelayCommand]
        private async Task LoadSession(ChatSession session)
        {
            if (session == null) return;
            
            // REMOVED: Don't cancel generation - let it continue in background
            // Generation will keep running and saving to the generating session
            
            // CRITICAL: Save current session before switching to prevent data loss
            if (CurrentSession != null && CurrentSession.Id != session.Id)
            {
                await SaveHistory();
            }
            
            if (CurrentSession != null)
                CurrentSession.IsActive = false;
                
            session.IsActive = true;
            CurrentSession = session;
            
            // Load messages
            if (CurrentSession?.Id == null) return;
            var messages = await _historyService.LoadSessionMessagesAsync(CurrentSession.Id);
            Messages.Clear();
            foreach (var msg in messages)
                Messages.Add(msg);
                
            // If this session is generating in background, we just loaded stale data
            // Reload to show latest progress
            if (_generatingSessionId == session.Id && IsGenerating)
            {
                // Give background a moment to save latest
                await Task.Delay(100);
                var updatedMessages = await _historyService.LoadSessionMessagesAsync(session.Id);
                Messages.Clear();
                foreach (var msg in updatedMessages)
                    Messages.Add(msg);
            }
        }
        


        private async Task InitializeAsync()
        {
            // Check for migration first
            if (await _historyService.NeedsMigrationAsync("assistant_chat.json"))
            {
                var migratedSession = await _historyService.MigrateOldHistoryAsync("assistant_chat.json", "chat");
                if (migratedSession != null)
                {
                    _mainViewModel?.ShowNotification("Chat history organized into sessions!", false);
                }
            }

            // Clean up any empty sessions from previous app sessions
            await _historyService.CleanupEmptySessionsAsync("chat");

            // Create a fresh session on startup
            await CreateNewSessionCommand.ExecuteAsync(null);
        }
        

        
        private async Task SaveHistory()
        {
            if (CurrentSession != null)
            {
                await _historyService.SaveSessionAsync(CurrentSession.Id, Messages.ToList(), "chat");
                
                if (CurrentSession != null)
                {
                    CurrentSession.LastModified = DateTime.Now;
                    CurrentSession.MessageCount = Messages.Count;
                    CurrentSession.Title = _historyService.GenerateTitleFromMessages(Messages.ToList());
                }
            }
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
                    _mainViewModel?.ShowNotification("Copied!", false);
                }
            }
        }
    }
}
