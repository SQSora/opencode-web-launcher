using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OpenCodeLauncher
{
    internal sealed class CliResult
    {
        public bool Ok;
        public int ExitCode;
        public string Output = string.Empty;
        public int FailedIndex = -1;
        public bool TimedOut;
    }

    internal sealed class ProcessRunner
    {
        public async Task<CliResult> RunAsync(string executable, IList<string[]> commands, int timeoutMs, params string[] extraSecrets)
        {
            var result = new CliResult();
            result.Ok = true;
            var allOutput = new StringBuilder();

            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                Logger.Info("执行: " + FormatCommand(command, extraSecrets));
                var single = await RunSingleAsync(executable, command, timeoutMs, extraSecrets);
                if (allOutput.Length > 0 && single.Output.Length > 0) allOutput.AppendLine();
                allOutput.Append(single.Output);

                if (!single.Ok)
                {
                    result.Ok = false;
                    result.ExitCode = single.ExitCode;
                    result.FailedIndex = index;
                    result.TimedOut = single.TimedOut;
                    break;
                }
            }

            result.Output = allOutput.ToString();
            return result;
        }

        public async Task<CliResult> RunSingleAsync(string executable, string[] arguments, int timeoutMs, params string[] extraSecrets)
        {
            var result = new CliResult();
            Process process = null;
            var output = new StringBuilder();
            var gate = new object();

            try
            {
                var info = CreateStartInfo(executable, arguments, true);
                process = new Process();
                process.StartInfo = info;
                DataReceivedEventHandler handler = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (gate) output.AppendLine(e.Data);
                };
                process.OutputDataReceived += handler;
                process.ErrorDataReceived += handler;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var timedOut = false;
                var exitCode = await Task.Run(delegate
                {
                    if (process.WaitForExit(timeoutMs))
                    {
                        process.WaitForExit();
                        return process.ExitCode;
                    }

                    timedOut = true;
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }
                    try
                    {
                        process.WaitForExit(5000);
                    }
                    catch
                    {
                    }
                    return -1;
                });

                string text;
                lock (gate) text = output.ToString();
                result.Ok = !timedOut && exitCode == 0;
                result.ExitCode = exitCode;
                result.TimedOut = timedOut;
                result.Output = text;
                if (timedOut) result.Output = result.Output + Environment.NewLine + "命令在 " + (timeoutMs / 1000) + " 秒内没有返回，已尝试终止。";
                return result;
            }
            catch (Exception ex)
            {
                lock (gate) result.Output = output.ToString();
                result.Ok = false;
                result.ExitCode = -1;
                result.Output = (result.Output.Length > 0 ? result.Output + Environment.NewLine : string.Empty) + ex.Message;
                return result;
            }
            finally
            {
                if (process != null)
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public bool RunDetached(string executable, string[] arguments, params string[] extraSecrets)
        {
            try
            {
                Logger.Info("执行: " + FormatCommand(arguments, extraSecrets) + "（不等待退出）");
                var info = CreateStartInfo(executable, arguments, false);
                var process = Process.Start(info);
                if (process != null) process.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("启动命令失败: " + ex.Message);
                return false;
            }
        }

        public bool OpenExternalUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn("拒绝打开非 http(s) 地址: " + SecretMasker.Mask(url));
                return false;
            }

            try
            {
                var info = new ProcessStartInfo();
                info.FileName = url;
                info.UseShellExecute = true;
                Process.Start(info);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn("打开链接失败: " + ex.Message);
                return false;
            }
        }

        private static ProcessStartInfo CreateStartInfo(string executable, string[] arguments, bool redirect)
        {
            var extension = Path.GetExtension(executable);
            var info = new ProcessStartInfo();
            info.WorkingDirectory = AppPaths.WorkDir;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;

            if (redirect)
            {
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;
            }

            if (string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
            {
                info.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
                info.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " +
                                 CommandLine.Quote(executable) + " " +
                                 CommandLine.Join(arguments);
                return info;
            }

            if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var argument in arguments)
                {
                    if (CommandLine.IsCmdUnsafe(argument))
                    {
                        throw new InvalidOperationException(
                            "当前 opencode 入口是批处理 shim（.cmd），无法安全传递包含 & | < > ^ % 等字符的参数。请在设置中选择 opencode.exe 或 opencode.ps1。");
                    }
                }
                info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                info.Arguments = "/d /s /c " + CommandLine.BuildCmdCommand(executable, arguments);
                return info;
            }

            info.FileName = executable;
            info.Arguments = CommandLine.Join(arguments);
            return info;
        }

        private static string FormatCommand(IEnumerable<string> arguments, string[] extraSecrets)
        {
            var values = new List<string>();
            var previous = string.Empty;
            foreach (var argument in arguments)
            {
                if (string.Equals(previous, "password", StringComparison.OrdinalIgnoreCase))
                {
                    values.Add("***");
                }
                else
                {
                    values.Add(argument);
                }
                previous = argument;
            }
            return SecretMasker.Mask("opencode " + string.Join(" ", values.ToArray()), extraSecrets);
        }
    }
}
