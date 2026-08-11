using System;
using System.IO;
using POTimeTracker.Models;

namespace POTimeTracker.Services
{
    /// <summary>
    /// Holds the active Jira connection (URL, email, API token) in memory only, for the
    /// lifetime of the running process. Nothing is written to disk: on every app restart
    /// the user has to reconnect via the Jira window. This is intentional — a Jira API
    /// token must never be cached on disk, even encrypted.
    /// </summary>
    public static class JiraConfigService
    {
        private static readonly string AppFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "POTimeTracker");

        private static readonly string LegacyConfigFile = Path.Combine(AppFolder, "jira_config.json");
        private static readonly string LegacyTokenFile  = Path.Combine(AppFolder, "jira_token.dat");

        private static JiraConfig? _config;
        private static string _apiToken = "";

        public static void SaveConfig(JiraConfig config, string apiToken)
        {
            _config = new JiraConfig
            {
                BaseUrl           = config.BaseUrl,
                Email             = config.Email,
                DefaultProjectKey = config.DefaultProjectKey,
                Enabled           = config.Enabled
            };
            _apiToken = apiToken ?? "";
        }

        public static (JiraConfig? Config, string ApiToken) LoadConfig() => (_config, _apiToken);

        public static void ClearConfig()
        {
            _config = null;
            _apiToken = "";
        }

        public static bool IsConfigured()
        {
            return _config != null
                && !string.IsNullOrWhiteSpace(_config.BaseUrl)
                && !string.IsNullOrWhiteSpace(_config.Email)
                && !string.IsNullOrWhiteSpace(_apiToken);
        }

        /// <summary>
        /// One-time cleanup for installs that still have Jira credentials cached on disk from
        /// before this became memory-only. Safe to call on every startup — it's a no-op once
        /// the legacy files are gone.
        /// </summary>
        public static void PurgeLegacyDiskCache()
        {
            try
            {
                if (File.Exists(LegacyConfigFile)) File.Delete(LegacyConfigFile);
                if (File.Exists(LegacyTokenFile))  File.Delete(LegacyTokenFile);
            }
            catch (Exception ex)
            {
                LogService.Warn("JiraConfigService.PurgeLegacyDiskCache: no se pudieron borrar archivos viejos", ex);
            }
        }
    }
}
