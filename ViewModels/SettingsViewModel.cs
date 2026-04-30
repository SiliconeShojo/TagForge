using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using TagForge.Services;
using System.Collections.Generic;
using TagForge.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Threading.Tasks;
using System.Diagnostics;
using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Platform.Storage;
using System.IO;
using System.IO.Compression;
using Avalonia.Controls.ApplicationLifetimes;
using Newtonsoft.Json;

namespace TagForge.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService;
        private readonly SessionService _sessionService;
        private readonly MainViewModel? _mainVM;
        private readonly LocalizationService _localizationService;

        public SettingsViewModel(SettingsService settingsService, SessionService sessionService, MainViewModel? mainVM = null)
        {
            _settingsService = settingsService;
            _sessionService = sessionService;
            _mainVM = mainVM;
            _localizationService = LocalizationService.Instance;
            
            // Load available languages
            AvailableLanguages = new ObservableCollection<LanguageInfo>(_localizationService.GetAvailableLanguages());
            
            // Set current language
            var currentLang = _localizationService.CurrentLanguageCode;
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == currentLang) ?? AvailableLanguages[0];
            
            if (Personas.Count > 0) 
            {
                SelectedPersonaToEdit = Personas[0];
            }

            if (ChatRules.Count > 0)
            {
                SelectedChatRuleToEdit = ChatRules[0];
            }
        }

        // Design-time
        public SettingsViewModel() 
        {
            _settingsService = new SettingsService();
            _sessionService = new SessionService();
            _localizationService = LocalizationService.Instance;
            AvailableLanguages = new ObservableCollection<LanguageInfo>(_localizationService.GetAvailableLanguages());
            SelectedLanguage = AvailableLanguages[0];
        }
        
        [SuppressMessage("Performance", "CA1822:Mark members as static")]
        public string AppVersion => "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "1.0";

        public ObservableCollection<LanguageInfo> AvailableLanguages { get; private set; }

        [ObservableProperty]
        private LanguageInfo _selectedLanguage;

        partial void OnSelectedLanguageChanged(LanguageInfo value)
        {
            if (value != null)
            {
                _localizationService.ChangeLanguage(value.Code);
                _settingsService.CurrentSettings.SelectedLanguage = value.Code;
                _settingsService.SaveSettings();
            }
        }

        public ObservableCollection<Persona> Personas => _sessionService.Personas;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeletePersonaCommand))]

        private Persona? _selectedPersonaToEdit;

        [ObservableProperty]
        private bool _isDirty;

        partial void OnSelectedPersonaToEditChanged(Persona? oldValue, Persona? newValue)
        {
             if (oldValue != null) oldValue.PropertyChanged -= OnPersonaPropertyChanged;
             if (newValue != null) newValue.PropertyChanged += OnPersonaPropertyChanged;
        }

        private void OnPersonaPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            IsDirty = true;
        }

        private bool CanDeletePersona() => SelectedPersonaToEdit != null && !SelectedPersonaToEdit.IsReadOnly;

        [RelayCommand]
        private void SavePersonas()
        {
            // Map Personas back to SettingsModel
            var list = new List<PersonaModel>();
            foreach (var p in Personas)
            {
                list.Add(new PersonaModel { Name = p.Name, SystemPrompt = p.SystemPrompt, IsReadOnly = p.IsReadOnly });
            }
            _settingsService.CurrentSettings.SavedPersonas = list;
            _settingsService.SaveSettings();
            IsDirty = false;
            
            _mainVM?.ShowNotification(_localizationService["Settings.Saved"]);
        }

        [RelayCommand]
        private void AddPersona()
        {
            var newP = new Persona("New Persona", "System instructions here...");
            Personas.Add(newP);
            SelectedPersonaToEdit = newP;
            IsDirty = true; // Adding is a modification
        }

        [RelayCommand(CanExecute = nameof(CanDeletePersona))]
        private void DeletePersona()
        {
            if (SelectedPersonaToEdit != null && !SelectedPersonaToEdit.IsReadOnly)
            {
                Personas.Remove(SelectedPersonaToEdit);
                SavePersonas(); // Deleting auto-saves for safety
            }
        }

        // Chat Rules Management
        public ObservableCollection<ChatRule> ChatRules => _sessionService.ChatRules;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteChatRuleCommand))]
        private ChatRule? _selectedChatRuleToEdit;

        [ObservableProperty]
        private bool _isChatRulesDirty;

        partial void OnSelectedChatRuleToEditChanged(ChatRule? oldValue, ChatRule? newValue)
        {
            if (oldValue != null) oldValue.PropertyChanged -= OnChatRulePropertyChanged;
            if (newValue != null) newValue.PropertyChanged += OnChatRulePropertyChanged;
        }

        private void OnChatRulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            IsChatRulesDirty = true;
        }

        private bool CanDeleteChatRule() => SelectedChatRuleToEdit != null && !SelectedChatRuleToEdit.IsReadOnly;

        [RelayCommand]
        private void SaveChatRules()
        {
            var list = new List<ChatRuleModel>();
            foreach (var r in ChatRules)
            {
                list.Add(new ChatRuleModel { Name = r.Name, Instruction = r.Instruction, IsReadOnly = r.IsReadOnly });
            }
            _settingsService.CurrentSettings.SavedChatRules = list;
            _settingsService.SaveSettings();
            IsChatRulesDirty = false;

            _mainVM?.ShowNotification(_localizationService["Settings.Saved"]);
        }

        [RelayCommand]
        private void AddChatRule()
        {
            var newR = new ChatRule("New Chat Rule", "Instruction here...");
            ChatRules.Add(newR);
            SelectedChatRuleToEdit = newR;
            IsChatRulesDirty = true;
        }

        [RelayCommand(CanExecute = nameof(CanDeleteChatRule))]
        private void DeleteChatRule()
        {
            if (SelectedChatRuleToEdit != null && !SelectedChatRuleToEdit.IsReadOnly)
            {
                ChatRules.Remove(SelectedChatRuleToEdit);
                SaveChatRules();
            }
        }

        [RelayCommand]
        private static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        [RelayCommand]
        private async Task ResetSettings()
        {
            var confirmed = await ShowConfirmationDialog(
                LocalizationService.Instance["Dialog.ResetTitle"], 
                LocalizationService.Instance["Dialog.ResetSubtitle"]);

            if (confirmed)
            {
                _mainVM?.ShowNotification("Factory Reset initiated. Clearing all data...", true);
                _settingsService.ResetSettings();
                
                // Show final status
                _sessionService.StatusMessage = "Settings Reset. Restarting...";
                
                // Brief delay to allow status to be seen/settings to save
                await Task.Delay(1000);

                // Restart app
                try
                {
                    var processPath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(processPath))
                    {
                        Process.Start(new ProcessStartInfo(processPath) { UseShellExecute = true });
                        Environment.Exit(0);
                    }
                }
                catch
                {
                    _sessionService.StatusMessage = "Reset complete. Please restart the app manually.";
                }
            }
        }
        
        [RelayCommand]
        private async Task ExportHistory()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var storage = desktop.MainWindow.StorageProvider;
                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = _localizationService["Settings.ExportHistory"],
                    SuggestedFileName = $"TagForge_History_Backup_{DateTime.Now:yyyy-MM-dd}.tfb",
                    FileTypeChoices = [new FilePickerFileType("TagForge Backup") { Patterns = ["*.tfb"] }]
                });

                if (file != null)
                {
                    try
                    {
                        var historyService = new HistoryService();
                        var backup = await historyService.CreateBackupAsync();
                        var json = JsonConvert.SerializeObject(backup);
                        
                        await using var stream = await file.OpenWriteAsync();
                        await using var gzipStream = new GZipStream(stream, CompressionLevel.Optimal);
                        await using var writer = new StreamWriter(gzipStream);
                        await writer.WriteAsync(json);
                        
                        _mainVM?.ShowNotification("History backed up successfully!");
                    }
                    catch (Exception ex)
                    {
                        _mainVM?.ShowNotification($"Backup failed: {ex.Message}", true);
                    }
                }
            }
        }

        [RelayCommand]
        private async Task ImportHistory()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var storage = desktop.MainWindow.StorageProvider;
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = _localizationService["Settings.ImportHistory"],
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("TagForge Backup") { Patterns = ["*.tfb"] }]
                });

                if (files.Any())
                {
                    try
                    {
                        await using var stream = await files[0].OpenReadAsync();
                        await using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
                        using var reader = new StreamReader(gzipStream);
                        var json = await reader.ReadToEndAsync();
                        
                        var backup = JsonConvert.DeserializeObject<HistoryBackupModel>(json);
                        if (backup != null)
                        {
                            var historyService = new HistoryService();
                            await historyService.ImportBackupAsync(backup);
                            _mainVM?.ShowNotification("History restored successfully!");
                        }
                    }
                    catch (Exception ex)
                    {
                        _mainVM?.ShowNotification($"Restore failed: {ex.Message}", true);
                    }
                }
            }
        }

        private static async Task<bool> ShowConfirmationDialog(string title, string subtitle)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 450,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
                ShowInTaskbar = false,
                CornerRadius = new CornerRadius(12)
            };

            bool result = false;

            var mainBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#333333")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Avalonia.Thickness(25)
            };

            var panel = new StackPanel
            {
                Spacing = 20,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.White)
            });

            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                Opacity = 0.7,
                Foreground = new SolidColorBrush(Color.Parse("#E0E0E0")),
                TextWrapping = TextWrapping.Wrap
            });

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 15,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Avalonia.Thickness(0, 10, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = LocalizationService.Instance["Common.Cancel"],
                Width = 110,
                Height = 40,
                CornerRadius = new CornerRadius(6),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            cancelButton.Click += (s, e) => { dialog.Close(); };

            var confirmButton = new Button
            {
                Content = LocalizationService.Instance["Common.Confirm"],
                Width = 110,
                Height = 40,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse("#D32F2F")),
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            confirmButton.Click += (s, e) => { result = true; dialog.Close(); };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);
            panel.Children.Add(buttonPanel);

            mainBorder.Child = panel;
            dialog.Content = mainBorder;

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                await dialog.ShowDialog(desktop.MainWindow);
            }

            return result;
        }
    }
}

