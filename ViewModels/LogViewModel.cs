using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TagForge.Services;
using TagForge.Models;
using Avalonia;
using System.Threading.Tasks;
// using Avalonia.Toolkit.Clipboard;

namespace TagForge.ViewModels
{
    public partial class LogViewModel : ViewModelBase
    {
        private readonly SessionService _sessionService;
        private readonly MainViewModel _mainViewModel;

        public ObservableCollection<LogEntry> Logs => _sessionService.Logs;

        public LogViewModel(SessionService sessionService, MainViewModel mainVm)
        {
            _sessionService = sessionService;
            _mainViewModel = mainVm;
        }

        // Design-time
        public LogViewModel() : this(new SessionService(), null!) { }

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
                     if (!string.IsNullOrEmpty(entry.Details))
                     {
                         text += $"\nDetails:\n{entry.Details}";
                     }
                     await clip.SetTextAsync(text);
                     _mainViewModel?.ShowNotification("Copied Log Entry!", false);
                 }
             }
        }

        [RelayCommand]
        private void ToggleExpand(LogEntry entry)
        {
            if (entry != null && !string.IsNullOrEmpty(entry.Details))
            {
                entry.IsExpanded = !entry.IsExpanded;
            }
        }

        [RelayCommand]
        private async Task ExportLogs()
        {
            if (_sessionService.Logs.Count == 0) return;

            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var storage = desktop.MainWindow?.StorageProvider;
                if (storage == null) return;

                var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Export Logs",
                    DefaultExtension = "txt",
                    SuggestedFileName = $"TagForge_Logs_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt",
                    FileTypeChoices = new[] 
                    { 
                        new Avalonia.Platform.Storage.FilePickerFileType("Text File") { Patterns = new[] { "*.txt" } } 
                    }
                });

                if (file != null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"TagForge Logs - Exported {System.DateTime.Now}");
                    sb.AppendLine("--------------------------------------------------");
                    foreach (var log in _sessionService.Logs)
                    {
                        sb.AppendLine($"[{log.Timestamp}] {log.Level}: {log.Message}");
                        if (!string.IsNullOrEmpty(log.Details))
                        {
                            sb.AppendLine(log.Details);
                        }
                        sb.AppendLine("-");
                    }

                    using var stream = await file.OpenWriteAsync();
                    using var writer = new System.IO.StreamWriter(stream);
                    await writer.WriteAsync(sb.ToString());
                }
            }
        }
    }
}
