using System;

namespace TagForge.Models
{
    public enum LogLevel { Info, Warning, Error }

    public class LogEntry
    {
        public string Timestamp { get; }
        public string Message { get; }
        public LogLevel Level { get; }
        
        public string Color => Level switch 
        {
            LogLevel.Info => "#CCCCCC",
            LogLevel.Warning => "#FFC107",
            LogLevel.Error => "#F44336",
            _ => "#FFFFFF"
        };
        
        public LogEntry(string message, LogLevel level)
        {
            Timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            Message = message;
            Level = level;
        }
    }
}
