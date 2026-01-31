using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TagForge.Models
{
    public partial class ChatSession : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        
        [ObservableProperty]
        private string _title = string.Empty;
        
        public DateTime CreatedAt { get; set; }
        
        [ObservableProperty]
        private DateTime _lastModified;
        
        [ObservableProperty]
        private int _messageCount;
        
        public string PreviewText { get; set; } = string.Empty;
        
        [ObservableProperty]
        private bool _isActive;

        public ChatSession()
        {
            CreatedAt = DateTime.Now;
            LastModified = DateTime.Now;
        }

        public ChatSession(string id, string title)
        {
            Id = id;
            Title = title;
            CreatedAt = DateTime.Now;
            LastModified = DateTime.Now;
        }

        public string Type => Id.StartsWith("chat_") ? "chat" : "tag";
    }
}
