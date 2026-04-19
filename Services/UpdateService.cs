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
            // Removed OS check to allow notification on all platforms
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
                var releaseUrl = json["html_url"]?.ToString(); // Get release page URL
                var assets = json["assets"] as JArray;
                
                // Find exe asset (Windows only)
                string? downloadUrl = null;
                if (assets != null)
                {
                    foreach(var asset in assets)
                    {
                        var name = asset["name"]?.ToString();
                        // Only look for .exe or specific windows asset
                        if (name != null && (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                        {
                            downloadUrl = asset["browser_download_url"]?.ToString();
                            break;
                        }
                    }
                }

                if (tagName != null)
                {
                    var currentVerStr = CurrentVersion.TrimStart('v');
                    var remoteVerStr = tagName.TrimStart('v');
                    bool isUpdate = false;

                    if (Version.TryParse(currentVerStr, out var currentVer) && 
                        Version.TryParse(remoteVerStr, out var remoteVer))
                    {
                        if (remoteVer > currentVer) isUpdate = true;
                    }
                    else if (tagName != CurrentVersion)
                    {
                         isUpdate = true;
                    }

                    if (isUpdate)
                    {
                        return new UpdateInfo 
                        { 
                            Version = tagName, 
                            Changelog = body, 
                            DownloadUrl = downloadUrl, // May be null on Linux/Mac
                            ReleaseUrl = releaseUrl 
                        };
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
             if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
             {
                 throw new PlatformNotSupportedException("Auto-update is only supported on Windows.");
             }

             // Resolve paths first
             var currentExe = Environment.ProcessPath;
             if (string.IsNullOrEmpty(currentExe)) currentExe = Process.GetCurrentProcess().MainModule.FileName;
             var currentDir = Path.GetDirectoryName(currentExe);

            // Define local download path
             // We first download to a temp file to prevent users from opening incomplete downloads
             var tempFilePath = Path.Combine(currentDir, "TagForge.Update.tmp");
             
             // Download
             var options = new RestClientOptions(downloadUrl);
             using var client = new RestClient(options);
             var request = new RestRequest("");
             var response = await client.ExecuteAsync(request);
             
             if (!response.IsSuccessful || response.RawBytes == null) throw new Exception("Download failed");
             
             // Save to local dir (will overwrite if exists from previous failed run)
             File.WriteAllBytes(tempFilePath, response.RawBytes);

             // Create Batch Script
             // Force target to be TagForge.exe
             var targetExe = Path.Combine(currentDir, "TagForge.exe");
             var batPath = Path.Combine(currentDir, "update.bat");
             
             // Script: Wait 1s (for app to close), Move Update -> Target, Start Target, Del Script
             var script = $@"
@echo off
timeout /t 1 /nobreak > nul
move /Y ""{tempFilePath}"" ""{targetExe}""
cd /d ""{currentDir}""
start """" ""{targetExe}""
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
        public string? DownloadUrl { get; set; }
        public string? ReleaseUrl { get; set; }
    }
}
