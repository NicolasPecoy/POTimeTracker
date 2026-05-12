using System;
using System.IO;
using System.Runtime.InteropServices;

namespace POTimeTracker.Avalonia.Services
{
    public static class AutostartService
    {
        private const string MacPlistName = "com.invenzis.potimetracker.plist";
        private const string LinuxDesktopFile = "potimetracker.desktop";
        private const string WindowsRunKeyName = "POTimeTracker.Avalonia";

        public static bool IsEnabled()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var args = Environment.GetCommandLineArgs();
                    var exe = Environment.ProcessPath ?? string.Empty;
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                    if (key == null) return false;
                    var val = key.GetValue(WindowsRunKeyName) as string;
                    return !string.IsNullOrEmpty(val);
                }
                catch { return false; }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "LaunchAgents", MacPlistName);
                return File.Exists(path);
            }

            // Assume Linux/others
            var autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", "autostart");
            var desktopPath = Path.Combine(autostartDir, LinuxDesktopFile);
            return File.Exists(desktopPath);
        }

        public static void Enable(bool enable)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                    if (key == null) return;
                    if (enable)
                        key.SetValue(WindowsRunKeyName, $"\"{exe}\" --background");
                    else
                        key.DeleteValue(WindowsRunKeyName, false);
                }
                catch { }
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    var launchAgents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "LaunchAgents");
                    Directory.CreateDirectory(launchAgents);
                    var plistPath = Path.Combine(launchAgents, MacPlistName);
                    if (enable)
                    {
                        var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                        var plist = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                                    "<!DOCTYPE plist PUBLIC \"-//Apple Computer//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                                    "<plist version=\"1.0\">\n" +
                                    "<dict>\n" +
                                    $"  <key>Label</key><string>com.invenzis.potimetracker</string>\n" +
                                    "  <key>RunAtLoad</key><true/>\n" +
                                    "  <key>KeepAlive</key><false/>\n" +
                                    "  <key>ProgramArguments</key>\n" +
                                    "  <array>\n" +
                                    $"    <string>{exe}</string>\n" +
                                    "    <string>--background</string>\n" +
                                    "  </array>\n" +
                                    "</dict>\n" +
                                    "</plist>";
                        File.WriteAllText(plistPath, plist);
                    }
                    else if (File.Exists(plistPath)) File.Delete(plistPath);
                }
                catch { }
                return;
            }

            // Linux / other freedesktop
            try
            {
                var autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", "autostart");
                Directory.CreateDirectory(autostartDir);
                var desktopPath = Path.Combine(autostartDir, LinuxDesktopFile);
                if (enable)
                {
                    var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    var content = $"[Desktop Entry]\nType=Application\nVersion=1.0\nName=POTimeTracker\nExec=\"{exe}\" --background\nStartupNotify=false\nTerminal=false\n";
                    File.WriteAllText(desktopPath, content);
                }
                else if (File.Exists(desktopPath)) File.Delete(desktopPath);
            }
            catch { }
        }
    }
}
