using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TagForge.Models;
using TagForge.Services;

namespace TagForge.ViewModels
{
    public partial class HistoryViewModel : ViewModelBase
    {
        private readonly HistoryService _historyService;
        private readonly MainViewModel _mainViewModel;

        // Backing store for all sessions (unfiltered)
        private List<ChatSession> _allLoadedSessions = new();

        public ObservableCollection<ChatSession> AllSessions { get; } = new();

        [ObservableProperty]
        private ChatSession? _selectedSession;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        partial void OnSearchQueryChanged(string value) => ApplyFilters();

        [ObservableProperty]
        private int _filterIndex = 0; // 0=All, 1=Chat, 2=Tags

        partial void OnFilterIndexChanged(int value) => ApplyFilters();

        public ObservableCollection<string> FilterOptions { get; } = new();

        private void UpdateFilterOptions()
        {
             var currentIndex = FilterIndex;
             FilterOptions.Clear();
             FilterOptions.Add(LocalizationService.Instance["History.FilterAll"]);
             FilterOptions.Add(LocalizationService.Instance["History.FilterChat"]);
             FilterOptions.Add(LocalizationService.Instance["History.FilterTags"]);
             
             // Restore selection (force update if needed)
             FilterIndex = -1;
             FilterIndex = currentIndex < 0 ? 0 : currentIndex;
        }

        public HistoryViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            _historyService = new HistoryService();
            
            UpdateFilterOptions();
            LocalizationService.Instance.LanguageChanged += UpdateFilterOptions;
            
            _ = InitializeAsync();
        }

        public HistoryViewModel()
        {
            // Design-time constructor
            _historyService = new HistoryService();
        }

        private async Task InitializeAsync()
        {
            // improved initialization with migration support
            await PerformMigrationAsync();
            await LoadAllSessions();
        }

        private async Task PerformMigrationAsync()
        {
            bool migrationOccurred = false;

            // Check for legacy chat history
            if (await _historyService.NeedsMigrationAsync("history.json"))
            {
                await _historyService.MigrateOldHistoryAsync("history.json", "chat");
                migrationOccurred = true;
            }

            // Check for legacy generator history
            if (await _historyService.NeedsMigrationAsync("generation_history.json"))
            {
                await _historyService.MigrateOldHistoryAsync("generation_history.json", "generator");
                migrationOccurred = true;
            }

            if (migrationOccurred)
            {
                System.Diagnostics.Debug.WriteLine("Migration completed.");
            }
        }

        [ObservableProperty]
        private bool _isSessionsEmpty;

        [RelayCommand]
        public async Task LoadAllSessions()
        {
            try
            {
                var sessions = await _historyService.LoadAllSessionsAsync();
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _allLoadedSessions = sessions.OrderByDescending(s => s.LastModified).ToList();
                    ApplyFilters();
                });
            }
            catch (Exception ex)
            {
                // Log error - sessions failed to load
                System.Diagnostics.Debug.WriteLine($"Failed to load sessions: {ex.Message}");
            }
        }

        private void ApplyFilters()
        {
            var query = SearchQuery?.Trim().ToLower() ?? "";
            
            var filtered = _allLoadedSessions.Where(s => 
            {
                // Search filter
                bool matchesSearch = string.IsNullOrEmpty(query) || 
                                     (s.Title?.ToLower().Contains(query) == true) || 
                                     (s.PreviewText?.ToLower().Contains(query) == true);
                                     
                if (!matchesSearch) return false;

                // Type filter
                // 0 = All
                // 1 = Chat ("chat")
                // 2 = Tags ("tag" or "generator")
                
                if (FilterIndex == 1 && s.Type != "chat") return false;
                if (FilterIndex == 2 && (s.Type != "tag" && s.Type != "generator")) return false;

                return true;
            });

            AllSessions.Clear();
            foreach (var s in filtered)
            {
                AllSessions.Add(s);
            }

            IsSessionsEmpty = AllSessions.Count == 0;
        }

        [RelayCommand]
        private async Task SelectSession(ChatSession session)
        {
            if (session == null) return;

            SelectedSession = session;

            // Determine target tab based on session type
            if (session.Type == "chat")
            {
                // Tell MainViewModel to switch to Chat tab and load this session
                await _mainViewModel.LoadChatSessionAsync(session);
            }
            else if (session.Type == "tag" || session.Type == "generator")
            {
                await _mainViewModel.LoadTagSessionAsync(session);
            }
        }

        [RelayCommand]
        private async Task CreateNewChatSession()
        {
            var newSession = await _historyService.CreateNewSessionAsync("chat");
            AllSessions.Insert(0, newSession);
            await SelectSession(newSession);
        }

        [RelayCommand]
        private async Task CreateNewTagSession()
        {
            var newSession = await _historyService.CreateNewSessionAsync("generator");
            AllSessions.Insert(0, newSession);
            await SelectSession(newSession);
        }

        [RelayCommand]
        private async Task DeleteSession(ChatSession session)
        {
            if (session == null) return;

            // Show confirmation dialog
            var confirmed = await ShowConfirmationDialog(string.Format(LocalizationService.Instance["Dialog.DeleteSingleTitle"], session.Title), LocalizationService.Instance["Dialog.DeleteSingleSubtitle"]);
            if (!confirmed) return;

            // Determine category based on session type
            string category = (session.Type == "tag" || session.Type == "generator") ? "generator" : "chat";

            await _historyService.DeleteSessionAsync(session.Id, category);
            AllSessions.Remove(session);
            
            // Reload sessions to refresh  
            await LoadAllSessions();
        }

        [RelayCommand]
        private async Task ClearAllHistory()
        {
            if (AllSessions.Count == 0 && _allLoadedSessions.Count == 0) return;

            string titleKey = "Dialog.DeleteAllTitle";
            string subtitleKey = "Dialog.DeleteAllSubtitle";

            if (FilterIndex == 1)
            {
                titleKey = "Dialog.DeleteAllChatTitle";
                subtitleKey = "Dialog.DeleteAllChatSubtitle";
            }
            else if (FilterIndex == 2)
            {
                titleKey = "Dialog.DeleteAllTagTitle";
                subtitleKey = "Dialog.DeleteAllTagSubtitle";
            }

            var confirmed = await ShowConfirmationDialog(LocalizationService.Instance[titleKey], LocalizationService.Instance[subtitleKey]);
            if (!confirmed) return;

            if (FilterIndex == 0) // Delete All
            {
                await _historyService.DeleteAllSessionsAsync("chat");
                await _historyService.DeleteAllSessionsAsync("generator");
            }
            else if (FilterIndex == 1) // Chat only
            {
                await _historyService.DeleteAllSessionsAsync("chat");
            }
            else if (FilterIndex == 2) // Tags/Generator only
            {
                await _historyService.DeleteAllSessionsAsync("generator");
            }

            await LoadAllSessions();
        }

        [RelayCommand]
        private async Task RenameSession(ChatSession session)
        {
            if (session == null) return;

            // Show rename dialog
            var newTitle = await ShowRenameDialog(session.Title);
            if (string.IsNullOrEmpty(newTitle) || newTitle == session.Title) return;

            session.Title = newTitle;
            await _historyService.UpdateSessionTitleAsync(session.Id, newTitle);
        }

        private async Task<bool> ShowConfirmationDialog(string title, string subtitle)
        {
            var dialog = new Window
            {
                Title = LocalizationService.Instance["Dialog.ConfirmDeleteTitle"],
                Width = 450,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
                ShowInTaskbar = false
            };

            // Round corners for the window border
            dialog.CornerRadius = new CornerRadius(12);

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
                FontSize = 16,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#FFFFFF"))
            });

            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 14,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                Opacity = 0.7,
                Foreground = new SolidColorBrush(Color.Parse("#E0E0E0"))
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

            var deleteButton = new Button
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
            deleteButton.Click += (s, e) => { result = true; dialog.Close(); };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(deleteButton);
            panel.Children.Add(buttonPanel);

            mainBorder.Child = panel;
            dialog.Content = mainBorder;

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                 await dialog.ShowDialog(desktop.MainWindow);
            }

            return result;
        }

        private async Task<string?> ShowRenameDialog(string currentTitle)
        {
            var dialog = new Window
            {
                Title = LocalizationService.Instance["Dialog.RenameSessionTitle"],
                Width = 450,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                SystemDecorations = SystemDecorations.BorderOnly,
                Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
                ShowInTaskbar = false
            };

            // Round corners for the window border
            dialog.CornerRadius = new CornerRadius(12);

            string? result = null;

            var mainBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#333333")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Avalonia.Thickness(25)
            };

            var panel = new StackPanel
            {
                Spacing = 15,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            panel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Instance["Dialog.RenameSessionTitle"],
                FontSize = 16,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#FFFFFF"))
            });

            var textBox = new TextBox
            {
                Text = currentTitle,
                FontSize = 14,
                Watermark = LocalizationService.Instance["Dialog.EnterTitlePlaceholder"],
                CornerRadius = new CornerRadius(6),
                Padding = new Avalonia.Thickness(10),
                Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
                BorderBrush = new SolidColorBrush(Color.Parse("#404040")),
                BorderThickness = new Avalonia.Thickness(1)
            };

            panel.Children.Add(textBox);

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

            var renameButton = new Button
            {
                Content = LocalizationService.Instance["History.Rename"],
                Width = 110,
                Height = 40,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse("#6B8AFF")),
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            renameButton.Click += (s, e) => { result = textBox.Text; dialog.Close(); };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(renameButton);
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
