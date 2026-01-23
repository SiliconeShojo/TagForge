using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TagForge.Models;

namespace TagForge.Services
{
    public class HistoryService
    {
        private readonly string _appDirectory;

        public HistoryService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _appDirectory = Path.Combine(appDataPath, "TagForge");
            
            if (!Directory.Exists(_appDirectory))
            {
                Directory.CreateDirectory(_appDirectory);
            }
        }

        public List<ChatMessage> LoadHistory(string fileName)
        {
            var path = Path.Combine(_appDirectory, fileName);
            if (!File.Exists(path))
            {
                return new List<ChatMessage>();
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new List<ChatMessage>();
            }
            catch (Exception)
            {
                return new List<ChatMessage>();
            }
        }

        public void SaveHistory(string fileName, IEnumerable<ChatMessage> messages)
        {
            try
            {
                var path = Path.Combine(_appDirectory, fileName);
                var json = JsonConvert.SerializeObject(messages, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving history: {ex.Message}");
            }
        }

        public void ClearHistory(string fileName)
        {
            var path = Path.Combine(_appDirectory, fileName);
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
