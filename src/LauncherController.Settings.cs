using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OpenCodeLauncher
{
    internal sealed class SettingsResult
    {
        public SettingsValues Values;
        public bool Restart;
    }

    internal sealed partial class LauncherController
    {
        private async void OpenSettings()
        {
            try
            {
                var result = ShowSettingsDialog();
                if (result == null) return;
                await ApplySettingsAsync(result.Values, result.Restart);
            }
            catch (Exception ex)
            {
                Logger.Error("打开或应用设置失败: " + ex);
                ShowViewError("设置失败", ex.Message, true, false);
            }
        }

        private SettingsResult ShowSettingsDialog()
        {
            var dialog = XamlLoader.LoadWindow(AppPaths.SettingsWindowXaml);
            dialog.Owner = Window;
            dialog.Icon = Window.Icon;
            var work = SystemParameters.WorkArea;
            dialog.Width = Math.Min(dialog.Width, work.Width - 60);
            dialog.Height = Math.Min(dialog.Height, work.Height - 60);
            dialog.SourceInitialized += delegate(object sender, EventArgs e)
            {
                try
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(dialog);
                    Native.ApplyDarkTitleBar(helper.Handle);
                }
                catch
                {
                }
            };

            var serviceConfig = ServiceConfigOps.Read();
            dialog.FindName("S_Host").As<TextBox>().Text = Json.GetString(serviceConfig, "hostname", string.Empty);
            dialog.FindName("S_Port").As<TextBox>().Text = Json.GetString(serviceConfig, "port", string.Empty);
            dialog.FindName("S_Password").As<PasswordBox>().Password = Json.GetString(serviceConfig, "password", string.Empty);
            dialog.FindName("S_Cors").As<TextBox>().Text = ServiceConfigOps.FormatCors(Json.Get(serviceConfig, "cors"));
            dialog.FindName("S_Exe").As<TextBox>().Text = _openCodePath;
            dialog.FindName("S_AutoStart").As<CheckBox>().IsChecked = _config.AutoStart;
            dialog.FindName("S_CloseTray").As<CheckBox>().IsChecked = _config.CloseToTray;
            dialog.FindName("S_SuspendHide").As<CheckBox>().IsChecked = _config.SuspendOnHide;
            dialog.FindName("S_StopExit").As<CheckBox>().IsChecked = _config.StopOnExit;

            var serviceExists = File.Exists(AppPaths.ServiceConfigFile);
            var servicePathText = serviceExists
                ? AppPaths.ServiceConfigFile
                : AppPaths.ServiceConfigFile + "（尚未创建，保存时会生成）";
            dialog.FindName("S_ConfigPath").As<TextBlock>().Text =
                servicePathText + Environment.NewLine + AppPaths.ConfigFile;
            dialog.FindName("S_PasswordSource").As<TextBlock>().Text =
                "来源：" + AppPaths.ServiceConfigFile + " 的 password 字段（保存时写回该文件）";

            dialog.FindName("S_TogglePassword").As<Button>().Click += delegate
            {
                var passwordBox = dialog.FindName("S_Password").As<PasswordBox>();
                var plainBox = dialog.FindName("S_PasswordPlain").As<TextBox>();
                var toggle = dialog.FindName("S_TogglePassword").As<Button>();
                if (plainBox.Visibility == Visibility.Visible)
                {
                    passwordBox.Password = plainBox.Text;
                    plainBox.Visibility = Visibility.Collapsed;
                    passwordBox.Visibility = Visibility.Visible;
                    toggle.Content = "显示";
                }
                else
                {
                    plainBox.Text = passwordBox.Password;
                    plainBox.Visibility = Visibility.Visible;
                    passwordBox.Visibility = Visibility.Collapsed;
                    toggle.Content = "隐藏";
                }
            };

            dialog.FindName("S_Browse").As<Button>().Click += delegate
            {
                var picker = new OpenFileDialog();
                picker.Title = "选择 opencode 可执行文件";
                picker.Filter = "opencode|opencode.exe;opencode.cmd;opencode.ps1|所有文件|*.*";
                picker.CheckFileExists = true;
                var current = dialog.FindName("S_Exe").As<TextBox>().Text;
                if (!string.IsNullOrEmpty(current) && File.Exists(current))
                {
                    picker.InitialDirectory = Path.GetDirectoryName(current);
                }
                if (picker.ShowDialog(dialog) == true)
                {
                    dialog.FindName("S_Exe").As<TextBox>().Text = picker.FileName;
                }
            };

            dialog.FindName("S_DesktopShortcut").As<Button>().Click += delegate
            {
                try
                {
                    var link = CreateDesktopShortcut();
                    MessageBox.Show(dialog, "桌面快捷方式已创建：" + Environment.NewLine + link,
                        "OpenCode Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, ex.Message, "创建快捷方式失败",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            SettingsResult result = null;
            Action<bool> save = delegate(bool restart)
            {
                if (!string.IsNullOrEmpty(_operation))
                {
                    SetSettingsError(dialog, "有服务操作正在执行，请等它完成后再保存。");
                    return;
                }

                var values = GetSettingsValues(dialog);
                if (!TestSettingsValues(dialog, values)) return;

                if (values.Password.Length == 0 && SecretMasker.CurrentPassword.Length > 0)
                {
                    var answer = MessageBox.Show(
                        dialog,
                        "清空密码后，任何能访问该端口的人都可以打开 Web 界面。确定要取消密码保护？",
                        "取消密码保护",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);
                    if (answer != MessageBoxResult.Yes) return;
                }

                result = new SettingsResult();
                result.Values = values;
                result.Restart = restart;
                dialog.Close();
            };

            dialog.FindName("S_Cancel").As<Button>().Click += delegate { dialog.Close(); };
            dialog.FindName("S_Save").As<Button>().Click += delegate { save(false); };
            dialog.FindName("S_SaveRestart").As<Button>().Click += delegate { save(true); };

            dialog.ShowDialog();
            return result;
        }

        private SettingsValues GetSettingsValues(Window dialog)
        {
            var passwordBox = dialog.FindName("S_Password").As<PasswordBox>();
            var plainBox = dialog.FindName("S_PasswordPlain").As<TextBox>();
            var password = plainBox.Visibility == Visibility.Visible ? plainBox.Text : passwordBox.Password;

            var values = new SettingsValues();
            values.Host = dialog.FindName("S_Host").As<TextBox>().Text.Trim();
            values.Port = dialog.FindName("S_Port").As<TextBox>().Text.Trim();
            values.Password = password;
            values.Cors = dialog.FindName("S_Cors").As<TextBox>().Text.Trim();
            values.ExePath = dialog.FindName("S_Exe").As<TextBox>().Text.Trim();
            values.AutoStart = dialog.FindName("S_AutoStart").As<CheckBox>().IsChecked == true;
            values.CloseToTray = dialog.FindName("S_CloseTray").As<CheckBox>().IsChecked == true;
            values.SuspendOnHide = dialog.FindName("S_SuspendHide").As<CheckBox>().IsChecked == true;
            values.StopOnExit = dialog.FindName("S_StopExit").As<CheckBox>().IsChecked == true;
            return values;
        }

        private bool TestSettingsValues(Window dialog, SettingsValues values)
        {
            if (!string.IsNullOrEmpty(values.Port))
            {
                int port;
                if (!int.TryParse(values.Port, out port) || port < 1 || port > 65535)
                {
                    SetSettingsError(dialog, "端口必须是 1 到 65535 之间的整数。");
                    return false;
                }
            }
            if (!string.IsNullOrEmpty(values.Host))
            {
                if (values.Host.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                    values.Host.IndexOf('/') >= 0 ||
                    values.Host.IndexOf(' ') >= 0 ||
                    values.Host.IndexOf('\t') >= 0)
                {
                    SetSettingsError(dialog, "监听地址只填主机名或 IP，不要带 http:// 或路径。");
                    return false;
                }
            }
            if (!string.IsNullOrEmpty(values.ExePath) && !File.Exists(values.ExePath))
            {
                SetSettingsError(dialog, "指定的 opencode 可执行文件不存在。");
                return false;
            }
            SetSettingsError(dialog, string.Empty);
            return true;
        }

        private static void SetSettingsError(Window dialog, string text)
        {
            var label = dialog.FindName("S_Error").As<TextBlock>();
            label.Text = text ?? string.Empty;
            label.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private async System.Threading.Tasks.Task ApplySettingsAsync(SettingsValues values, bool restart)
        {
            var serviceWasRunning = _serviceState == ServiceUiState.Running || ServiceRuntime.Read() != null;

            _config.AutoStart = values.AutoStart;
            _config.CloseToTray = values.CloseToTray;
            _config.SuspendOnHide = values.SuspendOnHide;
            _config.StopOnExit = values.StopOnExit;

            var desiredPath = values.ExePath ?? string.Empty;
            if (!string.Equals(desiredPath, _openCodePath, StringComparison.OrdinalIgnoreCase))
            {
                var located = _locator.Find(desiredPath);
                var resolved = located == null ? desiredPath : located.Path;
                _openCodePath = resolved ?? string.Empty;
                _openCodePathTemporary = located != null && located.IsTemporary;
                _autoStartAttempted = false;
                if (!string.IsNullOrEmpty(_openCodePath) && (!_openCodePathTemporary || !string.IsNullOrEmpty(desiredPath)))
                {
                    _config.OpenCodePath = _openCodePath;
                }
                else if (!_openCodePathTemporary)
                {
                    _config.OpenCodePath = string.Empty;
                }
                UpdateStatusBar();
                if (!string.IsNullOrEmpty(_openCodePath))
                {
                    StartVersionCheck();
                    Logger.Info("opencode 路径已更新: " + _openCodePath);
                }
                else
                {
                    Logger.Warn("未能自动找到 opencode 可执行文件");
                }
            }
            var configSaved = _config.Save();

            var ops = ServiceConfigOps.GetOps(values);
            if (ops.Count == 0)
            {
                SetAction(configSaved ? "设置已保存" : "程序设置保存失败，详情见日志");
                if (restart)
                {
                    if (serviceWasRunning)
                    {
                        SetUiState(ServiceUiState.Running);
                        await RestartServiceAsync();
                    }
                    else
                    {
                        SetUiState(ServiceUiState.Stopped);
                        await StartServiceAsync();
                    }
                }
                return;
            }

            if (string.IsNullOrEmpty(_openCodePath))
            {
                ShowViewError("未找到 opencode", "程序设置已保存，但没有 opencode 可执行文件，无法写入服务配置。请先指定 opencode.exe、opencode.cmd 或 opencode.ps1。", true, false);
                SetAction("未找到 opencode");
                return;
            }

            var commands = new List<string[]>();
            foreach (var op in ops)
            {
                if (op.Action == "set") commands.Add(new[] { "service", "set", op.Key, op.Value });
                else commands.Add(new[] { "service", "unset", op.Key });
            }

            var names = new Dictionary<string, string>();
            names["hostname"] = "监听地址";
            names["port"] = "端口";
            names["password"] = "密码";
            names["cors"] = "CORS";
            var changedNames = new List<string>();
            foreach (var op in ops)
            {
                string name;
                if (names.TryGetValue(op.Key, out name)) changedNames.Add(name);
            }
            var changed = string.Join("、", changedNames.ToArray());
            Logger.Info("准备更新服务配置: " + changed);

            _operation = "apply";
            _restartAfterApply = restart;
            SetUiState(ServiceUiState.Busy);
            ShowOverlay("正在保存设置", "正在更新 " + changed + "…", true, false, false, false, false, false);
            SetAction("正在保存服务配置…");

            var result = await _runner.RunAsync(_openCodePath, commands, 45000, values.Password);
            _operation = null;

            if (!result.Ok)
            {
                var errorText = SecretMasker.Mask(result.Output, values.Password);
                var failedName = "服务配置";
                if (result.FailedIndex >= 0 && result.FailedIndex < commands.Count)
                {
                    var failedCommand = commands[result.FailedIndex];
                    if (failedCommand.Length >= 3)
                    {
                        string name;
                        if (names.TryGetValue(failedCommand[2], out name)) failedName = name;
                    }
                }
                Logger.Error("保存服务配置失败（" + failedName + "）: " + errorText);
                SetUiState(ServiceUiState.Stopped);
                ShowViewError("保存设置失败", "更新 " + failedName + " 失败，之前的项目可能已经写入。" +
                    Environment.NewLine + Environment.NewLine + errorText, true, false);
                SetAction("保存设置失败");
                return;
            }

            Logger.Info("服务配置已保存");
            if (serviceWasRunning || _restartAfterApply)
            {
                if (ServiceRuntime.Read() != null)
                {
                    await RestartServiceAsync();
                }
                else
                {
                    SetUiState(ServiceUiState.Stopped);
                    await StartServiceAsync();
                }
            }
            else
            {
                SetAction("设置已保存");
                await UpdateServiceRuntimeAsync();
            }
        }

        private string CreateDesktopShortcut()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var linkPath = Path.Combine(desktop, "OpenCode Launcher.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("无法创建 WScript.Shell 对象。");

            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(linkPath);
            shortcut.TargetPath = Assembly.GetExecutingAssembly().Location;
            shortcut.Arguments = string.Empty;
            shortcut.WorkingDirectory = AppPaths.AppRoot;
            shortcut.IconLocation = AppPaths.IconPath;
            shortcut.Description = "OpenCode Launcher";
            shortcut.WindowStyle = 1;
            shortcut.Save();
            Logger.Info("已创建桌面快捷方式: " + linkPath);
            return linkPath;
        }
    }

    internal static class WindowLookupExtensions
    {
        public static T As<T>(this object value) where T : class
        {
            var typed = value as T;
            if (typed == null) throw new InvalidOperationException("XAML 控件类型不匹配: " + typeof(T).Name);
            return typed;
        }
    }
}
