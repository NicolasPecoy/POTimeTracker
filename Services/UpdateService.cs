using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace POTimeTracker.Services
{
    public static class UpdateService
    {
        private const string RepoApi = "https://api.github.com/repos/NicolasPecoy/POTimeTracker/releases/latest";

        public static string GetCurrentVersion()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
        }

        public static async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("POTimeTracker", GetCurrentVersion()));

                var json = await http.GetStringAsync(RepoApi);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
                var htmlUrl = root.GetProperty("html_url").GetString() ?? string.Empty;
                string downloadUrl = string.Empty;

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? string.Empty;
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                            break;
                        }
                    }
                }

                // Strip leading 'v' from tag (e.g. "v1.2.0" → "1.2.0")
                var latestRaw = tagName.TrimStart('v');

                if (Version.TryParse(latestRaw, out var latest) &&
                    Version.TryParse(GetCurrentVersion(), out var current) &&
                    latest > current)
                {
                    return new UpdateInfo(tagName, latestRaw, downloadUrl, htmlUrl);
                }

                return null;
            }
            catch (Exception ex)
            {
                LogService.Warn("UpdateService.CheckForUpdateAsync: error al consultar GitHub", ex);
                return null;
            }
        }

        public static async Task PerformUpdateAsync(string downloadUrl)
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe)) return;

            var tempDir = Path.GetTempPath();
            var newExe  = Path.Combine(tempDir, "POTimeTracker_update.exe");
            var script  = Path.Combine(tempDir, "pott_update.bat");

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("POTimeTracker", GetCurrentVersion()));

            var bytes = await http.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(newExe, bytes);

            // Batch script: wait for this process to exit, then replace exe and restart
            var bat = $"""
@echo off
:LOOP
tasklist /FI "IMAGENAME eq POTimeTracker.exe" 2>nul | find /I "POTimeTracker.exe" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto LOOP
)
copy /Y "{newExe}" "{currentExe}"
start "" "{currentExe}"
del "{script}"
del "{newExe}"
""";
            await File.WriteAllTextAsync(script, bat, System.Text.Encoding.ASCII);

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });

            System.Windows.Application.Current.Shutdown();
        }
    }

    public record UpdateInfo(string Tag, string Version, string DownloadUrl, string ReleasePageUrl);
}
