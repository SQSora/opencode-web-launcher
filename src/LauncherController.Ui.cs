using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace OpenCodeLauncher
{
    internal sealed partial class LauncherController
    {
        private void WireButtons()
        {
            _btnStart.Click += async delegate { await StartServiceAsync(); };
            _btnRestart.Click += async delegate { await RestartServiceAsync(); };
            _btnStop.Click += async delegate { await StopServiceAsync(); };
            _btnWebToggle.Click += delegate { ToggleWebPage(); };
            _btnBrowser.Click += async delegate { await OpenInBrowserAsync(); };
            _btnAccess.Click += delegate { ShowAccessPopup(); };
            _btnSettings.Click += delegate { OpenSettings(); };
            _btnTray.Click += delegate { HideToTray(); };

            _btnOverlayStart.Click += async delegate { await StartServiceAsync(); };
            _btnOverlayShow.Click += delegate { ShowWebPage(); };
            _btnOverlayLogin.Click += delegate { ShowManualLoginPage(); };
            _btnOverlayRetry.Click += async delegate { await RetryConnectionAsync(); };

            _accessClose.Click += delegate { HideAccessPopup(); };
            _accessCard.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    HideAccessPopup();
                    e.Handled = true;
                }
            };

            _urlText.MouseLeftButtonUp += delegate
            {
                if (string.IsNullOrEmpty(_serviceUrl)) return;
                try
                {
                    Clipboard.SetText(_serviceUrl);
                    SetAction("服务地址已复制到剪贴板");
                }
                catch
                {
                    SetAction("复制失败");
                }
            };

            _btnLog.Click += delegate
            {
                _logOpen = !_logOpen;
                _logPanel.Height = _logOpen ? 190 : 0;
            };
            _btnCloseLog.Click += delegate
            {
                _logOpen = false;
                _logPanel.Height = 0;
            };
            _btnClearLog.Click += delegate { _logBox.Clear(); };
            _btnCopyLog.Click += delegate
            {
                try
                {
                    Clipboard.SetText(_logBox.Text);
                    SetAction("日志已复制");
                }
                catch
                {
                }
            };
            _btnOpenData.Click += delegate
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "opencode");
                if (System.IO.Directory.Exists(path))
                {
                    try
                    {
                        Process.Start("explorer.exe", path);
                    }
                    catch
                    {
                    }
                }
            };
        }

        private void WireAccessMode()
        {
            _accessOpenModeToggle.Checked += delegate { SetAccessOpenMode(true); };
            _accessOpenModeToggle.Unchecked += delegate { SetAccessOpenMode(false); };
            _overlayOpenModeToggle.Checked += delegate { SetAccessOpenMode(true); };
            _overlayOpenModeToggle.Unchecked += delegate { SetAccessOpenMode(false); };
            SyncAccessModeUi();
        }

        private void ShowOverlay(string title, string message, bool spinner, bool showStart, bool showRetry, bool showWeb, bool showLogin, bool showAddress)
        {
            _overlayTitle.Text = title;
            _overlayMessage.Text = message;
            _spinnerBox.Visibility = spinner ? Visibility.Visible : Visibility.Collapsed;
            _btnOverlayStart.Visibility = showStart ? Visibility.Visible : Visibility.Collapsed;
            _btnOverlayRetry.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
            _btnOverlayShow.Visibility = showWeb ? Visibility.Visible : Visibility.Collapsed;
            _btnOverlayLogin.Visibility = showLogin ? Visibility.Visible : Visibility.Collapsed;
            _overlayActions.Visibility = (showStart || showRetry || showWeb || showLogin)
                ? Visibility.Visible
                : Visibility.Collapsed;
            _overlay.Visibility = Visibility.Visible;
            _overlayAddressModeRow.Visibility = showAddress ? Visibility.Visible : Visibility.Collapsed;
            _overlayAddressScroll.Visibility = showAddress ? Visibility.Visible : Visibility.Collapsed;
            _overlayAddressHint.Visibility = Visibility.Collapsed;
            if (!showAddress) _overlayAddressList.Children.Clear();
            if (_webView != null) _webView.Visibility = Visibility.Collapsed;
            if (spinner) StartSpinner(); else StopSpinner();
        }

        private void HideOverlay()
        {
            StopSpinner();
            if (_webHidden)
            {
                ShowHiddenPlaceholder();
                return;
            }
            _overlay.Visibility = Visibility.Collapsed;
            if (_webView != null) _webView.Visibility = Visibility.Visible;
        }

        private void ShowViewError(string title, string message, bool showStart, bool showLogin)
        {
            if (string.IsNullOrEmpty(message)) message = "请点击“重试”，或查看运行日志了解详情。";

            _connectedUrl = string.Empty;
            _connecting = false;
            _connectDeadline = null;
            _pendingUrl = string.Empty;
            _manualLogin = false;

            var runtime = ServiceRuntime.Read();
            if (runtime != null)
            {
                var url = AccessUrlBuilder.ConvertToLocal(runtime.Url);
                _serviceUrl = url;
                _serviceRawUrl = runtime.Url;
                _servicePid = runtime.Pid;
                _urlText.Text = url;
                _urlText.ToolTip = url;
                if (string.IsNullOrEmpty(_operation) && _serviceState != ServiceUiState.Running)
                {
                    SetUiState(ServiceUiState.Running);
                }
            }
            else if (string.IsNullOrEmpty(_operation) && _serviceState != ServiceUiState.Busy)
            {
                _serviceUrl = string.Empty;
                _serviceRawUrl = string.Empty;
                _servicePid = 0;
                _connectedUrl = string.Empty;
                _urlText.Text = "—";
                _urlText.ToolTip = string.Empty;
                SetUiState(ServiceUiState.Stopped);
            }

            var showStartButton = showStart && runtime == null;
            ShowOverlay(title, message, false, showStartButton, true, false, showLogin && runtime != null, false);
        }

        private void ShowHiddenPlaceholder()
        {
            var serviceAlive = IsServiceAlive();
            var message = "后台服务仍在运行。点击地址复制，Ctrl+点击在浏览器打开；或点“显示页面”恢复界面。";
            if (_accessOpenMode)
            {
                message = "后台服务仍在运行。点击地址打开一次性登录链接；或点“显示页面”恢复界面。";
            }
            if (!string.IsNullOrEmpty(_operation))
            {
                message = "服务操作正在执行，请稍候；完成后会自动更新页面状态。";
            }
            else if (!serviceAlive)
            {
                message = "后台服务当前未运行，可先启动服务。";
            }

            ShowOverlay("Web 页面已隐藏", message, false, false, false, true, false, true);
            SyncAccessModeUi();
            if (serviceAlive)
            {
                var urls = AccessUrlBuilder.GetAccessUrls(_serviceRawUrl, _serviceUrl);
                SetAccessRows(_overlayAddressList, urls);
                _overlayAddressHint.Visibility = Visibility.Visible;
                UpdateAccessModeHint();
            }
            else
            {
                _overlayAddressModeRow.Visibility = Visibility.Collapsed;
                _overlayAddressScroll.Visibility = Visibility.Collapsed;
                _overlayAddressHint.Visibility = Visibility.Collapsed;
            }
        }

        private void SyncAccessModeUi()
        {
            _syncAccessMode = true;
            try
            {
                _accessOpenModeToggle.IsChecked = _accessOpenMode;
                _overlayOpenModeToggle.IsChecked = _accessOpenMode;
                var label = _accessOpenMode ? "点击地址打开一次性登录链接" : "点击地址复制";
                _accessModeLabel.Text = label;
                _overlayAccessModeLabel.Text = label;
            }
            finally
            {
                _syncAccessMode = false;
            }
        }

        private void SetAccessOpenMode(bool enabled)
        {
            if (_syncAccessMode) return;
            _accessOpenMode = enabled;
            _config.AccessOpenMode = enabled;
            SyncAccessModeUi();
            UpdateAccessModeHint();
            _config.Save();
        }

        private void UpdateAccessModeHint()
        {
            if (_accessHint != null)
            {
                if (_accessOpenMode)
                {
                    _accessHint.Text = "点击地址打开一次性登录链接；Ctrl+点击复制。\n一次性链接 5 分钟内有效。";
                }
                else
                {
                    _accessHint.Text = "点击地址复制；Ctrl+点击在浏览器打开。\n直接访问需要登录：用户名 opencode，密码见 service.json 的 password。";
                }
            }

            if (_overlayAddressHint != null && _overlayAddressHint.Visibility == Visibility.Visible)
            {
                _overlayAddressHint.Text = _accessOpenMode
                    ? "点击地址打开一次性登录链接；Ctrl+点击复制。"
                    : "点击地址复制；Ctrl+点击在浏览器打开。用户名 opencode，密码见 service.json。";
            }

            if (_webHidden && IsServiceAlive() && _overlayMessage != null)
            {
                _overlayMessage.Text = _accessOpenMode
                    ? "后台服务仍在运行。点击地址打开一次性登录链接；或点“显示页面”恢复界面。"
                    : "后台服务仍在运行。点击地址复制，Ctrl+点击在浏览器打开；或点“显示页面”恢复界面。";
            }
        }

        private void SetAccessPopupContent()
        {
            _accessUrlList.Children.Clear();
            var urls = AccessUrlBuilder.GetAccessUrls(_serviceRawUrl, _serviceUrl);
            if (urls.Count == 0)
            {
                var empty = new TextBlock();
                empty.Text = "服务未运行，暂无访问地址";
                empty.Foreground = BrushFromHex("#8B95A6");
                empty.FontSize = 12;
                empty.Margin = new Thickness(2);
                _accessUrlList.Children.Add(empty);
                _accessHint.Text = "启动后台服务后即可获取访问地址。";
                return;
            }

            SetAccessRows(_accessUrlList, urls);
            UpdateAccessModeHint();
        }

        private void ShowAccessPopup()
        {
            SyncAccessModeUi();
            SetAccessPopupContent();
            _accessPopup.PlacementTarget = _btnAccess;
            _accessPopup.IsOpen = true;
            _accessCard.Focus();
        }

        private void HideAccessPopup()
        {
            if (_accessPopup != null) _accessPopup.IsOpen = false;
        }

        private void SetAccessRows(Panel target, IList<string> urls)
        {
            if (target == null) return;
            target.Children.Clear();
            foreach (var url in urls)
            {
                if (string.IsNullOrEmpty(url)) continue;
                target.Children.Add(NewAccessRow(url));
            }
        }

        private Border NewAccessRow(string url)
        {
            var row = new Border();
            row.Background = BrushFromHex("#0F141B");
            row.CornerRadius = new CornerRadius(7);
            row.Padding = new Thickness(10, 7, 10, 7);
            row.Margin = new Thickness(0, 0, 0, 6);
            row.Cursor = Cursors.Hand;
            row.Tag = url;

            var text = new TextBlock();
            text.Text = url;
            text.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            text.FontSize = 12;
            text.Foreground = BrushFromHex("#DCE3EE");
            text.Cursor = Cursors.Hand;
            row.Child = text;

            row.MouseEnter += delegate { row.Background = BrushFromHex("#1B222C"); };
            row.MouseLeave += delegate { row.Background = BrushFromHex("#0F141B"); };
            row.MouseLeftButtonUp += delegate
            {
                var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                InvokeAccessUrlAction(url, ctrl);
            };
            return row;
        }

        private void InvokeAccessUrlAction(string url, bool withCtrl)
        {
            if (_accessOpenMode)
            {
                if (withCtrl) CopyAccessUrl(url);
                else OpenPairLinkAsync(url);
            }
            else
            {
                if (withCtrl)
                {
                    if (_runner.OpenExternalUrl(url)) SetAction("已在浏览器打开 " + url);
                    else SetAction("打开浏览器失败");
                }
                else
                {
                    CopyAccessUrl(url);
                }
            }
        }

        private void CopyAccessUrl(string url)
        {
            try
            {
                Clipboard.SetText(url);
                SetAction("已复制 " + url);
            }
            catch
            {
                SetAction("复制失败");
            }
        }

        private async void OpenPairLinkAsync(string url)
        {
            SetAction("正在生成一次性链接: " + url);
            var commands = new List<string[]>();
            commands.Add(new[] { "pair", "--url", url });
            var result = await _runner.RunAsync(_openCodePath, commands, 30000);
            var pairUrl = GetPairUrl(result.Output);
            if (!string.IsNullOrEmpty(pairUrl) && _runner.OpenExternalUrl(pairUrl))
            {
                SetAction("已在浏览器打开 " + url);
            }
            else
            {
                Logger.Warn("生成一次性链接失败，改用普通地址: " + result.Output);
                if (_runner.OpenExternalUrl(url)) SetAction("已打开登录页，请手动输入密码");
                else SetAction("打开浏览器失败");
            }
        }

        private static string GetPairUrl(string output)
        {
            if (string.IsNullOrEmpty(output)) return string.Empty;
            var match = Regex.Match(output, "https?://[^\\s\"']*/auth/connect/[^\\s\"']+", RegexOptions.IgnoreCase);
            return match.Success ? match.Value.TrimEnd('.', ',', ')', ']', ';') : string.Empty;
        }

        private void StartSpinner()
        {
            if (_spinnerStoryboard != null) return;
            try
            {
                var animation = new DoubleAnimation();
                animation.From = 0;
                animation.To = 360;
                animation.Duration = new Duration(TimeSpan.FromSeconds(1.1));
                animation.RepeatBehavior = RepeatBehavior.Forever;
                var storyboard = new Storyboard();
                Storyboard.SetTarget(animation, _spinnerRotate);
                Storyboard.SetTargetProperty(animation, new PropertyPath("Angle"));
                storyboard.Children.Add(animation);
                storyboard.Begin();
                _spinnerStoryboard = storyboard;
            }
            catch
            {
            }
        }

        private void StopSpinner()
        {
            if (_spinnerStoryboard == null) return;
            try
            {
                _spinnerStoryboard.Stop();
            }
            catch
            {
            }
            _spinnerStoryboard = null;
        }
    }
}
