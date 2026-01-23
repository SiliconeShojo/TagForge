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
        
        public Persona(string name, string systemPrompt, string description = "")
        {
            Name = name;
            SystemPrompt = systemPrompt;
            Description = description;
        }
    }
}
