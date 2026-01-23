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
        private AgentProfile _activeProfile;

        [ObservableProperty]
        private ObservableCollection<Persona> _personas = new();

        [ObservableProperty]
        private Persona _activePersona;

        [ObservableProperty]
        private long _lastLatency; // In ms
        
        // Status Bar properties
        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isBusy;

        public SessionService(SettingsService settingsService)
        {
            // Load Personas from Settings
            foreach (var p in settingsService.CurrentSettings.SavedPersonas)
            {
                Personas.Add(new Persona(p.Name, p.SystemPrompt));
            }
            
            // Restore Active Persona
            var lastPersona = settingsService.CurrentSettings.LastSelectedPersonaName;
            ActivePersona = Personas.FirstOrDefault(p => p.Name == lastPersona) ?? Personas.FirstOrDefault();
            
            Log("Session Started", LogLevel.Info);
        }
        
        // Design-time / fallback
        public SessionService() { }

        // LOGGING
        public ObservableCollection<LogEntry> Logs { get; } = new();

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => 
            {
                Logs.Add(new LogEntry(message, level));
                if (Logs.Count > 1000) Logs.RemoveAt(0); // Cap size
            });
        }

        public void LogError(string context, string errorDetails)
        {
            var message = $"{context}: {errorDetails}";
            Log(message, LogLevel.Error);
        }
    }

    public enum LogLevel { Info, Warning, Error }

    public class LogEntry
    {
        public string Timestamp { get; }
        public string Message { get; }
        public LogLevel Level { get; }
        
        public string Color => Level switch 
        {
            LogLevel.Info => "#CCCCCC",
            LogLevel.Warning => "#FFC107",
            LogLevel.Error => "#F44336",
            _ => "#FFFFFF"
        };
        
        public LogEntry(string message, LogLevel level)
        {
            Timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            Message = message;
            Level = level;
        }
    }
}
