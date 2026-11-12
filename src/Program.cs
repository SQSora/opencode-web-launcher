using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace OpenCodeLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            Native.TryEnablePerMonitorDpi();
            Native.AddLibraryDirectory(AppPaths.LibDir);

            var exit = false;
            var selfTest = false;
            foreach (var arg in args)
            {
                if (string.Equals(arg, "-Exit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--exit", StringComparison.OrdinalIgnoreCase))
                {
                    exit = true;
                }
                if (string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    selfTest = true;
                }
            }

            if (exit)
            {
                Native.PostExit();
                return;
            }

            if (selfTest)
            {
                Environment.Exit(SelfTest.Run());
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\OpenCodeLauncher_7F3A9C21", out createdNew))
            {
                if (!createdNew)
                {
                    Native.PostShowWindow();
                    return;
                }

                try
                {
                    var app = new LauncherApp();
                    app.Run(app.MainWindow);
                }
                catch (Exception ex)
                {
                    try
                    {
                        AppPaths.EnsureDirectories();
                        Logger.Error("启动失败: " + ex);
                    }
                    catch
                    {
                    }
                    MessageBox.Show(ex.Message, "OpenCode Launcher 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                var requested = new AssemblyName(args.Name);
                if (string.IsNullOrEmpty(requested.Name)) return null;
                var candidate = Path.Combine(AppPaths.LibDir, requested.Name + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

