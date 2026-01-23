using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using TagForge.Services;
using Avalonia.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TagForge.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentPage;
    
    [ObservableProperty]
    private bool _isGeneratorVisible;
    [ObservableProperty]
    private bool _isChatVisible;
    [ObservableProperty]
    private bool _isNetworkVisible;
    [ObservableProperty]
    private bool _isSystemVisible;
    [ObservableProperty]
    private bool _isLogVisible;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    private ListItemTemplate? _selectedListItem;
    public ListItemTemplate? SelectedListItem
    {
        get => _selectedListItem;
        set
        {
            // Intercept navigation if Settings is Dirty
            if (CurrentPage is SettingsViewModel settingsVM && settingsVM.IsDirty && value != _selectedListItem)
            {
                ShowNotification("You have unsaved persona changes! Please save or revert them.", true);
                // Force UI re-binding to notify that the property didn't change (to reset tab selection visually if needed)
                OnPropertyChanged(nameof(SelectedListItem)); 
                return;
            }

            if (SetProperty(ref _selectedListItem, value))
            {
                UpdateCurrentView(value);
            }
        }
    }

    [ObservableProperty]
    private ObservableCollection<Notification> _notifications = new();

    public ObservableCollection<ListItemTemplate> Items { get; } = new();

    private readonly SessionService _sessionService;
    private readonly SettingsService _settingsService;
    
    // Status Bar Binding
    public string ActiveModelDisplay => _sessionService.ActiveProfile?.SelectedModel ?? "None";
    public string ActiveProviderDisplay => _sessionService.ActiveProfile?.Provider ?? "Unknown";
    [ObservableProperty]
    private string _latencyDisplay = "0ms";
    [ObservableProperty]
    private string _latencyColor = "#4CAF50"; // Default Green

    
    private DispatcherTimer _pingTimer;

    private readonly UpdateService _updateService = new();

    [ObservableProperty] private bool _isUpdateModalVisible;
    [ObservableProperty] private string _updateVersion;
    [ObservableProperty] private string _updateChangelog;
    private string _updateDownloadUrl;

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _sessionService = new SessionService(_settingsService);

        _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pingTimer.Tick += async (s, e) => await PingEndpoint();
        _pingTimer.Start();
        

        InitializeItems();
        if (Items.Count > 0) SelectedListItem = Items[0];
        
        // Listen to profile changes to update status bar
        _sessionService.PropertyChanged += (s, e) => {
             if (e.PropertyName == nameof(SessionService.ActiveProfile))
             {
                 OnPropertyChanged(nameof(ActiveModelDisplay));
                 OnPropertyChanged(nameof(ActiveProviderDisplay));
                 UpdateConnectionStatus();
                 
                 if (_sessionService.ActiveProfile != null)
                 {
                     _sessionService.ActiveProfile.PropertyChanged += (s2, e2) => {
                         if (e2.PropertyName == nameof(AgentProfile.SelectedModel))
                         {
                             OnPropertyChanged(nameof(ActiveModelDisplay));
                             UpdateConnectionStatus(); // Check on model change
                         }
                     };
                 }
             }
             if (e.PropertyName == nameof(SessionService.LastLatency))
             {
                 UpdateConnectionStatus();
             }
        };
        
        // Auto-Check Updates
        if (_settingsService.CurrentSettings.AutoCheckForUpdates)
        {
             _ = CheckUpdatesAsync(false);
        }

        // Initial Check
        UpdateConnectionStatus();
    }

    [ObservableProperty] private string _statusText = "Initializing...";
    [ObservableProperty] private string _statusColor = "#777777";

    private void UpdateConnectionStatus()
    {
         // 1. Check Latency / Offline
         if (_sessionService.LastLatency <= 0)
         {
              StatusText = "Offline";
              StatusColor = "#777777"; // Grey
              LatencyDisplay = "Offline";
              LatencyColor = "#777777";
              return;
         }

         // Latency Updates
         LatencyDisplay = $"{_sessionService.LastLatency}ms";
         if (_sessionService.LastLatency < 300) LatencyColor = "#4CAF50"; 
         else if (_sessionService.LastLatency < 1000) LatencyColor = "#FF9800"; 
         else LatencyColor = "#F44336"; 

         // 2. Check Model Selection
         if (string.IsNullOrEmpty(ActiveModelDisplay) || ActiveModelDisplay == "None")
         {
              StatusText = "Select Model";
              StatusColor = "#FF9800"; // Orange
         }
         else
         {
              StatusText = "Ready";
              StatusColor = "#4CAF50"; // Green
         }
    }



    public async Task CheckUpdatesAsync(bool manual)
    {
         var info = await _updateService.CheckForUpdatesAsync();
         if (info != null)
         {
              UpdateVersion = info.Version;
              UpdateChangelog = info.Changelog ?? "No changelog provided.";
              _updateDownloadUrl = info.DownloadUrl;
              IsUpdateModalVisible = true;
         } 
         else if (manual)
         {
              ShowNotification("No updates available.");
         }
    }

    [RelayCommand]
    private async Task PerformUpdate()
    {
         if (_updateDownloadUrl == null) return;
         ShowNotification("Downloading update...", false);
         IsUpdateModalVisible = false;
         
         try 
         {
             await _updateService.PerformUpdateAsync(_updateDownloadUrl);
         } 
         catch(Exception ex) 
         {
             ShowNotification($"Update failed: {ex.Message}", true);
         }
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        IsUpdateModalVisible = false;
    }

    public GenerationViewModel GenInstance { get; private set; }
    public ChatViewModel ChatInstance { get; private set; }
    public AgentManagerViewModel NetworkInstance { get; private set; }

    public SettingsViewModel SystemInstance { get; private set; }
    public LogViewModel LogInstance { get; private set; }

    private void InitializeItems()
    {
        GenInstance = new GenerationViewModel(_sessionService, this);
        ChatInstance = new ChatViewModel(_sessionService, this);
        NetworkInstance = new AgentManagerViewModel(_sessionService, _settingsService, this);
        SystemInstance = new SettingsViewModel(_settingsService, _sessionService);
        LogInstance = new LogViewModel(_sessionService);

        Items.Add(new ListItemTemplate(typeof(GenerationViewModel), "Tag Generator", "M5.5,7A1.5,1.5 0 0,1 4,5.5A1.5,1.5 0 0,1 5.5,4A1.5,1.5 0 0,1 7,5.5A1.5,1.5 0 0,1 5.5,7M21.41,11.58L12.41,2.58C12.05,2.22 11.55,2 11,2H4C2.9,2 2,2.9 2,4V11C2,11.55 2.22,12.05 2.59,12.41L11.58,21.41C11.95,21.77 12.45,22 13,22C13.55,22 14.05,21.77 14.41,21.41L21.41,14.41C21.78,14.05 22,13.55 22,13C22,12.45 21.77,11.94 21.41,11.58Z", GenInstance));
        Items.Add(new ListItemTemplate(typeof(ChatViewModel), "Chat", "M20,2H4A2,2 0 0,0 2,4V22L6,18H20A2,2 0 0,0 22,16V4A2,2 0 0,0 20,2M20,16H6L4,18V4H20Z", ChatInstance));
        Items.Add(new ListItemTemplate(typeof(AgentManagerViewModel), "Agent Configuration", "M12,18A6,6 0 0,1 6,12C6,8.69 8.69,6 12,6C15.31,6 18,8.69 18,12A6,6 0 0,1 12,18M12,4C7.58,4 4,7.58 4,12C4,16.42 7.58,20 12,20C16.42,20 20,16.42 20,12C20,7.58 16.42,4 12,4Z", NetworkInstance));
        Items.Add(new ListItemTemplate(typeof(SettingsViewModel), "System", "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.04 4.95,18.95L7.44,17.95C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56,17.95L19.05,18.95C19.27,19.04 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z", SystemInstance));
        Items.Add(new ListItemTemplate(typeof(LogViewModel), "Logs", "M14,12H15.5V14.82L17.94,16.23L17.19,17.53L14,15.69V12M4,2H18A2,2 0 0,1 20,4V16A2,2 0 0,1 18,18H6L2,22V4A2,2 0 0,1 4,2M6,9H18V7H6V9M6,13H12V11H6V13M6,17H10V15H6V17Z", LogInstance));
    }

    private void UpdateCurrentView(ListItemTemplate? value)
    {
        if (value is null) return;
        
        // Use the pre-instantiated instance if available
        if (value.Instance is ViewModelBase vm)
        {
            CurrentPage = vm;
            IsGeneratorVisible = vm is GenerationViewModel;
            IsChatVisible = vm is ChatViewModel;
            IsNetworkVisible = vm is AgentManagerViewModel;
            IsSystemVisible = vm is SettingsViewModel;
            IsLogVisible = vm is LogViewModel;
        }
    }

    public void ShowNotification(string message, bool isError = false)
    {
        // Also log deeply
        _sessionService.Log(message, isError ? LogLevel.Error : LogLevel.Info);

        var notification = new Notification(message, isError);
        // Dispatch to UI thread just in case
        Dispatcher.UIThread.Invoke(() => 
        {
            Notifications.Add(notification);
        });

        // Auto-dismiss after 3 seconds
        DispatcherTimer.RunOnce(() =>
        {
            Notifications.Remove(notification);
        }, TimeSpan.FromSeconds(3));
    }

    [RelayCommand]
    private void TriggerPane()
    {
        IsPaneOpen = !IsPaneOpen;
    }

    private async Task PingEndpoint()
    {
         var profile = _sessionService.ActiveProfile;
         if (profile == null) return;
         
         try 
         {
             var providerFactory = new ProviderFactory();
             var provider = providerFactory.CreateProvider(profile.Provider);
             if (provider != null)
             {
                 var sw = Stopwatch.StartNew();
                 await provider.FetchModelsAsync(profile.ApiKey, profile.EndpointUrl);
                 sw.Stop();
                 _sessionService.LastLatency = sw.ElapsedMilliseconds;
             }
         }
         catch (Exception ex)
         {
             _sessionService.LastLatency = 9999; // Indicate error
             LatencyColor = "#F44336"; // Red
             
             // Log detailed error information
             var errorDetails = $"Provider: {profile.Provider}\n" +
                              $"Endpoint: {profile.EndpointUrl}\n" +
                              $"Error: {ex.Message}";
             
             if (ex.InnerException != null)
             {
                 errorDetails += $"\nInner Error: {ex.InnerException.Message}";
             }
             
             // Use Warning for background pings to differentiate from explicit user tests
             _sessionService.Log($"Background Ping Failed: {errorDetails}", LogLevel.Warning);
         }
    }
}

public class Notification
{
    public string Message { get; }
    public bool IsError { get; }
    public string BackgroundColor => IsError ? "#8B0000" : "#2E7D32"; // Dark Red vs Green

    public Notification(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}

public class ListItemTemplate
{
    public string Label { get; }
    public Type ModelType { get; }
    public string IconData { get; }
    public Avalonia.Media.IImage? IconBitmap { get; }
    public bool HasCustomIcon => IconBitmap != null;
    public object? Instance { get; } // Restored

    public ListItemTemplate(Type type, string label, string iconData, object? instance = null)
    {
        ModelType = type;
        Label = label;
        IconData = iconData;
        Instance = instance;

        try 
        {
            string name = label.Replace(" ", "") + ".png";
            var uri = new Uri($"avares://TagForge/Assets/Icons/{name}");
            
            if (Avalonia.Platform.AssetLoader.Exists(uri))
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                IconBitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            }
        }
        catch { }
    }
}
