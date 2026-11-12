using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace OpenCodeLauncher
{
    internal static class AppPaths
    {
        public static readonly string AppRoot;
        public static readonly string AppDir;
        public static readonly string StylesXaml;
        public static readonly string MainWindowXaml;
        public static readonly string SettingsWindowXaml;
        public static readonly string IconPath;
        public static readonly string LogoPath;
        public static readonly string LibDir;
        public static readonly string ConfigDir;
        public static readonly string DataDir;
        public static readonly string ConfigFile;
        public static readonly string LogFile;
        public static readonly string WebViewData;
        public static readonly string WorkDir;
        public static readonly string ServiceConfigFile;
        public static readonly string ServiceRuntimeFile;
        public static readonly int SessionId;

        static AppPaths()
        {
            AppRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var packagedApp = Path.Combine(AppRoot, "app");
            AppDir = Directory.Exists(packagedApp) ? packagedApp : AppRoot;

            StylesXaml = Path.Combine(AppDir, "Styles.xaml");
            MainWindowXaml = Path.Combine(AppDir, "MainWindow.xaml");
            SettingsWindowXaml = Path.Combine(AppDir, "SettingsWindow.xaml");
            IconPath = Path.Combine(AppDir, "assets", "opencode.ico");
            LogoPath = Path.Combine(AppDir, "assets", "logo.png");
            LibDir = Path.Combine(AppDir, "lib");

            ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenCodeLauncher");
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCodeLauncher");
            ConfigFile = Path.Combine(ConfigDir, "config.json");
            LogFile = Path.Combine(DataDir, "launcher.log");
            SessionId = Process.GetCurrentProcess().SessionId;
            WebViewData = Path.Combine(DataDir, "WebView2-" + SessionId.ToString());
            WorkDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            ServiceConfigFile = Path.Combine(home, ".config", "opencode", "service.json");
            ServiceRuntimeFile = Path.Combine(home, ".local", "state", "opencode", "service.json");
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(WebViewData);
        }

        public static void RotateLog()
        {
            try
            {
                var info = new FileInfo(LogFile);
                if (!info.Exists || info.Length <= 2 * 1024 * 1024) return;
                File.Copy(LogFile, LogFile + ".1", true);
                File.WriteAllText(LogFile, string.Empty);
            }
            catch
            {
            }
        }
    }
}
