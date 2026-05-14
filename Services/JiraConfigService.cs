using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using POTimeTracker.Models;

namespace POTimeTracker.Services
{
    public static class JiraConfigService
    {
        private static readonly string AppFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "POTimeTracker");

        private static readonly string ConfigFile = Path.Combine(AppFolder, "jira_config.json");
        private static readonly string TokenFile  = Path.Combine(AppFolder, "jira_token.dat");

        public static void SaveConfig(JiraConfig config, string apiToken)
        {
            Directory.CreateDirectory(AppFolder);

            var json = JsonSerializer.Serialize(new
            {
                config.BaseUrl,
                config.Email,
                config.DefaultProjectKey,
                config.Enabled
            });
            File.WriteAllText(ConfigFile, json);

            if (!string.IsNullOrEmpty(apiToken))
            {
                var encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(apiToken),
                    null,
                    DataProtectionScope.CurrentUser);
                File.WriteAllBytes(TokenFile, encrypted);
            }
        }

        public static (JiraConfig? Config, string ApiToken) LoadConfig()
        {
            if (!File.Exists(ConfigFile))
                return (null, "");

            try
            {
                var json = File.ReadAllText(ConfigFile);
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                var config = new JiraConfig
                {
                    BaseUrl          = GetString(data, "BaseUrl"),
                    Email            = GetString(data, "Email"),
                    DefaultProjectKey = GetString(data, "DefaultProjectKey"),
                    Enabled          = data.TryGetProperty("Enabled", out var en) && en.GetBoolean()
                };

                var token = "";
                if (File.Exists(TokenFile))
                {
                    try
                    {
                        var encrypted = File.ReadAllBytes(TokenFile);
                        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                        token = Encoding.UTF8.GetString(decrypted);
                    }
                    catch (Exception ex)
                    {
                        LogService.Warn("JiraConfigService: no se pudo descifrar el token", ex);
                    }
                }

                return (config, token);
            }
            catch (Exception ex)
            {
                LogService.Error("JiraConfigService.LoadConfig: error al leer config", ex);
                return (null, "");
            }
        }

        public static void ClearConfig()
        {
            if (File.Exists(ConfigFile)) File.Delete(ConfigFile);
            if (File.Exists(TokenFile))  File.Delete(TokenFile);
        }

        public static bool IsConfigured()
        {
            var (config, token) = LoadConfig();
            return config != null
                && !string.IsNullOrWhiteSpace(config.BaseUrl)
                && !string.IsNullOrWhiteSpace(config.Email)
                && !string.IsNullOrWhiteSpace(token);
        }

        private static string GetString(JsonElement el, string key) =>
            el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
    }
}
