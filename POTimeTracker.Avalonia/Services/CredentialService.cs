using System;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using POTimeTracker.Models;

namespace POTimeTracker.Avalonia.Services
{
    // Cross-platform credential storage using ASP.NET Core Data Protection.
    public class CredentialService
    {
        private readonly string AppFolder;
        private readonly string CredFile;
        private readonly string ConfigFile;
        private readonly IDataProtector _protector;

        public CredentialService()
        {
            AppFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "POTimeTracker.Avalonia");
            CredFile = Path.Combine(AppFolder, "cred.dat");
            ConfigFile = Path.Combine(AppFolder, "config.json");

            Directory.CreateDirectory(AppFolder);
            var keysDir = Path.Combine(AppFolder, "keys");
            Directory.CreateDirectory(keysDir);
            var provider = DataProtectionProvider.Create(new DirectoryInfo(keysDir));
            _protector = provider.CreateProtector("POTimeTracker.CredentialProtection");
        }

        public void SaveCredentials(string username, string password, string serverUrl)
        {
            var payload = JsonSerializer.Serialize(new { username, password, serverUrl });
            var protectedText = _protector.Protect(payload);
            File.WriteAllText(CredFile, protectedText);
        }

        public LoginCredentials? LoadCredentials()
        {
            if (!File.Exists(CredFile)) return null;
            try
            {
                var protectedText = File.ReadAllText(CredFile);
                var json = _protector.Unprotect(protectedText);
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                var config = LoadConfig();
                return new LoginCredentials
                {
                    Username = data.GetProperty("username").GetString() ?? "",
                    Password = data.GetProperty("password").GetString() ?? "",
                    ServerUrl = data.GetProperty("serverUrl").GetString() ?? "http://po.invenzis.com:8080",
                    RememberMe = true,
                    WeeklyTarget = config?.WeeklyTarget ?? 40
                };
            }
            catch
            {
                return null;
            }
        }

        public void ClearCredentials()
        {
            if (File.Exists(CredFile)) File.Delete(CredFile);
        }

        public void SaveConfig(LoginCredentials config)
        {
            var json = JsonSerializer.Serialize(new { config.WeeklyTarget, config.ServerUrl });
            File.WriteAllText(ConfigFile, json);
        }

        public LoginCredentials? LoadConfig()
        {
            if (!File.Exists(ConfigFile)) return null;
            try
            {
                var json = File.ReadAllText(ConfigFile);
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                return new LoginCredentials
                {
                    WeeklyTarget = data.TryGetProperty("WeeklyTarget", out var wt) ? wt.GetDouble() : 40,
                    ServerUrl = data.TryGetProperty("ServerUrl", out var su) ? su.GetString() ?? "" : ""
                };
            }
            catch { return null; }
        }
    }
}
