using RestSharp;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Net.Http;

namespace TagForge.Services
{
    public class UpdateService
    {
        private const string Owner = "SiliconeShojo";
        private const string Repo = "TagForge";
        
        // Read version from Assembly (set in .csproj)
        public string CurrentVersion => "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);

        private string GetExpectedAssetName()
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => "unknown"
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return $"TagForge-win-x64.exe"; // User specified win-x64.exe
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return $"TagForge-linux-{arch}";
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return $"TagForge-osx-{arch}";

            return "unknown";
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try 
            {
                var options = new RestClientOptions($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
                using var client = new RestClient(options);
                var request = new RestRequest("");
                request.AddHeader("User-Agent", "TagForge-App");

                var response = await client.ExecuteGetAsync(request);
                if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content)) return null;

                var json = JObject.Parse(response.Content);
                var tagName = json["tag_name"]?.ToString();
                var body = json["body"]?.ToString();
                var releaseUrl = json["html_url"]?.ToString();
                var assets = json["assets"] as JArray;
                
                if (tagName == null) return null;

                // Version check
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

                if (!isUpdate) return null;

                // Asset Matching
                var expectedName = GetExpectedAssetName();
                string? downloadUrl = null;

                if (assets != null)
                {
                    foreach(var asset in assets)
                    {
                        var name = asset["name"]?.ToString();
                        if (name != null && name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset["browser_download_url"]?.ToString();
                            break;
                        }
                    }
                }

                // Per User Request: Don't prompt for update if no compatible asset found
                if (downloadUrl == null) return null;

                return new UpdateInfo 
                { 
                    Version = tagName, 
                    Changelog = body ?? "No changelog provided.", 
                    DownloadUrl = downloadUrl, 
                    ReleaseUrl = releaseUrl 
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
            }
            return null;
        }

        public async Task PerformUpdateAsync(string downloadUrl, IProgress<float>? progress = null)
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe)) currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "TagForge.exe";
            var currentDir = Path.GetDirectoryName(currentExe);
            var tempFilePath = Path.Combine(currentDir!, "TagForge.Update.tmp");

            // Download using HttpClient for better progress control
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("User-Agent", "TagForge-App");
                using (var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var canReportProgress = totalBytes != -1 && progress != null;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        int read;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            if (canReportProgress)
                            {
                                progress!.Report((float)totalRead / totalBytes);
                            }
                        }
                    }
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await InstallWindowsUpdate(tempFilePath, currentExe, currentDir!);
            }
            else
            {
                await InstallUnixUpdate(tempFilePath, currentExe);
            }
        }

        private async Task InstallWindowsUpdate(string tempFile, string targetExe, string currentDir)
        {
            var batPath = Path.Combine(currentDir, "update.bat");
            var script = $@"
@echo off
timeout /t 1 /nobreak > nul
move /Y ""{tempFile}"" ""{targetExe}""
cd /d ""{currentDir}""
start """" ""{targetExe}""
del ""%~f0""
";
            await File.WriteAllTextAsync(batPath, script);
            
            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Environment.Exit(0);
        }

        private async Task InstallUnixUpdate(string tempFile, string targetExe)
        {
            // On Unix, we can replace the running binary. 
            // 1. Set permissions
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try 
                {
                    // User Execute | User Read | User Write (0755 equivalent)
                    File.SetUnixFileMode(tempFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | 
                                                  UnixFileMode.GroupRead | UnixFileMode.GroupExecute | 
                                                  UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { /* Fallback if SetUnixFileMode is restricted */ }
            }

            // 2. Move to target (replaces running binary inode)
            File.Move(tempFile, targetExe, true);

            // 3. Restart app
            Process.Start(new ProcessStartInfo
            {
                FileName = targetExe,
                UseShellExecute = true
            });
            Environment.Exit(0);
        }
    }

    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string Changelog { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? ReleaseUrl { get; set; }
    }
}
