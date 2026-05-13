using System;
using System.IO;

namespace POTimeTracker.Services
{
    public static class LogService
    {
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "POTimeTracker", "logs");

        private static readonly object _lock = new();
        private static DateTime _lastPruned = DateTime.MinValue;

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                var file = Path.Combine(LogFolder, $"app-{DateTime.Today:yyyy-MM-dd}.log");

                var text = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
                if (ex != null)
                    text += $"{Environment.NewLine}  {ex.GetType().Name}: {ex.Message}{Environment.NewLine}  {ex.StackTrace?.Replace(Environment.NewLine, Environment.NewLine + "  ")}";
                text += Environment.NewLine;

                lock (_lock)
                {
                    File.AppendAllText(file, text);
                    PruneOldLogsIfNeeded();
                }
            }
            catch { /* Logging must never crash the app */ }
        }

        private static void PruneOldLogsIfNeeded()
        {
            if (_lastPruned == DateTime.Today) return;
            _lastPruned = DateTime.Today;

            try
            {
                var cutoff = DateTime.Today.AddDays(-7);
                foreach (var f in Directory.GetFiles(LogFolder, "app-*.log"))
                {
                    var datePart = Path.GetFileNameWithoutExtension(f).Replace("app-", "");
                    if (DateTime.TryParse(datePart, out var date) && date < cutoff)
                        File.Delete(f);
                }
            }
            catch { }
        }
    }
}
