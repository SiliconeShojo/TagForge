using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TagForge.Services;
using Avalonia;
using System.Threading.Tasks;
// using Avalonia.Toolkit.Clipboard;

namespace TagForge.ViewModels
{
    public partial class LogViewModel : ViewModelBase
    {
        private readonly SessionService _sessionService;

        public ObservableCollection<LogEntry> Logs => _sessionService.Logs;

        public LogViewModel(SessionService sessionService)
        {
            _sessionService = sessionService;
        }

        // Design-time
        public LogViewModel() : this(new SessionService()) { }

        [RelayCommand]
        private void ClearLogs()
        {
            _sessionService.Logs.Clear();
        }

        [RelayCommand]
        private async Task CopyLog(LogEntry entry)
        {
             if (entry == null) return;
             
             if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
             {
                 var clip = desktop.MainWindow?.Clipboard;
                 if (clip != null) 
                 {
                     var text = $"[{entry.Timestamp}] {entry.Level}: {entry.Message}";
                     await clip.SetTextAsync(text);
                 }
             }
        }
    }
}
