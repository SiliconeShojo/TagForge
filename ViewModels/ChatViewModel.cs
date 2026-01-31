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

namespace TagForge.ViewModels
{
    public partial class ChatViewModel : ViewModelBase
    {
        private readonly SessionService _sessionService;
        private readonly MainViewModel _mainViewModel;
        private readonly ProviderFactory _providerFactory;
        private readonly HistoryService _historyService; // Reuse history? Or separate chat history? Let's assume shared for now or separate. User requested separate Tab.

        [ObservableProperty]
        private string _prompt;

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
             _sessionService = new SessionService();
             _providerFactory = new ProviderFactory();
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(Prompt)) return;
            
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

            // User Message (VISIBLE)
            var userMsg = new ChatMessage("User", Prompt);
            Messages.Add(userMsg);
            
            Prompt = string.Empty;
            
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
                    var tokenQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
                    bool networkFinished = false;
                    Task displayTask = null;

                try 
                {
                    var provider = _providerFactory.CreateProvider(profile.Provider);
                    if (provider == null) throw new Exception($"Provider '{profile.Provider}' implementation not found.");

                    // Standard Chat system prompt
                    string systemPrompt = "You are a helpful AI assistant.";
                    bool isThinking = false;
                    
                    displayTask = Task.Run(async () => 
                    {
                        while (!networkFinished || !tokenQueue.IsEmpty)
                        {
                            if (tokenQueue.TryDequeue(out var str))
                            {
                                // Batching: consume up to 10 more tokens or 100 chars to prevent UI flood
                                var sb = new StringBuilder(str);
                                int count = 0;
                                while (count < 20 && tokenQueue.TryDequeue(out var next))
                                {
                                    sb.Append(next);
                                    count++;
                                }

                                var batchContent = sb.ToString();
                                
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
                                            var currentContent = _generatingMessage.Content;
                                            
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
                                        if (aiMsg.IsThinking) aiMsg.IsThinking = false;
                                        aiMsg.Content += batchContent;
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
                    
                    // Producer Loop
                    bool firstToken = true;
                    await foreach (var token in provider.GenerateStreamingAsync(systemPrompt, userMsg.Content, profile.SelectedModel, profile.ApiKey, profile.EndpointUrl, _cts.Token))
                    {
                         if (firstToken)
                         {
                             firstToken = false;
                             _ = Dispatcher.UIThread.InvokeAsync(() => 
                             {
                                 aiMsg.IsLoadingModel = false;
                                 if (!aiMsg.IsThinking) aiMsg.IsThinking = false; // Just to ensure clean state
                             });
                         }
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
                    
                    var userMessage = ParseErrorMessage(ex);
                    Dispatcher.UIThread.Invoke(() => 
                    {
                        aiMsg.IsThinking = false;
                        aiMsg.Content = $"Generation Failed\n\n{userMessage}";
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
                Dispatcher.UIThread.InvokeAsync(() => 
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
            var messages = await _historyService.LoadSessionMessagesAsync(session.Id);
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
                
                // Update session metadata in list
                CurrentSession.LastModified = DateTime.Now;
                CurrentSession.MessageCount = Messages.Count;
                CurrentSession.Title = _historyService.GenerateTitleFromMessages(Messages.ToList());
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
