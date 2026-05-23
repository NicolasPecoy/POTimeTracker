using System;
using System.IO;

namespace POTimeTracker.Services
{
    internal static class EnvLoader
    {
        internal static void Load(string fileName = ".env")
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(Directory.GetCurrentDirectory(), fileName)
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;

                try
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

                        var idx = trimmed.IndexOf('=');
                        if (idx <= 0) continue;

                        var key   = trimmed[..idx].Trim();
                        var value = trimmed[(idx + 1)..].Trim();

                        if (!string.IsNullOrEmpty(key))
                            Environment.SetEnvironmentVariable(key, value);
                    }

                    LogService.Info($"EnvLoader: cargado desde {path}");
                }
                catch (Exception ex)
                {
                    LogService.Error($"EnvLoader: error al leer {path}", ex);
                }
                return;
            }
        }
    }
}
