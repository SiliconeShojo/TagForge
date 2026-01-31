using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TagForge.Models;
using System.Threading.Tasks;

namespace TagForge.Services
{
    public class HistoryService
    {
        private readonly string _appDirectory;
        private readonly string _sessionsDirectory;

        public HistoryService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _appDirectory = Path.Combine(appDataPath, "TagForge");
            _sessionsDirectory = Path.Combine(_appDirectory, "sessions");
            
            if (!Directory.Exists(_appDirectory))
            {
                Directory.CreateDirectory(_appDirectory);
            }
            
            if (!Directory.Exists(_sessionsDirectory))
            {
                Directory.CreateDirectory(_sessionsDirectory);
            }
        }

        #region Legacy Methods (for backward compatibility)
        
        public async Task<List<ChatMessage>> LoadHistoryAsync(string fileName)
        {
            var path = Path.Combine(_appDirectory, fileName);
            if (!File.Exists(path))
            {
                return new List<ChatMessage>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new List<ChatMessage>();
            }
            catch (Exception)
            {
                return new List<ChatMessage>();
            }
        }

        public async Task SaveHistoryAsync(string fileName, IEnumerable<ChatMessage> messages)
        {
            try
            {
                var path = Path.Combine(_appDirectory, fileName);
                var json = JsonConvert.SerializeObject(messages, Formatting.Indented);
                await File.WriteAllTextAsync(path, json);
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
        
        #endregion

        #region Session Management

        /// <summary>
        /// Loads the session index for a given category (chat or generator)
        /// </summary>
        public async Task<List<ChatSession>> LoadSessionsIndexAsync(string category = "chat")
        {
            var indexPath = Path.Combine(_appDirectory, $"sessions_index_{category}.json");
            
            if (!File.Exists(indexPath))
            {
                // Try to build index from existing session files
                return await RebuildSessionIndexAsync(category);
            }

            try
            {
                var json = await File.ReadAllTextAsync(indexPath);
                return JsonConvert.DeserializeObject<List<ChatSession>>(json) ?? new List<ChatSession>();
            }
            catch (Exception)
            {
                // If index is corrupted, rebuild it
                return await RebuildSessionIndexAsync(category);
            }

        }

        public async Task<List<ChatSession>> LoadAllSessionsAsync()
        {
            var chatSessions = await LoadSessionsIndexAsync("chat");
            var tagSessions = await LoadSessionsIndexAsync("generator");
            
            var allSessions = new List<ChatSession>();
            allSessions.AddRange(chatSessions);
            allSessions.AddRange(tagSessions);
            
            return allSessions.OrderByDescending(s => s.LastModified).ToList();
        }

        /// <summary>
        /// Loads all messages from a specific session
        /// </summary>
        public async Task<List<ChatMessage>> LoadSessionMessagesAsync(string sessionId)
        {
            var sessionPath = GetSessionPath(sessionId);
            
            if (!File.Exists(sessionPath))
            {
                return new List<ChatMessage>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(sessionPath);
                return JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new List<ChatMessage>();
            }
            catch (Exception)
            {
                return new List<ChatMessage>();
            }
        }

        /// <summary>
        /// Saves messages to a session and updates the index
        /// </summary>
        public async Task SaveSessionAsync(string sessionId, List<ChatMessage> messages, string category = "chat")
        {
            try
            {
                // Save session messages
                var sessionPath = GetSessionPath(sessionId);
                var json = JsonConvert.SerializeObject(messages, Formatting.Indented);
                await File.WriteAllTextAsync(sessionPath, json);

                // Update session index
                await UpdateSessionInIndexAsync(sessionId, messages, category);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving session: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a new session with a generated ID, or reuses an existing empty session
        /// </summary>
        public async Task<ChatSession> CreateNewSessionAsync(string category = "chat")
        {
            // Check if there's an existing empty session we can reuse
            var sessions = await LoadSessionsIndexAsync(category);
            var emptySession = sessions
                .Where(s => s.MessageCount == 0)
                .OrderByDescending(s => s.LastModified)
                .FirstOrDefault();
                
            if (emptySession != null)
            {
                // Reuse the existing empty session
                emptySession.LastModified = DateTime.Now;
                emptySession.IsActive = true;
                return emptySession;
            }
            
            // No empty session exists, create a new one
            var sessionId = GenerateSessionId(category);
            var session = new ChatSession(sessionId, "New Session")
            {
                CreatedAt = DateTime.Now,
                LastModified = DateTime.Now,
                MessageCount = 0,
                PreviewText = string.Empty,
                IsActive = true
            };

            // Save empty session
            await SaveSessionAsync(sessionId, new List<ChatMessage>(), category);

            return session;
        }

        /// <summary>
        /// Updates the title of a session
        /// </summary>
        public async Task UpdateSessionTitleAsync(string sessionId, string newTitle, string category = "chat")
        {
            var sessions = await LoadSessionsIndexAsync(category);
            var session = sessions.FirstOrDefault(s => s.Id == sessionId);
            
            if (session != null)
            {
                session.Title = newTitle;
                session.LastModified = DateTime.Now;
                await SaveSessionIndexAsync(sessions, category);
            }
        }

        /// <summary>
        /// Deletes a session file and removes it from the index
        /// </summary>
        public async Task DeleteSessionAsync(string sessionId, string category = "chat")
        {
            try
            {
                // Delete session file
                var sessionPath = GetSessionPath(sessionId);
                if (File.Exists(sessionPath))
                {
                    File.Delete(sessionPath);
                }

                // Remove from index
                var sessions = await LoadSessionsIndexAsync(category);
                sessions.RemoveAll(s => s.Id == sessionId);
                await SaveSessionIndexAsync(sessions, category);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting session: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Deletes all sessions in a specific category
        /// </summary>
        public async Task DeleteAllSessionsAsync(string category)
        {
            try
            {
                var sessions = await LoadSessionsIndexAsync(category);
                
                // Delete all individual session files
                foreach (var session in sessions)
                {
                    var sessionPath = GetSessionPath(session.Id);
                    if (File.Exists(sessionPath))
                    {
                        try { File.Delete(sessionPath); } catch { }
                    }
                }

                // Clear the index
                sessions.Clear();
                await SaveSessionIndexAsync(sessions, category);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting all sessions for {category}: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes all empty sessions (MessageCount == 0) in a specific category
        /// </summary>
        public async Task CleanupEmptySessionsAsync(string category)
        {
            try
            {
                var sessions = await LoadSessionsIndexAsync(category);
                var emptySessions = sessions.Where(s => s.MessageCount == 0).ToList();
                
                // Delete all empty session files
                foreach (var session in emptySessions)
                {
                    var sessionPath = GetSessionPath(session.Id);
                    if (File.Exists(sessionPath))
                    {
                        try { File.Delete(sessionPath); } catch { }
                    }
                }

                // Remove from index
                sessions.RemoveAll(s => s.MessageCount == 0);
                await SaveSessionIndexAsync(sessions, category);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up empty sessions for {category}: {ex.Message}");
            }
        }

        #region Migration

        /// <summary>
        /// Checks if the old history file needs migration
        /// </summary>
        public async Task<bool> NeedsMigrationAsync(string oldFileName)
        {
            var oldPath = Path.Combine(_appDirectory, oldFileName);
            return File.Exists(oldPath);
        }

        /// <summary>
        /// Migrates old single-file history to session-based format
        /// </summary>
        public async Task<ChatSession?> MigrateOldHistoryAsync(string oldFileName, string category = "chat")
        {
            try
            {
                var messages = await LoadHistoryAsync(oldFileName);
                
                if (messages.Count == 0)
                {
                    // No messages to migrate, just delete old file
                    ClearHistory(oldFileName);
                    return null;
                }

                // Create migrated session
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                var sessionId = $"{category}_migrated_{timestamp}";
                
                var title = GenerateTitleFromMessages(messages);
                var session = new ChatSession(sessionId, title)
                {
                    CreatedAt = DateTime.Now,
                    LastModified = DateTime.Now,
                    MessageCount = messages.Count,
                    PreviewText = GetPreviewText(messages),
                    IsActive = false
                };

                // Save migrated session
                await SaveSessionAsync(sessionId, messages, category);

                // Delete old file
                ClearHistory(oldFileName);

                return session;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error migrating history: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Helper Methods

        private string GenerateSessionId(string category)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            return $"{category}_{timestamp}";
        }

        private string GetSessionPath(string sessionId)
        {
            var categoryDir = sessionId.StartsWith("chat_") ? "chat" : "generator";
            var categoryPath = Path.Combine(_sessionsDirectory, categoryDir);
            
            if (!Directory.Exists(categoryPath))
            {
                Directory.CreateDirectory(categoryPath);
            }
            
            return Path.Combine(categoryPath, $"{sessionId}.json");
        }

        public string GenerateTitleFromMessages(List<ChatMessage> messages)
        {
            // Find first user message
            var firstUserMsg = messages.FirstOrDefault(m => m.Role == "User");
            
            if (firstUserMsg != null && !string.IsNullOrWhiteSpace(firstUserMsg.Content))
            {
                return TruncateTitle(firstUserMsg.Content);
            }

            // Fallback to timestamp
            return $"Chat - {DateTime.Now:MMM dd, yyyy h:mm tt}";
        }

        private string TruncateTitle(string text, int maxLength = 50)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Untitled Session";

            text = text.Trim().Replace("\r", " ").Replace("\n", " ");
            
            if (text.Length <= maxLength)
                return text;

            // Truncate at word boundary
            var truncated = text.Substring(0, maxLength);
            var lastSpace = truncated.LastIndexOf(' ');
            
            if (lastSpace > 0)
                truncated = truncated.Substring(0, lastSpace);
            
            return truncated + "...";
        }

        private string GetPreviewText(List<ChatMessage> messages, int maxLength = 100)
        {
            var firstAssistantMsg = messages.FirstOrDefault(m => m.Role == "Assistant");
            
            if (firstAssistantMsg != null && !string.IsNullOrWhiteSpace(firstAssistantMsg.Content))
            {
                var preview = firstAssistantMsg.Content.Trim().Replace("\r", " ").Replace("\n", " ");
                return preview.Length > maxLength ? preview.Substring(0, maxLength) + "..." : preview;
            }

            return string.Empty;
        }

        private async Task UpdateSessionInIndexAsync(string sessionId, List<ChatMessage> messages, string category)
        {
            var sessions = await LoadSessionsIndexAsync(category);
            var session = sessions.FirstOrDefault(s => s.Id == sessionId);

            if (session == null)
            {
                // Create new session entry
                session = new ChatSession(sessionId, GenerateTitleFromMessages(messages));
                sessions.Add(session);
            }

            // Update metadata
            session.Title = GenerateTitleFromMessages(messages);
            session.LastModified = DateTime.Now;
            session.MessageCount = messages.Count;
            session.PreviewText = GetPreviewText(messages);

            await SaveSessionIndexAsync(sessions, category);
        }

        private async Task SaveSessionIndexAsync(List<ChatSession> sessions, string category)
        {
            try
            {
                // Deduplicate sessions by ID (keep the most recent version)
                var uniqueSessions = sessions
                    .GroupBy(s => s.Id)
                    .Select(g => g.OrderByDescending(s => s.LastModified).First())
                    .ToList();
                    
                var indexPath = Path.Combine(_appDirectory, $"sessions_index_{category}.json");
                var json = JsonConvert.SerializeObject(uniqueSessions, Formatting.Indented);
                await File.WriteAllTextAsync(indexPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving session index: {ex.Message}");
            }
        }

        private async Task<List<ChatSession>> RebuildSessionIndexAsync(string category)
        {
            var sessions = new List<ChatSession>();
            var categoryDir = Path.Combine(_sessionsDirectory, category);

            if (!Directory.Exists(categoryDir))
                return sessions;

            try
            {
                var sessionFiles = Directory.GetFiles(categoryDir, "*.json");

                foreach (var file in sessionFiles)
                {
                    var sessionId = Path.GetFileNameWithoutExtension(file);
                    var messages = await LoadSessionMessagesAsync(sessionId);

                    var session = new ChatSession(sessionId, GenerateTitleFromMessages(messages))
                    {
                        CreatedAt = File.GetCreationTime(file),
                        LastModified = File.GetLastWriteTime(file),
                        MessageCount = messages.Count,
                        PreviewText = GetPreviewText(messages),
                        IsActive = false
                    };

                    sessions.Add(session);
                }

                // Save rebuilt index
                await SaveSessionIndexAsync(sessions, category);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rebuilding session index: {ex.Message}");
            }

            return sessions;
        }

        #endregion
    }
}
