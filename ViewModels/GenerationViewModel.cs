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
        
        private void LoadHistory()
        {
            var history = _historyService.LoadHistory("generator.json");
            Messages.Clear();
            foreach (var msg in history)
            {
                Messages.Add(msg);
            }
        }

        private void SaveHistory()
        {
            _historyService.SaveHistory("generator.json", Messages);
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
            
            // Placeholder for AI Response (to update iteratively)
            // Placeholder for AI Response (to update iteratively)
            var aiMsg = new ChatMessage("Assistant", "");
            aiMsg.IsThinking = true;
            Messages.Add(aiMsg);

            var sw = Stopwatch.StartNew();

            await Task.Run(async () => 
            {
                try 
                {
                    var provider = _providerFactory.CreateProvider(profile.Provider);
                    if (provider == null) throw new Exception($"Provider '{profile.Provider}' implementation not found.");

                    string systemPrompt = persona.SystemPrompt;
                    string finalSystemMessage = systemPrompt.Contains("{input}") ? systemPrompt.Replace("{input}", currentPrompt) : systemPrompt;

                    bool isThinking = false;
                    
                    // Consumer Task: Updates UI smoothly
                    var tokenQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
                    bool networkFinished = false;

                    var displayTask = Task.Run(async () => 
                    {
                        while (!networkFinished || !tokenQueue.IsEmpty)
                        {
                            if (tokenQueue.TryDequeue(out var str))
                            {
                                // Batching
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
                                
                                await Task.Delay(15); 
                            }
                            else
                            {
                                 await Task.Delay(10);
                            }
                        }
                    });
                    
                    // Producer Loop: Fetches from Network
                    await foreach (var token in provider.GenerateStreamingAsync(finalSystemMessage, currentPrompt, profile.SelectedModel, profile.ApiKey, profile.EndpointUrl))
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
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Invoke(() => 
                        Messages.Add(new ChatMessage("Error", "Generation Failed", ex.Message)));
                }
            });
            
            IsGenerating = false;
            SaveHistory(); // Save on UI thread (ObservableCollection safe?)
            // Messages modified on UI thread, so SaveHistory strictly on UI thread is safer.
            // But here we are ending async method. Task.Run awaits, then we are back on UI context? 
            // RelayCommand is async void/Task. It resumes on context.
            // Yes.
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
