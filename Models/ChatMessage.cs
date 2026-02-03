using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TagForge.Models
{
    public partial class ChatMessage : ObservableObject
    {
        public string Role { get; set; } 
        
        [ObservableProperty]
        private string _content;

        [ObservableProperty]
        private bool _isThinking;

        [ObservableProperty]
        private bool _isLoadingModel;

        [ObservableProperty]
        private bool _isError;
        
        public DateTime Timestamp { get; set; }
        [ObservableProperty]
        private string? _details;

        [ObservableProperty]
        private string? _imagePath;

        public ChatMessage(string role, string content, string? details = null, string? imagePath = null)
        {
            Role = role;
            _content = content;
            Timestamp = DateTime.Now;
            _details = details;
            _imagePath = imagePath;
        }
    }
}
