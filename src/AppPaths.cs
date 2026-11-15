using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

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
        public static readonly string LegacyConfigDir;
        public static readonly string DataDir;
        public static readonly string ConfigPointerFile;
        public static readonly string ConfigMigrationFile;
        public static readonly string LogFile;
        public static readonly string WebViewData;
        public static readonly string WorkDir;
        public static readonly string ServiceConfigFile;
        public static readonly string ServiceRuntimeFile;
        public static readonly int SessionId;

        // 启动时由 ConfigLocation 决定：默认用户目录，可切到程序目录或用户自选目录。
        public static string ConfigDir;

        public static string ConfigFile
        {
            get { return ConfigPathFor(ConfigDir); }
        }

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

            LegacyConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenCodeLauncher");
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCodeLauncher");
            ConfigPointerFile = Path.Combine(DataDir, "config-dir.txt");
            ConfigMigrationFile = Path.Combine(DataDir, "config-migrated.txt");
            ConfigDir = LegacyConfigDir;
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

        public static string ConfigPathFor(string directory)
        {
            return Path.Combine(directory, "config.json");
        }

        public static void UseConfigDir(string directory)
        {
            if (!string.IsNullOrEmpty(directory)) ConfigDir = directory;
        }

        /// <summary>实际写一个探针文件，确认目录可写；只读目录或文件路径都会返回 false。</summary>
        public static bool CanWriteTo(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return false;
            var probe = Path.Combine(directory, ".oc-write-test");
            try
            {
                Directory.CreateDirectory(directory);
                using (var stream = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.WriteByte(0);
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(probe)) File.Delete(probe);
                }
                catch
                {
                }
            }
        }

        public static bool MigrateConfig(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return false;
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                if (!File.Exists(source) || File.Exists(target)) return false;
                File.Copy(source, target);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool MigrateLegacyConfig()
        {
            if (File.Exists(ConfigMigrationFile)) return false;

            var source = ConfigPathFor(LegacyConfigDir);
            var target = ConfigFile;
            var copied = MigrateConfig(source, target);
            if (copied) Logger.Info("已迁移 Launcher 配置: " + source + " -> " + target);
            MarkConfigMigration();
            return copied;
        }

        /// <summary>迁移只做一次：避免程序目录里的配置文件被删掉后又从旧目录覆盖回来。</summary>
        private static void MarkConfigMigration()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(ConfigMigrationFile, ConfigFile, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public static string ReadConfigPointer()
        {
            try
            {
                if (!File.Exists(ConfigPointerFile)) return string.Empty;
                return File.ReadAllText(ConfigPointerFile, Encoding.UTF8).Trim();
            }
            catch (Exception ex)
            {
                Logger.Warn("读取配置目录记录失败: " + ex.Message);
                return string.Empty;
            }
        }

        public static bool WriteConfigPointer(string directory)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(ConfigPointerFile, directory, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("保存配置目录记录失败: " + ex.Message);
                return false;
            }
        }

        public static void ClearConfigPointer()
        {
            try
            {
                if (File.Exists(ConfigPointerFile)) File.Delete(ConfigPointerFile);
            }
            catch (Exception ex)
            {
                Logger.Warn("清除配置目录记录失败: " + ex.Message);
            }
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
