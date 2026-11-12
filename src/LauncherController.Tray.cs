using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace OpenCodeLauncher
{
    internal sealed partial class LauncherController
    {
        private void InitializeTray()
        {
            _trayIcon = new Forms.NotifyIcon();
            _trayIcon.Icon = new Icon(AppPaths.IconPath);
            _trayIcon.Text = "OpenCode Launcher";
            _trayIcon.Visible = true;

            var menu = new Forms.ContextMenuStrip();
            menu.ShowImageMargin = false;

            var showItem = new Forms.ToolStripMenuItem("显示主窗口");
            showItem.Click += delegate { ShowMainWindow(); };
            menu.Items.Add(showItem);
            menu.Items.Add(new Forms.ToolStripSeparator());

            _trayBrowser = new Forms.ToolStripMenuItem("在浏览器打开");
            _trayBrowser.Click += async delegate { await OpenInBrowserAsync(); };
            menu.Items.Add(_trayBrowser);

            _trayStart = new Forms.ToolStripMenuItem("启动服务");
            _trayStart.Click += async delegate { await StartServiceAsync(); };
            menu.Items.Add(_trayStart);

            _trayStop = new Forms.ToolStripMenuItem("停止服务");
            _trayStop.Click += async delegate { await StopServiceAsync(); };
            menu.Items.Add(_trayStop);

            menu.Items.Add(new Forms.ToolStripSeparator());

            var settingsItem = new Forms.ToolStripMenuItem("设置");
            settingsItem.Click += delegate
            {
                ShowMainWindow();
                OpenSettings();
            };
            menu.Items.Add(settingsItem);

            var exitItem = new Forms.ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApp(); };
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += delegate { ShowMainWindow(); };
            _trayIcon.Visible = true;
        }

        private void ShowBalloon(string message)
        {
            if (_trayIcon == null || !_trayIcon.Visible) return;
            try
            {
                _trayIcon.ShowBalloonTip(1800, "OpenCode Launcher", message, Forms.ToolTipIcon.Info);
            }
            catch
            {
            }
        }

        private void DisposeTray()
        {
            if (_trayIcon == null) return;
            try
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            catch
            {
            }
            _trayIcon = null;
        }
    }
}
