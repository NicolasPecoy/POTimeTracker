using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private static readonly string EntriesFile = Path.Combine(AppFolder, "entries.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = false
        };

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
            catch (Exception ex)
            {
                LogService.Error("LoadCredentials: error al leer credenciales", ex);
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
                config.ReloginIntervalHours,
                config.StartDateAsToday
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
                    ReloginIntervalHours = data.TryGetProperty("ReloginIntervalHours", out var ri) ? ri.GetDouble() : 3.0,
                    StartDateAsToday = !data.TryGetProperty("StartDateAsToday", out var sdt) || sdt.GetBoolean()
                };
            }
            catch (Exception ex)
            {
                LogService.Error("LoadConfig: error al leer configuracion", ex);
                return null;
            }
        }

        public static void SaveEntries(Dictionary<string, List<TimeEntry>> entries)
        {
            try
            {
                Directory.CreateDirectory(AppFolder);
                var json = JsonSerializer.Serialize(entries, JsonOpts);
                File.WriteAllText(EntriesFile, json);
            }
            catch (Exception ex) { LogService.Error("SaveEntries: error al guardar registros", ex); }
        }

        public static Dictionary<string, List<TimeEntry>> LoadEntries()
        {
            if (!File.Exists(EntriesFile))
                return new Dictionary<string, List<TimeEntry>>();
            try
            {
                var json = File.ReadAllText(EntriesFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, List<TimeEntry>>>(json);
                if (data == null) return new Dictionary<string, List<TimeEntry>>();

                // Keep only last 3 months
                var cutoff = DateTime.Today.AddMonths(-3);
                return data
                    .Where(kv => DateTime.TryParse(kv.Key, out var d) && d.Date >= cutoff)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
            catch (Exception ex)
            {
                LogService.Error("LoadEntries: error al leer registros", ex);
                return new Dictionary<string, List<TimeEntry>>();
            }
        }
    }
}
