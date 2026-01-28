using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using TagForge.Services;
using TagForge.Models;
using System;
using System.Collections.ObjectModel;
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

        // Separate collection for this chat session? 
        // Or reuse existing Message model but in a new collection? 
        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public ChatViewModel(SessionService sessionService, MainViewModel mainVm)
        {
            _sessionService = sessionService;
            _mainViewModel = mainVm;
            _providerFactory = new ProviderFactory();
            _historyService = new HistoryService(); 
            LoadHistory();
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

            // User Message (VISIBLE)
            var userMsg = new ChatMessage("User", Prompt);
            Messages.Add(userMsg);
            
            Prompt = string.Empty;
            IsGenerating = true;
            
            
            _cts = new System.Threading.CancellationTokenSource();
            
            var aiMsg = new ChatMessage("Assistant", "");
            
            aiMsg.IsThinking = true;
             
            Messages.Add(aiMsg);

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

                                await Dispatcher.UIThread.InvokeAsync(() => 
                                {
                                    if (aiMsg.IsThinking) aiMsg.IsThinking = false;
                                    aiMsg.Content += sb.ToString();
                                    RequestScroll?.Invoke();
                                });
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
                    Dispatcher.UIThread.Invoke(() => 
                         Messages.Add(new ChatMessage("Error", "Generation Failed", ex.Message)));
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
            }
        }

        private System.Threading.CancellationTokenSource? _cts;

        [RelayCommand]
        private void StopGeneration()
        {
            _cts?.Cancel();
        }

        [RelayCommand]
        private void ClearChat()
        {
            Messages.Clear();
            _historyService.ClearHistory("assistant_chat.json");
        }

        private async void LoadHistory()
        {
            var history = await _historyService.LoadHistoryAsync("assistant_chat.json");
            Messages.Clear();
            foreach (var msg in history) Messages.Add(msg);
        }
        
        private async Task SaveHistory()
        {
            await _historyService.SaveHistoryAsync("assistant_chat.json", Messages);
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
