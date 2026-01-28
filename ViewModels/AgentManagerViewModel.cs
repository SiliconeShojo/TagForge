using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Collections.Generic;
using TagForge.Services;
using System.Linq;
using Avalonia.Threading;
using Avalonia.Media;
using System;

namespace TagForge.ViewModels
{
    public partial class AgentManagerViewModel : ViewModelBase
    {
        private readonly SessionService _sessionService;
        private readonly SettingsService _settingsService;
        private readonly MainViewModel _mainViewModel;
        private readonly ProviderFactory _providerFactory;

        [ObservableProperty]
        private ObservableCollection<string> _availableProviders = new()
        {
            "Google Gemini",
            "Groq",
            "OpenRouter",
            "Hugging Face",
            // "Cerebras", // Hidden from UI
            "LM Studio",
            "Ollama"
        };
        
        [ObservableProperty]
        private string _helpText = "Select a provider to see configuration details.";

        [ObservableProperty]
        private bool _isPasswordVisible;

        [ObservableProperty]
        private char _maskChar = '*';

        public ObservableCollection<AgentProfile> Profiles => _sessionService.Profiles;

        public AgentProfile SelectedProfile 
        { 
            get => _sessionService.ActiveProfile;
            set 
            {
               _sessionService.ActiveProfile = value;
               OnPropertyChanged(nameof(SelectedProfile));
               UpdateHelpText(value?.Provider);
               
               // Persist selection
               if (value != null)
               {
                   _settingsService.CurrentSettings.LastSelectedProviderName = value.Name;
                   _settingsService.SaveSettings();
               }
            }
        }

        partial void OnIsPasswordVisibleChanged(bool value)
        {
            MaskChar = value ? '\0' : '*';
        }

        public AgentManagerViewModel(SessionService sessionService, SettingsService settingsService, MainViewModel mainVm)
        {
            _sessionService = sessionService;
            _settingsService = settingsService;
            _mainViewModel = mainVm;
            _providerFactory = new ProviderFactory();
            
            LoadProfiles();
        }
        
        // Design-time / Fallback
        public AgentManagerViewModel() 
        { 
             _settingsService = new SettingsService();
             _sessionService = new SessionService(_settingsService);
             _providerFactory = new ProviderFactory();
             LoadProfiles();
        }

        private void LoadProfiles()
        {
            if (_sessionService.Profiles.Count > 0) return;

            var savedAgents = _settingsService.CurrentSettings.SavedAgents;

            // Load saved agents or create defaults
            foreach (var provider in AvailableProviders)
            {
                var saved = savedAgents.FirstOrDefault(a => a.Provider == provider);
                var profile = new AgentProfile 
                { 
                    Name = provider, 
                    Provider = provider,
                    EndpointUrl = saved?.EndpointUrl ?? GetDefaultUrl(provider),
                    ApiKey = DecryptKey(saved?.EncryptedApiKey),
                    SelectedModel = saved?.SelectedModel,

                    HelpUrl = GetHelpUrl(provider),
                    Description = GetProviderDescription(provider),

                    IconData = GetIconGeometry(provider)
                };

                // Check for embedded icon resource
                try 
                {
                    // Remove spaces for resource names (e.g. "Google Gemini" -> "GoogleGemini.png")
                    string resourceName = provider.Replace(" ", "") + ".png";
                    var uri = new Uri($"avares://TagForge/Assets/Icons/{resourceName}");
                    
                    if (Avalonia.Platform.AssetLoader.Exists(uri))
                    {
                        using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                        profile.IconBitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                    }
                }
                catch { /* Ignore missing assets */ }

                // Migration: Auto-update legacy Hugging Face URL
                if (provider == "Hugging Face" && profile.EndpointUrl == "https://api-inference.huggingface.co/models/")
                {
                    profile.EndpointUrl = "https://router.huggingface.co/v1";
                }
                
                // Add property change listener to auto-save and pre-load
                profile.PropertyChanged += async (s, e) => 
                {
                    SaveProfiles();
                    if (e.PropertyName == nameof(AgentProfile.SelectedModel) && !string.IsNullOrEmpty(profile.SelectedModel))
                    {
                        await PreloadModel(profile);
                    }
                };
                
                _sessionService.Profiles.Add(profile);
            }
            
            // Restore selection
            var lastSelected = _settingsService.CurrentSettings.LastSelectedProviderName;
            SelectedProfile = _sessionService.Profiles.FirstOrDefault(p => p.Name == lastSelected) 
                              ?? _sessionService.Profiles.FirstOrDefault();

            // Auto-Connect (Phase 11)
            if (SelectedProfile != null)
            {
                bool isLocal = SelectedProfile.Provider == "Ollama" || SelectedProfile.Provider == "Custom";
                if (isLocal || !string.IsNullOrEmpty(SelectedProfile.ApiKey))
                {
                    _ = TestConnection();
                }
            }
        }

        private void SaveProfiles()
        {
            var configList = new List<AgentConfig>();
            foreach (var p in _sessionService.Profiles)
            {
                configList.Add(new AgentConfig
                {
                    Name = p.Name,
                    Provider = p.Provider,
                    EndpointUrl = p.EndpointUrl,
                    SelectedModel = p.SelectedModel,
                    EncryptedApiKey = EncryptKey(p.ApiKey)
                });
            }
            _settingsService.CurrentSettings.SavedAgents = configList;
            _settingsService.SaveSettings();
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private string EncryptKey(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return null;
            try {
                var data = System.Text.Encoding.UTF8.GetBytes(plainText);
                var encrypted = System.Security.Cryptography.ProtectedData.Protect(data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            } catch { return null; }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private string DecryptKey(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return null;
            try {
                var data = Convert.FromBase64String(encryptedText);
                var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(decrypted);
            } catch { return null; }
        }

        private string GetDefaultUrl(string provider)
        {
            return provider switch
            {
                "Google Gemini" => "https://generativelanguage.googleapis.com/v1beta/models",
                "Groq" => "https://api.groq.com/openai/v1/chat/completions",
                "OpenRouter" => "https://openrouter.ai/api/v1/chat/completions",
                "LM Studio" => "http://localhost:1234/v1/chat/completions",
                "Ollama" => "http://localhost:11434/api/chat",
                "Cerebras" => "https://api.cerebras.ai/v1/chat/completions",
                "Hugging Face" => "https://router.huggingface.co/v1",
                "Custom" => "http://localhost:5000/v1/chat/completions", // Example default
                _ => ""
            };
        }

        private void UpdateHelpText(string provider)
        {
            if (provider == null) return;
             HelpText = provider switch
            {
                "Google Gemini" => "Generative AI from Google. Requires API Key.",
                "Groq" => "Ultra-fast inference speed. Get key from console.groq.com.",
                "Hugging Face" => "Access 100k+ models. Token required.",
                "LM Studio" => "Connect to local LM Studio server.",
                "Ollama" => "Run local models. No API Key required.",
                "OpenRouter" => "Unified interface for top LLMs.",
                "Cerebras" => "Fast AI inference.",
                "Custom" => "Connect to any OpenAI-compatible endpoint.",
                _ => "Configure your agent connection."
            };
        }

        [RelayCommand]
        private void TogglePassword()
        {
            IsPasswordVisible = !IsPasswordVisible;
        }

        [RelayCommand]
        private async Task TestConnection()
        {
            if (SelectedProfile == null) return;

            var providerImpl = _providerFactory.CreateProvider(SelectedProfile.Provider);
            
            if (providerImpl == null)
            {
                _mainViewModel?.ShowNotification($"Unknown Provider: {SelectedProfile.Provider}", true);
                return;
            }

            HelpText = "Testing connection (Ping)...";
            _sessionService.IsBusy = true;
            _sessionService.StatusMessage = "Pinging...";
            
            try 
            {
                bool success = await providerImpl.PingAsync(SelectedProfile.ApiKey, SelectedProfile.EndpointUrl);
                
                if (success)
                {
                    HelpText = "Connection Successful!";
                    _mainViewModel?.ShowNotification("Connection Verified!", false);
                    _sessionService.Log($"{SelectedProfile.Provider} ping successful", LogLevel.Info);
                }
                else
                {
                    HelpText = "Connection Failed.";
                    _mainViewModel?.ShowNotification("Connection Ping Failed.", true);
                }
            } 
            catch (Exception ex)
            {
                HelpText = "Connection Failed.";
                var errorMsg = $"{SelectedProfile.Provider} connection error: {ex.Message}";
                _mainViewModel?.ShowNotification(errorMsg, true);
            }
            finally
            {
                _sessionService.IsBusy = false;
                _sessionService.StatusMessage = "Ready";
            }
        }

        [RelayCommand]
        private async Task FetchModels()
        {
             if (SelectedProfile == null) return;
             var providerImpl = _providerFactory.CreateProvider(SelectedProfile.Provider);
             if (providerImpl == null) return;

             HelpText = "Fetching models...";
             _sessionService.IsBusy = true;
             
             try 
             {
                 await FetchModelsInternal(providerImpl);
                 HelpText = $"Success! Loaded {SelectedProfile.AvailableModels.Count} models.";
                 _mainViewModel?.ShowNotification("Models Refreshed", false);
             }
             catch(Exception ex)
             {
                 HelpText = "Fetch failed.";
                 _mainViewModel?.ShowNotification($"Fetch Failed: {ex.Message}", true);
             }
             finally
             {
                 _sessionService.IsBusy = false;
             }
        }

        [RelayCommand]
        private void SaveProfile()
        {
            SaveProfiles();
            _mainViewModel?.ShowNotification("Configuration Saved", false);
        }

        private async Task FetchModelsInternal(IAIProvider provider)
        {
            // Persist current selection logic
            var previousModel = SelectedProfile.SelectedModel;

            var models = await provider.FetchModelsAsync(SelectedProfile.ApiKey, SelectedProfile.EndpointUrl);
            
            // Update on UI thread
            Dispatcher.UIThread.Post(() => {
                SelectedProfile.AvailableModels.Clear();
                foreach (var m in models)
                {
                    SelectedProfile.AvailableModels.Add(m);
                }
                if (models.Count > 0)
                {
                    if (!string.IsNullOrEmpty(previousModel) && models.Contains(previousModel))
                    {
                        SelectedProfile.SelectedModel = previousModel;
                    }
                    else
                    {
                         SelectedProfile.SelectedModel = models[0];
                    }
                }
            });
            SaveProfiles(); 
        }

        private async Task PreloadModel(AgentProfile profile)
        {
             if (profile == null || string.IsNullOrEmpty(profile.SelectedModel)) return;
             
             if (profile.Provider == "Ollama" || profile.Provider == "LM Studio")
             {
                 HelpText = $"Pre-loading {profile.SelectedModel}...";
                 
                 // Update global status bar
                 if (_mainViewModel != null)
                 {
                     _mainViewModel.StatusText = $"Loading {profile.SelectedModel}...";
                     _mainViewModel.StatusColor = "#FFC107"; // Amber/Loading
                 }

                 try 
                 {
                     var provider = _providerFactory.CreateProvider(profile.Provider);
                     if (provider != null)
                     {
                         await provider.LoadModelAsync(profile.SelectedModel, profile.ApiKey, profile.EndpointUrl);
                         HelpText = $"Ready. {profile.SelectedModel} loaded.";
                         
                         if (_mainViewModel != null)
                         {
                             _mainViewModel.StatusText = "Ready";
                             _mainViewModel.StatusColor = "#4CAF50"; // Green
                         }
                     }
                 }
                 catch (Exception ex)
                 {
                     HelpText = $"Failed to load model: {ex.Message}";
                     if (_mainViewModel != null)
                     {
                         _mainViewModel.StatusText = "Load Failed";
                         _mainViewModel.StatusColor = "#F44336";
                     }
                 }
             }
        }

        [RelayCommand]
        private void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { /* Ignore or show notification */ }
        }

        private string GetHelpUrl(string provider)
        {
            return provider switch
            {
                "OpenAI" => "https://platform.openai.com/api-keys",
                "Anthropic" => "https://console.anthropic.com/settings/keys",
                "DeepSeek" => "https://platform.deepseek.com/api_keys",
                "Mistral" => "https://console.mistral.ai/api-keys/",
                "Google Gemini" => "https://aistudio.google.com/app/apikey",
                "Hugging Face" => "https://huggingface.co/settings/tokens",
                "Groq" => "https://console.groq.com/keys",
                "OpenRouter" => "https://openrouter.ai/keys",
                "Cerebras" => "https://cloud.cerebras.ai/platform",
                "LM Studio" => "https://lmstudio.ai/docs/local-server",
                "Ollama" => "https://ollama.com",
                "Custom" => "https://platform.openai.com/docs/api-reference",
                _ => "https://google.com/search?q=" + provider + "+api+key"
            };
        }

        private string GetProviderDescription(string provider)
        {
            return provider switch
            {
                "Google Gemini" => "Generative AI from Google. High performance and large context window. Requires API Key.",
                "Groq" => "Ultra-fast inference speed suitable for real-time applications.",
                "Hugging Face" => "Access thousands of open-source models via the Hugging Face Inference API.",
                "OpenRouter" => "A unified interface to access top LLMs from OpenAI, Anthropic, and more.",
                "LM Studio" => "Connect to your local LM Studio server. Ensure the server is running on localhost:1234.",
                "Ollama" => "Run powerful local models like Llama 3 on your machine. Ensure Ollama is running.",
                "Cerebras" => "Fast AI inference specialized for low latency.",
                "Custom" => "Connect to any OpenAI-compatible API endpoint.",
                _ => "Configure your agent details below."
            };
        }

        private Geometry GetIconGeometry(string provider)
        {
             return provider switch
             {
                 "Google Gemini" => StreamGeometry.Parse("M12,2L14.5,9.5L22,12L14.5,14.5L12,22L9.5,14.5L2,12L9.5,9.5Z"), // Star/Sparkle
                 "Groq" => StreamGeometry.Parse("M3,3V21H21V3H3M5,5H19V19H5V5Z"), // Square Outline (Minimalist)
                 "OpenRouter" => StreamGeometry.Parse("M12,2A10,10 0 1,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 1,0 20,12A8,8 0 0,0 12,4M12,6L16,10H13V14H11V10H8L12,6Z"), // Compass/Arrow
                 "LM Studio" => StreamGeometry.Parse("M2,2H22V22H2V2M4,4V20H20V4H4M8,8H16V16H8V8Z"), // Generic Chip/Box fallback
                 "Ollama" => StreamGeometry.Parse("M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M7,9.5C7,8.7 7.7,8 8.5,8C9.3,8 10,8.7 10,9.5C10,10.3 9.3,11 8.5,11C7.7,11 7,10.3 7,9.5M12,17.23C10.25,17.23 8.71,16.5 7.81,15.42L9.23,14C9.68,14.72 10.75,15.23 12,15.23C13.25,15.23 14.32,14.72 14.77,14L16.19,15.42C15.29,16.5 13.75,17.23 12,17.23M15.5,11C14.7,11 14,10.3 14,9.5C14,8.7 14.7,8 15.5,8C16.3,8 17,8.7 17,9.5C17,10.3 16.3,11 15.5,11Z"), // Emoji Face
                 "Cerebras" => StreamGeometry.Parse("M2,2H22V22H2V2M4,4V20H20V4H4M8,8H16V16H8V8Z"), // Chip
                 "Hugging Face" => StreamGeometry.Parse("M12,2C6.48,2 2,6.48 2,12C2,17.52 6.48,22 12,22C17.52,22 22,17.52 22,12C22,6.48 17.52,2 12,2M16,13H8V11H16V13Z"), // Simple Neutral Face
                 _ => StreamGeometry.Parse("M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4")
             };
        }
    }

    public partial class AgentProfile : ObservableObject
    {
        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsApiKeyNeeded))]
        private string _provider;

        [ObservableProperty]
        private string _apiKey;

        [ObservableProperty]
        private string _endpointUrl;

        [ObservableProperty]
        private string _selectedModel;

        [ObservableProperty]
        private string _helpUrl;

        [ObservableProperty]
        private string _description;

        public bool IsApiKeyNeeded => Provider != "Ollama" && Provider != "LM Studio" && Provider != "Custom";
        
        [ObservableProperty]
        private Geometry _iconData;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasCustomIcon))]
        private Avalonia.Media.IImage? _iconBitmap;

        public bool HasCustomIcon => IconBitmap != null;

        public ObservableCollection<string> AvailableModels { get; } = new();
    }
}
