using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using TagForge.Services;
using System.Collections.Generic;

namespace TagForge.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService;
        private readonly SessionService _sessionService;
        private readonly LocalizationService _localizationService;

        public SettingsViewModel(SettingsService settingsService, SessionService sessionService)
        {
            _settingsService = settingsService;
            _sessionService = sessionService;
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

        private bool CanDeletePersona() => SelectedPersonaToEdit != null;

        [RelayCommand]
        private void SavePersonas()
        {
            // Map Personas back to SettingsModel
            var list = new List<PersonaModel>();
            foreach (var p in Personas)
            {
                list.Add(new PersonaModel { Name = p.Name, SystemPrompt = p.SystemPrompt });
            }
            _settingsService.CurrentSettings.SavedPersonas = list;
            _settingsService.SaveSettings();
            IsDirty = false;
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
            if (SelectedPersonaToEdit != null)
            {
                Personas.Remove(SelectedPersonaToEdit);
                SavePersonas(); // Deleting auto-saves for safety
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
            catch { }
        }


    }
}

