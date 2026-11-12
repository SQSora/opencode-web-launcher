using System;
using System.Windows;

namespace OpenCodeLauncher
{
    internal sealed class LauncherApp : Application
    {
        public LauncherController Controller { get; private set; }

        public LauncherApp()
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            SessionEnding += OnSessionEnding;

            AppPaths.EnsureDirectories();
            AppPaths.RotateLog();

            Resources = XamlLoader.LoadDictionary(AppPaths.StylesXaml);
            Controller = new LauncherController(this);
            MainWindow = Controller.Window;
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                Logger.Error("未处理异常: " + e.Exception);
                if (Controller != null) Controller.ReportInternalError();
            }
            catch
            {
            }
            e.Handled = true;
        }

        private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            if (Controller != null) Controller.Exiting = true;
        }
    }
}
