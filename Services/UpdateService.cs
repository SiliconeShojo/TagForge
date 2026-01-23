using RestSharp;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.IO;

namespace TagForge.Services
{
    public class UpdateService
    {
        private const string Owner = "SiliconeShojo";
        private const string Repo = "TagForge";
        
        // Read version from Assembly (set in .csproj)
        public string CurrentVersion => "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);

        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try 
            {
                var options = new RestClientOptions($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
                using var client = new RestClient(options);
                var request = new RestRequest("");
                request.AddHeader("User-Agent", "TagForge-App");

                var response = await client.ExecuteGetAsync(request);
                if (!response.IsSuccessful) return null;

                var json = JObject.Parse(response.Content);
                var tagName = json["tag_name"]?.ToString();
                var body = json["body"]?.ToString();
                var assets = json["assets"] as JArray;
                
                // Find exe asset
                string? downloadUrl = null;
                if (assets != null)
                {
                    foreach(var asset in assets)
                    {
                        var name = asset["name"]?.ToString();
                        if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset["browser_download_url"]?.ToString();
                            break;
                        }
                    }
                }

                if (tagName != null && downloadUrl != null)
                {
                    var currentVerStr = CurrentVersion.TrimStart('v');
                    var remoteVerStr = tagName.TrimStart('v');

                    if (Version.TryParse(currentVerStr, out var currentVer) && 
                        Version.TryParse(remoteVerStr, out var remoteVer))
                    {
                        if (remoteVer > currentVer)
                        {
                            return new UpdateInfo { Version = tagName, Changelog = body, DownloadUrl = downloadUrl };
                        }
                    }
                    else if (tagName != CurrentVersion)
                    {
                         // Fallback to string comparison if parsing fails
                         return new UpdateInfo { Version = tagName, Changelog = body, DownloadUrl = downloadUrl };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update check failed: {ex.Message}");
            }
            return null;
        }

        public async Task PerformUpdateAsync(string downloadUrl)
        {
             // Download to temp
             var tempPath = Path.GetTempFileName().Replace(".tmp", ".exe");
             
             // Download
             var options = new RestClientOptions(downloadUrl);
             using var client = new RestClient(options);
             var request = new RestRequest("");
             var response = await client.ExecuteAsync(request);
             
             if (!response.IsSuccessful || response.RawBytes == null) throw new Exception("Download failed");
             File.WriteAllBytes(tempPath, response.RawBytes);

             // Create Batch Script
             // Use Environment.ProcessPath for net6+ reliable exe path
             var currentExe = Environment.ProcessPath;
             if (string.IsNullOrEmpty(currentExe)) currentExe = Process.GetCurrentProcess().MainModule.FileName;
             
             var currentDir = Path.GetDirectoryName(currentExe);
             var batPath = Path.Combine(currentDir, "update.bat");
             
             // Script: Wait 2s, Move temp to current, Start current, Del script
             var script = $@"
@echo off
timeout /t 3 /nobreak > nul
move /Y ""{tempPath}"" ""{currentExe}""
cd /d ""{currentDir}""
start """" ""{currentExe}""
del ""%~f0""
";
             File.WriteAllText(batPath, script);
             
             // Run script hidden
             var psi = new ProcessStartInfo
             {
                 FileName = batPath,
                 UseShellExecute = true,
                 CreateNoWindow = true,
                 WindowStyle = ProcessWindowStyle.Hidden
             };
             Process.Start(psi);
             
             // Kill self
             Environment.Exit(0);
        }
    }

    public class UpdateInfo
    {
        public string Version { get; set; }
        public string Changelog { get; set; }
        public string DownloadUrl { get; set; }
    }
}
