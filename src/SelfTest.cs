using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace OpenCodeLauncher
{
    internal static class SelfTest
    {
        public static int Run()
        {
            var failures = new List<string>();

            SecretMasker.CurrentPassword = "oldsecret";
            var masked = SecretMasker.Mask("oldsecret \"password\": \"newsecret\" /auth/connect/abcdef123");
            Check(failures, !masked.Contains("oldsecret"), "secret masker: current password");
            Check(failures, !masked.Contains("newsecret"), "secret masker: json password");
            Check(failures, !masked.Contains("abcdef123"), "secret masker: pair token");

            Check(failures, CommandLine.Quote("a b") == "\"a b\"", "command line: spaces");
            Check(failures, CommandLine.Quote("a&b") == "a&b", "command line: safe char");

            var local = AccessUrlBuilder.ConvertToLocal("http://0.0.0.0:28894/");
            Check(failures, local.StartsWith("http://127.0.0.1:28894", StringComparison.OrdinalIgnoreCase), "local url: ipv4 wildcard");
            var local6 = AccessUrlBuilder.ConvertToLocal("http://[::]:28894/");
            Check(failures, local6.StartsWith("http://127.0.0.1:28894", StringComparison.OrdinalIgnoreCase), "local url: ipv6 wildcard");
            var urls = AccessUrlBuilder.GetAccessUrls("http://0.0.0.0:28894", string.Empty);
            Check(failures, urls.Contains("http://127.0.0.1:28894"), "access urls: loopback");

            var pairCode = AccessUrlBuilder.GetPairCode("  http://127.0.0.1:28894/auth/connect/AbC-123  ");
            Check(failures, string.Equals(pairCode, "AbC-123", StringComparison.Ordinal), "pair: parse code");
            Check(failures, string.Equals(
                AccessUrlBuilder.BuildPairUrl("http://192.168.1.101:28894", "AbC-123"),
                "http://192.168.1.101:28894/auth/connect/AbC-123",
                StringComparison.Ordinal), "pair: build ipv4 link");
            Check(failures, string.Equals(
                AccessUrlBuilder.BuildPairUrl("http://[::1]:28894", "AbC-123"),
                "http://[::1]:28894/auth/connect/AbC-123",
                StringComparison.Ordinal), "pair: build ipv6 link");

            RunProcessTests(failures);
            RunConfigTests(failures);

            try
            {
                var app = new Application();
                app.Resources = XamlLoader.LoadDictionary(AppPaths.StylesXaml);
                var main = XamlLoader.LoadWindow(AppPaths.MainWindowXaml);
                var settings = XamlLoader.LoadWindow(AppPaths.SettingsWindowXaml);

                var mainNames = new[]
                {
                    "StatusText", "UrlText", "BtnStart", "BtnRestart", "BtnStop", "BtnWebToggle",
                    "BtnBrowser", "BtnAccess", "BtnSettings", "BtnTray", "WebHost", "Overlay",
                    "BtnOverlayStart", "BtnOverlayShow", "BtnOverlayLogin", "BtnOverlayRetry",
                    "AccessPopup", "AccessCard", "AccessUrlList", "AccessHint", "AccessCodeMode",
                    "AccessCodeLabel", "AccessPairStatus", "AccessRefreshPair",
                    "OverlayCodeMode", "OverlayCodeLabel", "OverlayPairStatus", "OverlayRefreshPair", "LogPanel",
                    "LogBox", "ExeText", "ActionText"
                };
                foreach (var name in mainNames)
                {
                    Check(failures, main.FindName(name) != null, "xaml main: " + name);
                }

                var settingsNames = new[]
                {
                    "S_Host", "S_Port", "S_Password", "S_PasswordPlain", "S_Cors", "S_Exe",
                    "S_AutoStart", "S_CloseTray", "S_SuspendHide", "S_StopExit", "S_ConfigPath",
                    "S_PasswordSource", "S_TogglePassword", "S_Browse", "S_DesktopShortcut",
                    "S_Cancel", "S_Save", "S_SaveRestart", "S_Error"
                };
                foreach (var name in settingsNames)
                {
                    Check(failures, settings.FindName(name) != null, "xaml settings: " + name);
                }
            }
            catch (Exception ex)
            {
                failures.Add("xaml load: " + ex.Message);
            }

            if (failures.Count == 0)
            {
                Console.WriteLine("SELF-TEST PASS");
                return 0;
            }

            Console.WriteLine("SELF-TEST FAIL");
            foreach (var failure in failures) Console.WriteLine("  " + failure);
            return 1;
        }

        private static void RunProcessTests(List<string> failures)
        {
            var directory = Path.Combine(Path.GetTempPath(), "oc-launcher-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                var runner = new ProcessRunner();

                var ps1 = Path.Combine(directory, "echo args.ps1");
                File.WriteAllText(
                    ps1,
                    "param([string]$A, [string]$B)" + Environment.NewLine +
                    "Write-Output ($A + '|' + $B)" + Environment.NewLine +
                    "exit 0" + Environment.NewLine,
                    new UTF8Encoding(false));
                var commands = new List<string[]>();
                commands.Add(new[] { "a&b", "c|d" });
                var psTask = runner.RunAsync(ps1, commands, 10000);
                if (!psTask.Wait(15000))
                {
                    failures.Add("process runner: ps1 timeout");
                }
                else
                {
                    var result = psTask.Result;
                    Check(failures, result.Ok && result.Output.Contains("a&b|c|d"), "process runner: ps1 args");
                }

                var cmd = Path.Combine(directory, "echo args.cmd");
                File.WriteAllText(cmd, "@echo off" + Environment.NewLine + "echo ARG=[%~1]" + Environment.NewLine, Encoding.ASCII);
                var cmdCommands = new List<string[]>();
                cmdCommands.Add(new[] { "hello world" });
                var cmdTask = runner.RunAsync(cmd, cmdCommands, 10000);
                if (!cmdTask.Wait(15000))
                {
                    failures.Add("process runner: cmd timeout");
                }
                else
                {
                    var result = cmdTask.Result;
                    Check(failures, result.Ok && result.Output.Contains("ARG=[hello world]"), "process runner: cmd args");
                }

                var unsafeCommands = new List<string[]>();
                unsafeCommands.Add(new[] { "a&b" });
                var unsafeTask = runner.RunAsync(cmd, unsafeCommands, 10000);
                if (!unsafeTask.Wait(15000))
                {
                    failures.Add("process runner: cmd unsafe timeout");
                }
                else
                {
                    Check(failures, !unsafeTask.Result.Ok, "process runner: cmd unsafe guard");
                }
            }
            catch (Exception ex)
            {
                failures.Add("process runner: " + ex.Message);
            }
            finally
            {
                try
                {
                    foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                    Directory.Delete(directory, false);
                }
                catch
                {
                }
            }
        }

        private static void RunConfigTests(List<string> failures)
        {
            var directory = Path.Combine(Path.GetTempPath(), "oc-launcher-config-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "config.json");
                File.WriteAllText(path, "{ this is not json", new UTF8Encoding(false));

                var fallback = LauncherConfig.LoadFrom(path);
                Check(failures, fallback.AutoStart && fallback.CloseToTray && fallback.SuspendOnHide && fallback.AccessCodeMode, "config: malformed falls back to defaults");
                Check(failures, File.Exists(path + ".bad"), "config: malformed file backup");

                fallback.OpenCodePath = @"C:\test\opencode.exe";
                fallback.Window.Width = 1200;
                fallback.Window.Height = 700;
                Check(failures, fallback.SaveTo(path), "config: save");
                Check(failures, !File.Exists(path + ".tmp"), "config: temp file cleanup");
                Check(failures, !fallback.SaveTo(directory), "config: save failure is contained");

                var reloaded = LauncherConfig.LoadFrom(path);
                Check(failures, string.Equals(reloaded.OpenCodePath, @"C:\test\opencode.exe", StringComparison.Ordinal), "config: round trip path");
                Check(failures, reloaded.Window.Width == 1200 && reloaded.Window.Height == 700, "config: round trip window");
                Check(failures, reloaded.AccessCodeMode, "config: access code mode default");
            }
            catch (Exception ex)
            {
                failures.Add("config: " + ex.Message);
            }
            finally
            {
                try
                {
                    foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                    Directory.Delete(directory, false);
                }
                catch
                {
                }
            }
        }

        private static void Check(List<string> failures, bool condition, string name)
        {
            if (!condition) failures.Add(name);
        }
    }
}
