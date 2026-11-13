using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (_runner.OpenExternalUrl(_serviceUrl)) SetAction("已在浏览器打开 " + _serviceUrl);
                    else SetAction("打开浏览器失败");
                    return;
                }
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
            _accessCodeModeToggle.Checked += delegate { SetAccessCodeMode(true); };
            _accessCodeModeToggle.Unchecked += delegate { SetAccessCodeMode(false); };
            _overlayCodeModeToggle.Checked += delegate { SetAccessCodeMode(true); };
            _overlayCodeModeToggle.Unchecked += delegate { SetAccessCodeMode(false); };
            _accessRefreshPair.Click += delegate { EnsurePairCodeAsync(true); };
            _overlayRefreshPair.Click += delegate { EnsurePairCodeAsync(true); };
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
                _urlText.ToolTip = AccessHintText;
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
            var message = "后台服务仍在运行。";
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
                SetAccessRows(_overlayAddressList, GetDisplayAccessUrls());
                _overlayAddressHint.Visibility = Visibility.Visible;
                UpdateAccessModeHint();
                EnsurePairCodeAsync(false);
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
                _accessCodeModeToggle.IsChecked = _accessCodeMode;
                _overlayCodeModeToggle.IsChecked = _accessCodeMode;
                _accessCodeLabel.Text = "使用一次性链接";
                _overlayCodeLabel.Text = "使用一次性链接";
                var visibility = _accessCodeMode ? Visibility.Visible : Visibility.Collapsed;
                _accessPairStatus.Visibility = visibility;
                _overlayPairStatus.Visibility = visibility;
                _accessRefreshPair.Visibility = visibility;
                _overlayRefreshPair.Visibility = visibility;
            }
            finally
            {
                _syncAccessMode = false;
            }
            UpdatePairStatus();
        }

        private void SetAccessCodeMode(bool enabled)
        {
            if (_syncAccessMode) return;
            _accessCodeMode = enabled;
            _config.AccessCodeMode = enabled;
            SyncAccessModeUi();
            UpdateAccessModeHint();
            _config.Save();
            if (enabled)
            {
                EnsurePairCodeAsync(false);
                UpdateAccessSurfaces();
            }
            else
            {
                ClearPairCode();
                UpdateAccessSurfaces();
            }
        }

        private void UpdateAccessModeHint()
        {
            if (_accessHint != null) _accessHint.Text = AccessHintText;
            if (_overlayAddressHint != null && _overlayAddressHint.Visibility == Visibility.Visible)
            {
                _overlayAddressHint.Text = AccessHintText;
            }
        }

        private void UpdatePairStatus()
        {
            if (_accessPairStatus == null || _overlayPairStatus == null) return;
            var text = string.Empty;
            if (_accessCodeMode)
            {
                if (_pairCodeBusy)
                {
                    text = "生成中…";
                }
                else if (!IsServiceAlive())
                {
                    text = "服务未运行";
                }
                else if (_pairCodeFailed)
                {
                    text = string.IsNullOrEmpty(_pairCode) ? "生成失败" : "刷新失败";
                }
                else if (_pairCodeUsed)
                {
                    text = "已使用";
                }
                else if (string.IsNullOrEmpty(_pairCode) || !_pairCodeIssuedAt.HasValue)
                {
                    text = "未生成";
                }
                else
                {
                    var remaining = PairCodeLifetime - (DateTime.UtcNow - _pairCodeIssuedAt.Value);
                    if (remaining <= TimeSpan.Zero)
                    {
                        text = "已过期";
                    }
                    else
                    {
                        text = "剩余 " + ((int)remaining.TotalMinutes).ToString(CultureInfo.InvariantCulture) +
                               ":" + remaining.Seconds.ToString("00", CultureInfo.InvariantCulture);
                    }
                }
            }

            if (_accessRefreshPair != null) _accessRefreshPair.IsEnabled = !_pairCodeBusy;
            if (_overlayRefreshPair != null) _overlayRefreshPair.IsEnabled = !_pairCodeBusy;
            if (string.Equals(text, _lastPairStatusText, StringComparison.Ordinal)) return;
            _lastPairStatusText = text;
            _accessPairStatus.Text = text;
            _overlayPairStatus.Text = text;
        }

        private void ClearPairCode()
        {
            _pairCodeGeneration++;
            _pairCode = string.Empty;
            _pairCodeIssuedAt = null;
            _pairCodeUsed = false;
            _pairCodeBusy = false;
            _pairCodeFailed = false;
            _lastPairStatusText = null;
            UpdatePairStatus();
        }

        private async void EnsurePairCodeAsync(bool force)
        {
            if (!_accessCodeMode) return;
            if (_pairCodeBusy) return;
            if (!force)
            {
                if (!string.IsNullOrEmpty(_pairCode) && _pairCodeIssuedAt.HasValue &&
                    !_pairCodeUsed && DateTime.UtcNow - _pairCodeIssuedAt.Value < PairCodeLifetime)
                {
                    return;
                }
                if (_pairCodeFailed) return;
            }

            if (string.IsNullOrEmpty(_openCodePath) || !IsServiceAlive())
            {
                _pairCodeFailed = true;
                UpdatePairStatus();
                return;
            }

            _pairCodeBusy = true;
            _pairCodeFailed = false;
            var generation = ++_pairCodeGeneration;
            UpdatePairStatus();
            try
            {
                var commands = new List<string[]>();
                commands.Add(new[] { "pair" });
                var result = await _runner.RunAsync(_openCodePath, commands, 30000);
                if (generation != _pairCodeGeneration || !_accessCodeMode) return;

                var code = AccessUrlBuilder.GetPairCode(result.Output);
                if (string.IsNullOrEmpty(code))
                {
                    _pairCodeFailed = true;
                    Logger.Warn("生成一次性访问链接失败: " + SecretMasker.Mask(result.Output ?? string.Empty));
                    SetAction("生成一次性链接失败");
                    return;
                }

                _pairCode = code;
                _pairCodeIssuedAt = DateTime.UtcNow;
                _pairCodeUsed = false;
                _pairCodeFailed = false;
                Logger.Info("已生成一次性访问链接");
                SetAction("已生成一次性链接，5 分钟内有效");
                UpdateAccessSurfaces();
            }
            catch (Exception ex)
            {
                if (generation == _pairCodeGeneration)
                {
                    _pairCodeFailed = true;
                    Logger.Warn("生成一次性访问链接失败: " + SecretMasker.Mask(ex.Message));
                    SetAction("生成一次性链接失败");
                }
            }
            finally
            {
                if (generation == _pairCodeGeneration)
                {
                    _pairCodeBusy = false;
                    UpdatePairStatus();
                }
            }
        }

        private IList<string> GetDisplayAccessUrls()
        {
            var urls = AccessUrlBuilder.GetAccessUrls(_serviceRawUrl, _serviceUrl);
            if (!_accessCodeMode || string.IsNullOrEmpty(_pairCode)) return urls;

            var result = new List<string>();
            foreach (var url in urls)
            {
                if (string.IsNullOrEmpty(url)) continue;
                result.Add(AccessUrlBuilder.BuildPairUrl(url, _pairCode));
            }
            return result;
        }

        private void UpdateAccessSurfaces()
        {
            if (_accessPopup != null && _accessPopup.IsOpen) SetAccessPopupContent();
            if (_webHidden) ShowHiddenPlaceholder();
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
                _accessHint.Text = AccessHintText;
                return;
            }

            SetAccessRows(_accessUrlList, GetDisplayAccessUrls());
            UpdateAccessModeHint();
            EnsurePairCodeAsync(false);
        }

        private void ShowAccessPopup()
        {
            SyncAccessModeUi();
            if (_accessCodeMode && !_pairCodeBusy) _pairCodeFailed = false;
            SetAccessPopupContent();
            _accessPopup.PlacementTarget = _btnAccess;
            _accessPopup.IsOpen = true;
            if (!string.IsNullOrEmpty(_pairCode)) SetAccessRows(_accessUrlList, GetDisplayAccessUrls());
            EnsurePairCodeAsync(false);
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
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.ToolTip = url;
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
            if (withCtrl)
            {
                if (_runner.OpenExternalUrl(url))
                {
                    if (_accessCodeMode && !string.IsNullOrEmpty(_pairCode))
                    {
                        _pairCodeUsed = true;
                        SetAction("已在浏览器打开一次性链接");
                        UpdatePairStatus();
                    }
                    else
                    {
                        SetAction("已在浏览器打开 " + url);
                    }
                }
                else
                {
                    SetAction("打开浏览器失败");
                }
                return;
            }

            CopyAccessUrl(url);
        }

        private void CopyAccessUrl(string url)
        {
            try
            {
                Clipboard.SetText(url);
                SetAction(_accessCodeMode && !string.IsNullOrEmpty(_pairCode) ? "已复制一次性链接" : "已复制 " + url);
            }
            catch
            {
                SetAction("复制失败");
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
