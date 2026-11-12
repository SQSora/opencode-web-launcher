using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OpenCodeLauncher
{
    internal sealed class WindowConfig
    {
        public double Width = 1360;
        public double Height = 900;
        public double? Left;
        public double? Top;
        public bool Maximized;
    }

    internal sealed class LauncherConfig
    {
        public string OpenCodePath = string.Empty;
        public bool AutoStart = true;
        public bool CloseToTray = true;
        public bool StopOnExit;
        public bool SuspendOnHide = true;
        public bool AccessOpenMode;
        public WindowConfig Window = new WindowConfig();

        public static LauncherConfig Load()
        {
            return LoadFrom(AppPaths.ConfigFile);
        }

        public static LauncherConfig LoadFrom(string path)
        {
            var config = new LauncherConfig();
            Dictionary<string, object> data;
            try
            {
                data = Json.ReadObject(path);
            }
            catch (Exception ex)
            {
                Logger.Warn("读取 Launcher 配置失败，使用默认值: " + ex.Message);
                TryBackupConfig(path);
                data = new Dictionary<string, object>();
            }

            try
            {
                config.OpenCodePath = Json.GetString(data, "opencodePath", string.Empty);
                config.AutoStart = Json.GetBool(data, "autoStart", true);
                config.CloseToTray = Json.GetBool(data, "closeToTray", true);
                config.StopOnExit = Json.GetBool(data, "stopOnExit", false);
                config.SuspendOnHide = Json.GetBool(data, "suspendOnHide", true);
                config.AccessOpenMode = Json.GetBool(data, "accessOpenMode", false);

                var window = Json.GetObject(data, "window");
                if (window != null)
                {
                    var width = Json.GetDouble(window, "width", 1360);
                    var height = Json.GetDouble(window, "height", 900);
                    config.Window.Width = width >= 1000 ? width : 1360;
                    config.Window.Height = height >= 600 ? height : 900;

                    var leftValue = Json.Get(window, "left");
                    var topValue = Json.Get(window, "top");
                    double left;
                    double top;
                    if (leftValue != null && double.TryParse(Convert.ToString(leftValue, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out left))
                    {
                        config.Window.Left = left;
                    }
                    if (topValue != null && double.TryParse(Convert.ToString(topValue, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out top))
                    {
                        config.Window.Top = top;
                    }
                    config.Window.Maximized = Json.GetBool(window, "maximized", false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("解析 Launcher 配置失败，使用默认值: " + ex.Message);
                TryBackupConfig(path);
                return new LauncherConfig();
            }

            return config;
        }

        private static void TryBackupConfig(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                File.Copy(path, path + ".bad", true);
            }
            catch
            {
            }
        }

        public bool Save()
        {
            return SaveTo(AppPaths.ConfigFile);
        }

        public bool SaveTo(string path)
        {
            var window = new Dictionary<string, object>();
            window["width"] = Window.Width;
            window["height"] = Window.Height;
            window["left"] = Window.Left.HasValue ? (object)Window.Left.Value : null;
            window["top"] = Window.Top.HasValue ? (object)Window.Top.Value : null;
            window["maximized"] = Window.Maximized;

            var data = new Dictionary<string, object>();
            data["opencodePath"] = OpenCodePath;
            data["autoStart"] = AutoStart;
            data["closeToTray"] = CloseToTray;
            data["stopOnExit"] = StopOnExit;
            data["suspendOnHide"] = SuspendOnHide;
            data["accessOpenMode"] = AccessOpenMode;
            data["window"] = window;
            try
            {
                Json.WriteObject(path, data);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("保存 Launcher 配置失败: " + ex.Message);
                return false;
            }
        }
    }

    internal sealed class SettingsValues
    {
        public string Host = string.Empty;
        public string Port = string.Empty;
        public string Password = string.Empty;
        public string Cors = string.Empty;
        public string ExePath = string.Empty;
        public bool AutoStart;
        public bool CloseToTray;
        public bool SuspendOnHide;
        public bool StopOnExit;
    }

    internal sealed class ServiceOp
    {
        public string Key;
        public string Action;
        public string Value;
    }

    internal static class ServiceConfigOps
    {
        public static Dictionary<string, object> Read()
        {
            var data = Json.ReadObject(AppPaths.ServiceConfigFile);
            SecretMasker.CurrentPassword = Json.GetString(data, "password", string.Empty);
            return data;
        }

        public static bool IsDisabled()
        {
            var data = Read();
            var value = Json.Get(data, "disabled");
            if (value is bool) return (bool)value;
            return string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), "true", StringComparison.OrdinalIgnoreCase);
        }

        public static List<ServiceOp> GetOps(SettingsValues values)
        {
            var current = Read();
            var ops = new List<ServiceOp>();
            var desired = new Dictionary<string, string>();
            desired["hostname"] = values.Host;
            desired["port"] = values.Port;
            desired["password"] = values.Password;
            desired["cors"] = values.Cors;

            foreach (var key in new[] { "hostname", "port", "password", "cors" })
            {
                var oldValue = Json.Get(current, key);
                if (key == "cors") oldValue = FormatCors(oldValue);

                string oldText;
                string newText;
                if (key == "password")
                {
                    oldText = oldValue == null ? string.Empty : Convert.ToString(oldValue, CultureInfo.InvariantCulture);
                    newText = desired[key] ?? string.Empty;
                }
                else
                {
                    oldText = oldValue == null ? string.Empty : Convert.ToString(oldValue, CultureInfo.InvariantCulture).Trim();
                    newText = (desired[key] ?? string.Empty).Trim();
                }

                if (string.Equals(newText, oldText, StringComparison.Ordinal)) continue;
                var op = new ServiceOp();
                op.Key = key;
                if (newText.Length == 0)
                {
                    if (oldText.Length == 0) continue;
                    op.Action = "unset";
                    op.Value = string.Empty;
                }
                else
                {
                    op.Action = "set";
                    op.Value = newText;
                }
                ops.Add(op);
            }
            return ops;
        }

        public static string FormatCors(object value)
        {
            if (value == null) return string.Empty;
            var array = value as object[];
            if (array != null)
            {
                var parts = new List<string>();
                foreach (var item in array) parts.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
                return string.Join(",", parts.ToArray());
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }
    }
}
