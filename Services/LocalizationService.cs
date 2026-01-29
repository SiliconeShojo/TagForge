using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TagForge.Services
{
    public partial class LocalizationService : ObservableObject
    {
        private static LocalizationService _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private Dictionary<string, string> _currentLanguage = new();
        private Dictionary<string, string> _fallbackLanguage = new();

        [ObservableProperty]
        private string _currentLanguageCode = "en-US";

        public event Action LanguageChanged;

        private LocalizationService()
        {
            LoadLanguage("en-US", isFallback: true);
            LoadLanguage(CurrentLanguageCode);
        }

        public List<LanguageInfo> GetAvailableLanguages()
        {
            var languages = new List<LanguageInfo>
            {
                new("en-US", "English"),
                new("fr-FR", "Français"),
                new("es-ES", "Español")
            };

            // Dynamically detect available language files
            try
            {
                var existingCodes = languages.Select(l => l.Code).ToHashSet();
                var assetPrefix = "avares://TagForge/Assets/Localization/";
                
                // Check which assets exist
                foreach (var lang in languages.ToList())
                {
                    var uri = new Uri($"{assetPrefix}{lang.Code}.json");
                    if (!Avalonia.Platform.AssetLoader.Exists(uri))
                    {
                        languages.Remove(lang);
                    }
                }
            }
            catch { /* Fallback to predefined list */ }

            return languages;
        }

        public void ChangeLanguage(string languageCode)
        {
            if (CurrentLanguageCode == languageCode) return;

            if (LoadLanguage(languageCode))
            {
                CurrentLanguageCode = languageCode;
                
                // Notify all possible listeners
                OnPropertyChanged(string.Empty); // Refresh ALL properties
                OnPropertyChanged("Item[]");      // Indexer notification (WPF/Avalonia style)
                OnPropertyChanged("Item");        // Alternative indexer notification
                
                LanguageChanged?.Invoke();
            }
        }

        private bool LoadLanguage(string languageCode, bool isFallback = false)
        {
            try
            {
                var uri = new Uri($"avares://TagForge/Assets/Localization/{languageCode}.json");
                
                if (!Avalonia.Platform.AssetLoader.Exists(uri))
                {
                    return false;
                }

                using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var translations = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

                if (isFallback)
                {
                    _fallbackLanguage = translations ?? new();
                }
                else
                {
                    _currentLanguage = translations ?? new();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GetString(string key)
        {
            // Try current language first
            if (_currentLanguage.TryGetValue(key, out var value))
            {
                return value;
            }

            // Fallback to English
            if (_fallbackLanguage.TryGetValue(key, out var fallback))
            {
                return fallback;
            }

            // Return key if not found (helps identify missing translations)
            return $"[{key}]";
        }

        public string this[string key] => GetString(key);
    }

    public record LanguageInfo(string Code, string NativeName);
}
