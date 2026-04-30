using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using TagForge.ViewModels;
using TagForge.Models;
using System.Linq;

namespace TagForge.Services
{
    public partial class SessionService : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<AgentProfile> _profiles = new();

        [ObservableProperty]
        private AgentProfile _activeProfile = null!;

        [ObservableProperty]
        private ObservableCollection<Persona> _personas = new();

        [ObservableProperty]
        private Persona _activePersona = null!;

        [ObservableProperty]
        private ObservableCollection<ChatRule> _chatRules = new();

        [ObservableProperty]
        private ChatRule _activeChatRule = null!;

        [ObservableProperty]
        private long _lastLatency; // In ms
        
        // Status Bar properties
        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isBusy;

        private readonly SettingsService _settingsService = null!;

        public SessionService(SettingsService settingsService)
        {
            _settingsService = settingsService;
            
            // Load Personas from Settings
            foreach (var p in settingsService.CurrentSettings.SavedPersonas)
            {
                Personas.Add(new Persona(p.Name, p.SystemPrompt, isReadOnly: p.IsReadOnly));
            }
            
            // Restore Active Persona
            var lastPersona = settingsService.CurrentSettings.LastSelectedPersonaName;
            ActivePersona = (Personas.FirstOrDefault(p => p.Name == lastPersona) ?? Personas.FirstOrDefault())!;

            // Load Chat Rules
            foreach (var r in settingsService.CurrentSettings.SavedChatRules)
            {
                ChatRules.Add(new ChatRule(r.Name, r.Instruction, isReadOnly: r.IsReadOnly));
            }
            // Restore Active Chat Rule
            var lastRule = settingsService.CurrentSettings.LastSelectedChatRuleName;
            ActiveChatRule = (ChatRules.FirstOrDefault(r => r.Name == lastRule) ?? ChatRules.FirstOrDefault())!;
        }

        partial void OnActivePersonaChanged(Persona value)
        {
            // Save selected persona to settings
            if (_settingsService != null && value != null)
            {
                _settingsService.CurrentSettings.LastSelectedPersonaName = value.Name;
                _settingsService.SaveSettings();
            }
        }

        partial void OnActiveChatRuleChanged(ChatRule value)
        {
            if (_settingsService != null && value != null)
            {
                _settingsService.CurrentSettings.LastSelectedChatRuleName = value.Name;
                _settingsService.SaveSettings();
            }
        }
        
        // Design-time / fallback
        public SessionService() { }

        // LOGGING
        public ObservableCollection<LogEntry> Logs { get; } = new();

        public void Log(string message, LogLevel level = LogLevel.Info, string? details = null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => 
            {
                Logs.Add(new LogEntry(message, level, details));
                if (Logs.Count > 1000) Logs.RemoveAt(0); // Cap size
            });
        }

        public void LogError(string context, string errorDetails)
        {
            Log($"{context} Failed", LogLevel.Error, errorDetails);
        }

        public void LogApi(string title, string content, bool isResponse)
        {
            Log(title, isResponse ? LogLevel.ApiResponse : LogLevel.ApiRequest, content);
        }
    }


}
