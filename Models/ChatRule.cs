using CommunityToolkit.Mvvm.ComponentModel;

namespace TagForge.Models
{
    public partial class ChatRule : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _instruction = string.Empty;

        [ObservableProperty]
        private bool _isReadOnly;

        public bool IsNotReadOnly => !IsReadOnly;

        public ChatRule(string name, string instruction, bool isReadOnly = false)
        {
            Name = name;
            Instruction = instruction;
            IsReadOnly = isReadOnly;
        }

        public ChatRule() { }
    }
}
