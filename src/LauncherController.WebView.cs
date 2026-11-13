using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace OpenCodeLauncher
{
    internal sealed partial class LauncherController
    {
        private void InitializeWebView()
        {
            _webView = new WebView2();
            var creation = new CoreWebView2CreationProperties();
            creation.UserDataFolder = AppPaths.WebViewData;
            _webView.CreationProperties = creation;
            _webHost.Child = _webView;
            _webView.Visibility = Visibility.Collapsed;

            _webView.CoreWebView2InitializationCompleted += OnWebViewInitializationCompleted;
            _webView.NavigationStarting += OnNavigationStarting;
            _webView.NavigationCompleted += OnNavigationCompleted;
        }

        private async void OnWebViewInitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            _webViewInitializing = false;
            if (!e.IsSuccess)
            {
                var message = e.InitializationException == null
                    ? "未知错误"
                    : e.InitializationException.Message;
                _webViewInitFailed = true;
                _retryAfterInit = false;
                _connecting = false;
                _connectDeadline = null;
                _pendingUrl = string.Empty;
                _lastError = message;
                Logger.Error("WebView2 初始化失败: " + message);
                ShowViewError(
                    "无法初始化内嵌浏览器",
                    message + Environment.NewLine + Environment.NewLine + "请确认已安装 Microsoft Edge WebView2 Runtime；安装后点「重试」。",
                    false,
                    false);
                return;
            }

            _webViewInitFailed = false;
            _webReady = true;
            _lastError = string.Empty;
            Logger.Info("WebView2 运行时: " + _webView.CoreWebView2.Environment.BrowserVersionString);

            try
            {
                var settings = _webView.CoreWebView2.Settings;
                settings.AreDevToolsEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = true;
                settings.AreBrowserAcceleratorKeysEnabled = true;
                _webView.CoreWebView2.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs args)
                {
                    args.Handled = true;
                    Logger.Info("新窗口请求已转交系统浏览器: " + SecretMasker.Mask(args.Uri));
                    _runner.OpenExternalUrl(args.Uri);
                };
            }
            catch (Exception ex)
            {
                Logger.Warn("配置 WebView2 失败: " + ex.Message);
            }

            if (_retryAfterInit)
            {
                _retryAfterInit = false;
                Logger.Info("WebView2 重新初始化完成");
                await UpdateServiceRuntimeAsync();
                return;
            }

            if (!string.IsNullOrEmpty(_pendingUrl))
            {
                var target = _pendingUrl;
                _pendingUrl = string.Empty;
                Logger.Info("处理待定页面: " + SecretMasker.Mask(target));
                try
                {
                    _webView.CoreWebView2.Navigate(target);
                }
                catch (Exception ex)
                {
                    Logger.Error("打开待处理页面失败: " + ex.Message);
                }
            }
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            Logger.Info("开始导航: " + SecretMasker.Mask(e.Uri));
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var source = _webView.Source == null ? string.Empty : _webView.Source.ToString();
            Logger.Info("导航完成: success=" + e.IsSuccess + " url=" + SecretMasker.Mask(source));

            if (string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(_pendingUrl) && _connecting)
                {
                    var target = _pendingUrl;
                    _pendingUrl = string.Empty;
                    Logger.Info("从空白页跳转到登录链接");
                    try
                    {
                        _webView.CoreWebView2.Navigate(target);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("打开待处理页面失败: " + ex.Message);
                    }
                }
                return;
            }

            if (_manualLogin)
            {
                _manualLogin = false;
                _connecting = false;
                _connectDeadline = null;
                if (e.IsSuccess)
                {
                    _connectedUrl = _serviceUrl;
                    _pendingUrl = string.Empty;
                    SetUiState(ServiceUiState.Running);
                    HideOverlay();
                    SetAction("已打开登录页，请手动输入密码");
                    _webView.Focus();
                }
                else
                {
                    Logger.Warn("登录页加载失败: " + e.WebErrorStatus + " " + source);
                    ShowViewError("打开登录页失败", "页面加载失败: " + e.WebErrorStatus, true, false);
                    SetAction("打开登录页失败");
                }
                return;
            }

            if (!_connecting) return;
            if (source.IndexOf("/auth/connect/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (e.IsSuccess) _connectLastSuccess = DateTime.UtcNow;
                return;
            }

            if (e.IsSuccess)
            {
                _connectedUrl = _serviceUrl;
                _connecting = false;
                _connectDeadline = null;
                _pendingUrl = string.Empty;
                SetUiState(ServiceUiState.Running);
                HideOverlay();
                SetAction("已连接 · " + DateTime.Now.ToString("HH:mm:ss"));
                _webView.Focus();
            }
            else
            {
                Logger.Warn("页面加载失败: " + e.WebErrorStatus + " " + source);
                if (!string.IsNullOrEmpty(_lastError))
                {
                    _connecting = false;
                    _connectDeadline = null;
                    ShowViewError("无法连接后台服务", _lastError, true, false);
                    SetAction("连接失败");
                }
            }
        }

        private async Task ConnectWebAsync()
        {
            if (_exiting || string.IsNullOrEmpty(_serviceUrl) || _connecting) return;
            if (_webHidden) return;
            // 启动时未找到 opencode 会跳过 WebView2 初始化，首次真正连接时再按需初始化。
            if (!_webReady)
            {
                if (_webViewInitFailed)
                {
                    var detail = _lastError;
                    if (string.IsNullOrEmpty(detail)) detail = "请确认已安装 Microsoft Edge WebView2 Runtime，安装后点「重试」。";
                    ShowViewError("无法初始化内嵌浏览器", detail, false, false);
                    return;
                }
                if (_webViewInitializing)
                {
                    _retryAfterInit = true;
                    ShowOverlay("正在初始化内嵌浏览器", "WebView2 正在初始化，完成后会自动继续连接…", true, false, false, false, false, false);
                    return;
                }
                ShowOverlay("正在初始化内嵌浏览器", "正在初始化 WebView2…", true, false, false, false, false, false);
                await InitializeWebViewAsync();
                if (!_webReady)
                {
                    _retryAfterInit = true;
                    return;
                }
                if (_connecting) return;
            }

            _connectGeneration++;
            var generation = _connectGeneration;
            ResumeWebView();
            _connecting = true;
            _connectedUrl = string.Empty;
            _connectLastSuccess = null;
            _lastError = string.Empty;
            _manualLogin = false;
            _connectDeadline = DateTime.UtcNow.AddSeconds(30);
            ShowOverlay("正在连接", "正在为内嵌窗口生成一次性访问链接…", true, false, false, false, false, false);

            var commands = new System.Collections.Generic.List<string[]>();
            commands.Add(new[] { "pair" });
            var result = await _runner.RunAsync(_openCodePath, commands, 30000);
            if (generation != _connectGeneration || !_connecting) return;

            if (!result.Ok)
            {
                var reason = (result.Output ?? string.Empty).Trim();
                if (reason.Length > 700) reason = reason.Substring(0, 700) + "…";
                _lastError = reason;
                Logger.Warn("生成一次性访问链接失败(exit=" + result.ExitCode + "): " + reason);
            }

            var target = GetPairUrl(result.Output);
            if (string.IsNullOrEmpty(target))
            {
                _connecting = false;
                _connectDeadline = null;
                _pendingUrl = string.Empty;
                var detail = string.IsNullOrEmpty(_lastError)
                    ? "未能从 opencode pair 获取一次性访问链接。"
                    : _lastError;
                var message = detail + Environment.NewLine + Environment.NewLine +
                              "可以点「重试」重新生成，或点「打开登录页」手动输入密码（密码见 service.json 的 password）。";
                Logger.Warn("生成访问链接失败: " + result.Output);
                ShowViewError("无法自动登录", message, false, true);
                SetAction("需要手动登录");
                return;
            }

            Logger.Info("已生成一次性访问链接");
            _pendingUrl = target;
            Logger.Info("准备打开页面: WebReady=" + _webReady + " target=" + SecretMasker.Mask(target));
            if (_webReady && _webView.CoreWebView2 != null)
            {
                try
                {
                    _webView.CoreWebView2.Navigate(target);
                    _pendingUrl = string.Empty;
                    Logger.Info("已调用 WebView2.Navigate");
                }
                catch (Exception ex)
                {
                    Logger.Error("打开页面失败: " + ex.Message);
                    _connecting = false;
                    _connectDeadline = null;
                    _pendingUrl = string.Empty;
                    ShowViewError("打开页面失败", ex.Message, true, false);
                }
            }
        }

        private void ShowManualLoginPage()
        {
            if (string.IsNullOrEmpty(_serviceUrl))
            {
                SetAction("服务未运行，无法打开登录页");
                return;
            }
            if (!_webReady || _webView.CoreWebView2 == null)
            {
                ShowViewError("内嵌浏览器尚未就绪", "WebView2 还没有初始化完成。请稍后重试，或先检查 WebView2 Runtime 是否已安装。", false, false);
                return;
            }

            _connecting = false;
            _connectDeadline = null;
            _pendingUrl = string.Empty;
            _connectedUrl = string.Empty;
            _manualLogin = true;
            _lastError = string.Empty;
            HideOverlay();
            SetAction("正在打开登录页…");
            try
            {
                _webView.CoreWebView2.Navigate(_serviceUrl);
            }
            catch (Exception ex)
            {
                _manualLogin = false;
                ShowViewError("打开登录页失败", ex.Message, true, false);
                SetAction("打开登录页失败");
            }
        }

        private async void ShowWebPage()
        {
            _webHidden = false;
            UpdateWebToggle();
            if (!string.IsNullOrEmpty(_operation)) return;
            ResumeWebView();

            if (_webViewInitFailed)
            {
                var detail = _lastError;
                if (string.IsNullOrEmpty(detail)) detail = "请确认已安装 Microsoft Edge WebView2 Runtime，安装后点「重试」。";
                ShowViewError("无法初始化内嵌浏览器", detail, false, false);
                SetAction("内嵌浏览器初始化失败");
                return;
            }

            if (_serviceState != ServiceUiState.Running)
            {
                var runtime = ServiceRuntime.Read();
                if (runtime == null)
                {
                    ShowOverlay("服务未运行", "OpenCode 后台服务当前未运行。启动后即可在窗口内使用 Web 界面。", false, true, false, false, false, false);
                    SetAction("服务未运行");
                    return;
                }
                _serviceUrl = AccessUrlBuilder.ConvertToLocal(runtime.Url);
                _serviceRawUrl = runtime.Url;
                _servicePid = runtime.Pid;
                _urlText.Text = _serviceUrl;
                _urlText.ToolTip = AccessHintText;
                SetUiState(ServiceUiState.Running);
            }

            if (!string.IsNullOrEmpty(_connectedUrl))
            {
                _overlay.Visibility = Visibility.Collapsed;
                if (_webView != null) _webView.Visibility = Visibility.Visible;
                SetAction("内嵌页面已显示");
            }
            else
            {
                await ConnectWebAsync();
            }
        }

        private void HideWebPage()
        {
            _webHidden = true;
            _connecting = false;
            _connectDeadline = null;
            if (_webView != null) _webView.Visibility = Visibility.Collapsed;
            ShowHiddenPlaceholder();
            UpdateWebToggle();
            SetAction("内嵌页面已隐藏");
            ScheduleSuspend(300, true);
        }

        private void ToggleWebPage()
        {
            if (_webHidden) ShowWebPage();
            else HideWebPage();
        }

        private async void ScheduleSuspend(int delayMs, bool force)
        {
            var generation = ++_suspendGeneration;
            await Task.Delay(delayMs);
            if (generation != _suspendGeneration) return;
            await SuspendWebViewAsync(force);
        }

        private async Task SuspendWebViewAsync(bool force)
        {
            if (!force && !_config.SuspendOnHide) return;
            if (_webViewSuspended) return;
            if (!_webHidden && Window.IsVisible) return;
            if (_webView == null || _webView.CoreWebView2 == null) return;
            try
            {
                var suspended = await _webView.CoreWebView2.TrySuspendAsync();
                if (suspended)
                {
                    _webViewSuspended = true;
                    if (!_webHidden && Window.IsVisible)
                    {
                        ResumeWebView();
                    }
                    else
                    {
                        Logger.Info("已挂起内嵌页面以降低资源占用");
                    }
                }
                else
                {
                    Logger.Warn("挂起内嵌页面失败");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("挂起内嵌页面失败: " + ex.Message);
            }
        }

        private void ResumeWebView()
        {
            _suspendGeneration++;
            if (_webView == null || _webView.CoreWebView2 == null) return;
            if (!_webViewSuspended) return;
            try
            {
                _webView.CoreWebView2.Resume();
                _webViewSuspended = false;
                Logger.Info("已恢复内嵌页面");
            }
            catch (Exception ex)
            {
                Logger.Warn("恢复内嵌页面失败: " + ex.Message);
            }
        }
    }
}
