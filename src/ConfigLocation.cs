using System;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace OpenCodeLauncher
{
    /// <summary>决定 Launcher 配置目录：优先程序目录，其次用户自选目录或 %APPDATA%。</summary>
    internal static class ConfigLocation
    {
        public static void Resolve()
        {
            var saved = AppPaths.ReadConfigPointer();
            var hadPointer = !string.IsNullOrEmpty(saved);
            if (hadPointer)
            {
                if (AppPaths.CanWriteTo(saved))
                {
                    Activate(saved);
                    return;
                }
                Logger.Warn("已记录的配置目录不可写，将重新选择: " + saved);
            }

            if (AppPaths.CanWriteTo(AppPaths.AppRoot))
            {
                Activate(AppPaths.AppRoot);
                if (hadPointer) AppPaths.ClearConfigPointer();
                return;
            }

            Logger.Warn("程序目录不可写，无法把配置保存在: " + AppPaths.AppRoot);
            var chosen = PromptForDirectory();
            if (string.IsNullOrEmpty(chosen)) chosen = AppPaths.LegacyConfigDir;
            if (!AppPaths.CanWriteTo(chosen))
            {
                MessageBox.Show(
                    "所选目录仍然无法写入，将使用用户配置目录：" + Environment.NewLine + AppPaths.LegacyConfigDir,
                    "OpenCode Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                chosen = AppPaths.LegacyConfigDir;
                AppPaths.CanWriteTo(chosen);
            }

            Activate(chosen);
            if (!string.Equals(chosen, AppPaths.AppRoot, StringComparison.OrdinalIgnoreCase))
            {
                AppPaths.WriteConfigPointer(chosen);
            }
        }

        private static void Activate(string directory)
        {
            AppPaths.UseConfigDir(directory);
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex)
            {
                Logger.Warn("创建配置目录失败: " + ex.Message);
            }
            AppPaths.MigrateLegacyConfig();
            Logger.Info("Launcher 配置: " + AppPaths.ConfigFile);
        }

        private static string PromptForDirectory()
        {
            try
            {
                var text =
                    "无法在程序目录保存配置：" + Environment.NewLine +
                    AppPaths.AppRoot + Environment.NewLine + Environment.NewLine +
                    "选择「是」使用用户配置目录：" + Environment.NewLine +
                    AppPaths.LegacyConfigDir + Environment.NewLine + Environment.NewLine +
                    "选择「否」自定义配置目录；选择「取消」保持用户配置目录。";
                var answer = MessageBox.Show(text, "OpenCode Launcher", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.No) return null;

                using (var dialog = new Forms.FolderBrowserDialog())
                {
                    dialog.Description = "选择 OpenCode Launcher 配置目录";
                    dialog.SelectedPath = AppPaths.LegacyConfigDir;
                    dialog.ShowNewFolderButton = true;
                    if (dialog.ShowDialog() != Forms.DialogResult.OK) return null;
                    return dialog.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("询问配置目录失败: " + ex.Message);
                return null;
            }
        }
    }
}
