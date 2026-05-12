using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using POTimeTracker.Models;

namespace POTimeTracker.Services
{
    /// <summary>
    /// Stores and retrieves credentials securely using Windows DPAPI.
    /// Credentials are encrypted and stored in the user's AppData folder.
    /// </summary>
    public static class CredentialService
    {
        private static readonly string AppFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "POTimeTracker");

        private static readonly string CredFile = Path.Combine(AppFolder, "cred.dat");
        private static readonly string ConfigFile = Path.Combine(AppFolder, "config.json");

        public static void SaveCredentials(string username, string password, string serverUrl)
        {
            Directory.CreateDirectory(AppFolder);

            var payload = JsonSerializer.Serialize(new { username, password, serverUrl });
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(payload),
                null,
                DataProtectionScope.CurrentUser);

            File.WriteAllBytes(CredFile, encrypted);
        }

        public static LoginCredentials? LoadCredentials()
        {
            if (!File.Exists(CredFile))
                return null;

            try
            {
                var encrypted = File.ReadAllBytes(CredFile);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
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

        public static void ClearCredentials()
        {
            if (File.Exists(CredFile))
                File.Delete(CredFile);
        }

        public static void SaveConfig(LoginCredentials config)
        {
            Directory.CreateDirectory(AppFolder);
            var json = JsonSerializer.Serialize(new
            {
                config.WeeklyTarget,
                config.ServerUrl,
                config.ReminderHour,
                config.ReminderMinute,
                config.ReminderOnSaturday,
                config.ReminderOnSunday,
                config.ReloginIntervalHours
            });
            File.WriteAllText(ConfigFile, json);
        }

        public static LoginCredentials? LoadConfig()
        {
            if (!File.Exists(ConfigFile))
                return null;
            try
            {
                var json = File.ReadAllText(ConfigFile);
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                return new LoginCredentials
                {
                    WeeklyTarget = data.TryGetProperty("WeeklyTarget", out var wt) ? wt.GetDouble() : 40,
                    ServerUrl = data.TryGetProperty("ServerUrl", out var su) ? su.GetString() ?? "" : "",
                    ReminderHour = data.TryGetProperty("ReminderHour", out var rh) ? rh.GetInt32() : 17,
                    ReminderMinute = data.TryGetProperty("ReminderMinute", out var rm) ? rm.GetInt32() : 15,
                    ReminderOnSaturday = data.TryGetProperty("ReminderOnSaturday", out var rs) && rs.GetBoolean(),
                    ReminderOnSunday = data.TryGetProperty("ReminderOnSunday", out var rsu) && rsu.GetBoolean(),
                    ReloginIntervalHours = data.TryGetProperty("ReloginIntervalHours", out var ri) ? ri.GetDouble() : 3.0
                };
            }
            catch { return null; }
        }
    }
}
