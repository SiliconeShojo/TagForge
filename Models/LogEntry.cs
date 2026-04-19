using System;

namespace TagForge.Models
{
    public enum LogLevel { Info, Warning, Error, ApiRequest, ApiResponse, Success }

    public class LogEntry : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public string Timestamp { get; }
        public string Message { get; }
        public LogLevel Level { get; }
        public string? Details { get; } // JSON or StackTrace

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
        
        public string Color => Level switch 
        {
            LogLevel.Info => "#CCCCCC",
            LogLevel.Warning => "#FFC107",
            LogLevel.Error => "#F44336",
            LogLevel.ApiRequest => "#64B5F6", // Blue
            LogLevel.ApiResponse => "#81C784", // Green
            LogLevel.Success => "#4CAF50",
            _ => "#FFFFFF"
        };
        
        public LogEntry(string message, LogLevel level, string? details = null)
        {
            Timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            Message = message;
            Level = level;
            Details = details;
        }
    }
}
