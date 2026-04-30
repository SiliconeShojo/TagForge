using CommunityToolkit.Mvvm.ComponentModel;

namespace TagForge.Services
{
    public partial class Persona : ObservableObject
    {
        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _systemPrompt;

        [ObservableProperty]
        private string _description;

        [ObservableProperty]
        private bool _isReadOnly;
        
        public Persona(string name, string systemPrompt, string description = "", bool isReadOnly = false)
        {
            Name = name;
            SystemPrompt = systemPrompt;
            Description = description;
            IsReadOnly = isReadOnly;
        }

        public bool IsNotReadOnly => !IsReadOnly;
    }
}
