using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

namespace OpenCodeLauncher
{
    internal enum ServiceUiState
    {
        Unknown,
        Running,
        Stopped,
        Busy,
        Error
    }

    internal sealed partial class LauncherController
    {
        public Window Window { get; private set; }

        private readonly Application _application;
        private readonly LauncherConfig _config;
        private readonly OpenCodeLocator _locator = new OpenCodeLocator();
        private readonly ProcessRunner _runner = new ProcessRunner();
        private readonly DispatcherTimer _timer;

        private static readonly TimeSpan PairCodeLifetime = TimeSpan.FromMinutes(5);
        private const string AccessHintText = "点击地址复制；Ctrl+点击在浏览器打开。";

        private ServiceUiState _serviceState = ServiceUiState.Unknown;
        private string _serviceUrl = string.Empty;
        private string _serviceRawUrl = string.Empty;
        private int _servicePid;
        private int _serviceGoneCount;
        private string _openCodePath = string.Empty;
        private bool _openCodePathTemporary;
        private string _connectedUrl = string.Empty;
        private bool _connecting;
        private DateTime? _connectDeadline;
        private DateTime? _connectLastSuccess;
        private string _pendingUrl = string.Empty;
        private bool _webReady;
        private bool _webViewInitFailed;
        private bool _webViewInitializing;
        private bool _retryAfterInit;
        private bool _manualLogin;
        private string _lastError = string.Empty;
        private bool _webViewSuspended;
        private bool _webHidden;
        private bool _accessCodeMode;
        private bool _syncAccessMode;
        private string _pairCode = string.Empty;
        private DateTime? _pairCodeIssuedAt;
        private bool _pairCodeUsed;
        private bool _pairCodeBusy;
        private bool _pairCodeFailed;
        private int _pairCodeGeneration;
        private string _lastPairStatusText = string.Empty;
        private string _operation;
        private bool _autoStartAttempted;
        private string _version = string.Empty;
        private bool _exiting;
        private bool _restartAfterApply;
        private bool _logOpen;
        private DateTime _lastStatePoll = DateTime.MinValue;
        private int _connectGeneration;
        private int _suspendGeneration;
        private DateTime _lastVersionCheck = DateTime.MinValue;
        private bool _windowLoaded;
        private bool _versionCheckRunning;

        private TextBlock _statusText;
        private Ellipse _statusDot;
        private Border _statusPill;
        private TextBlock _urlText;
        private TextBlock _exeText;
        private TextBlock _actionText;
        private Button _btnStart;
        private Button _btnRestart;
        private Button _btnStop;
        private Button _btnWebToggle;
        private Button _btnBrowser;
        private Button _btnAccess;
        private Button _btnSettings;
        private Button _btnTray;
        private Border _webHost;
        private Grid _overlay;
        private TextBlock _overlayTitle;
        private TextBlock _overlayMessage;
        private StackPanel _overlayActions;
        private Button _btnOverlayStart;
        private Button _btnOverlayShow;
        private Button _btnOverlayLogin;
        private Button _btnOverlayRetry;
        private Grid _spinnerBox;
        private RotateTransform _spinnerRotate;
        private Popup _accessPopup;
        private Border _accessCard;
        private StackPanel _accessUrlList;
        private TextBlock _accessHint;
        private Button _accessClose;
        private ToggleButton _accessCodeModeToggle;
        private ToggleButton _overlayCodeModeToggle;
        private TextBlock _accessCodeLabel;
        private TextBlock _overlayCodeLabel;
        private TextBlock _accessPairStatus;
        private Button _accessRefreshPair;
        private TextBlock _overlayPairStatus;
        private Button _overlayRefreshPair;
        private ScrollViewer _overlayAddressScroll;
        private StackPanel _overlayAddressList;
        private TextBlock _overlayAddressHint;
        private StackPanel _overlayAddressModeRow;
        private Border _logPanel;
        private TextBox _logBox;
        private Button _btnLog;
        private Button _btnCopyLog;
        private Button _btnClearLog;
        private Button _btnCloseLog;
        private Button _btnOpenData;
        private Image _logoImage;

        private WebView2 _webView;
        private Forms.NotifyIcon _trayIcon;
        private Forms.ToolStripMenuItem _trayStart;
        private Forms.ToolStripMenuItem _trayStop;
        private Forms.ToolStripMenuItem _trayBrowser;
        private Storyboard _spinnerStoryboard;
        private HwndSource _hwndSource;

        public LauncherController(Application application)
        {
            _application = application;
            AppPaths.EnsureDirectories();
            _config = LauncherConfig.Load();
            _accessCodeMode = _config.AccessCodeMode;

            Window = XamlLoader.LoadWindow(AppPaths.MainWindowXaml);
            Window.Icon = BitmapFrame.Create(new Uri(AppPaths.IconPath));
            LoadControls();
            LoadLogo();
            WireWindowEvents();
            WireButtons();
            WireAccessMode();
            InitializeWebView();
            InitializeTray();
            RestoreWindowBounds();
            UpdateWebToggle();
            SetUiState(ServiceUiState.Unknown);

            Logger.LineWritten += OnLogLine;
            Logger.Info("OpenCode Launcher (C#) 启动");
            if (Native.TryEnablePerMonitorDpi()) Logger.Info("已启用 PerMonitorV2 DPI 感知");

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(120);
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        public bool Exiting
        {
            get { return _exiting; }
            set { _exiting = value; }
        }

        public void ReportInternalError()
        {
            SetAction("发生内部错误，详情见日志");
        }

        private T Find<T>(string name) where T : class
        {
            var value = Window.FindName(name) as T;
            if (value == null) throw new InvalidOperationException("找不到 XAML 控件: " + name);
            return value;
        }

        private void LoadControls()
        {
            _statusText = Find<TextBlock>("StatusText");
            _statusDot = Find<Ellipse>("StatusDot");
            _statusPill = Find<Border>("StatusPill");
            _urlText = Find<TextBlock>("UrlText");
            _exeText = Find<TextBlock>("ExeText");
            _actionText = Find<TextBlock>("ActionText");
            _btnStart = Find<Button>("BtnStart");
            _btnRestart = Find<Button>("BtnRestart");
            _btnStop = Find<Button>("BtnStop");
            _btnWebToggle = Find<Button>("BtnWebToggle");
            _btnBrowser = Find<Button>("BtnBrowser");
            _btnAccess = Find<Button>("BtnAccess");
            _btnSettings = Find<Button>("BtnSettings");
            _btnTray = Find<Button>("BtnTray");
            _webHost = Find<Border>("WebHost");
            _overlay = Find<Grid>("Overlay");
            _overlayTitle = Find<TextBlock>("OverlayTitle");
            _overlayMessage = Find<TextBlock>("OverlayMessage");
            _overlayActions = Find<StackPanel>("OverlayActions");
            _btnOverlayStart = Find<Button>("BtnOverlayStart");
            _btnOverlayShow = Find<Button>("BtnOverlayShow");
            _btnOverlayLogin = Find<Button>("BtnOverlayLogin");
            _btnOverlayRetry = Find<Button>("BtnOverlayRetry");
            _spinnerBox = Find<Grid>("SpinnerBox");
            _spinnerRotate = Find<RotateTransform>("SpinnerRotate");
            _accessPopup = Find<Popup>("AccessPopup");
            _accessCard = Find<Border>("AccessCard");
            _accessUrlList = Find<StackPanel>("AccessUrlList");
            _accessHint = Find<TextBlock>("AccessHint");
            _accessClose = Find<Button>("AccessClose");
            _accessCodeModeToggle = Find<ToggleButton>("AccessCodeMode");
            _overlayCodeModeToggle = Find<ToggleButton>("OverlayCodeMode");
            _accessCodeLabel = Find<TextBlock>("AccessCodeLabel");
            _overlayCodeLabel = Find<TextBlock>("OverlayCodeLabel");
            _accessPairStatus = Find<TextBlock>("AccessPairStatus");
            _accessRefreshPair = Find<Button>("AccessRefreshPair");
            _overlayPairStatus = Find<TextBlock>("OverlayPairStatus");
            _overlayRefreshPair = Find<Button>("OverlayRefreshPair");
            _overlayAddressScroll = Find<ScrollViewer>("OverlayAddressScroll");
            _overlayAddressList = Find<StackPanel>("OverlayAddressList");
            _overlayAddressHint = Find<TextBlock>("OverlayAddressHint");
            _overlayAddressModeRow = Find<StackPanel>("OverlayAddressModeRow");
            _logPanel = Find<Border>("LogPanel");
            _logBox = Find<TextBox>("LogBox");
            _btnLog = Find<Button>("BtnLog");
            _btnCopyLog = Find<Button>("BtnCopyLog");
            _btnClearLog = Find<Button>("BtnClearLog");
            _btnCloseLog = Find<Button>("BtnCloseLog");
            _btnOpenData = Find<Button>("BtnOpenData");
            _logoImage = Find<Image>("LogoImage");
        }

        private void LoadLogo()
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(AppPaths.LogoPath);
            image.EndInit();
            image.Freeze();
            _logoImage.Source = image;
        }

        private void WireWindowEvents()
        {
            Window.SourceInitialized += OnSourceInitialized;
            Window.Loaded += OnWindowLoaded;
            Window.Closing += OnWindowClosing;
            Window.Closed += OnWindowClosed;
            Window.StateChanged += OnWindowStateChanged;
            Window.KeyDown += OnWindowKeyDown;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(Window);
                Native.ApplyDarkTitleBar(helper.Handle);
                _hwndSource = HwndSource.FromHwnd(helper.Handle);
                if (_hwndSource != null) _hwndSource.AddHook(HwndHook);
            }
            catch
            {
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)Native.ShowWindowMessage)
            {
                ShowMainWindow();
                handled = true;
            }
            else if (msg == (int)Native.ExitMessage)
            {
                ExitApp();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (_windowLoaded) return;
            _windowLoaded = true;
            ShowOverlay("正在启动", "正在检查 OpenCode 后台服务…", true, false, false, false, false, false);
            LocateOpenCode(true);
            UpdateStatusBar();

            if (string.IsNullOrEmpty(_openCodePath))
            {
                Logger.Error("未找到 opencode 可执行文件");
                SetUiState(ServiceUiState.Error);
                ShowOverlay("未找到 opencode", "请点击“设置”，在“opencode 可执行文件”中指定 opencode.exe、opencode.cmd 或 opencode.ps1 的完整路径。", false, false, true, false, false, false);
                SetAction("未找到 opencode");
                return;
            }

            Logger.Info("使用 opencode: " + _openCodePath);
            StartVersionCheck();
            await InitializeWebViewAsync();
            await UpdateServiceRuntimeAsync();
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (_exiting) return;
            if (_config.CloseToTray)
            {
                e.Cancel = true;
                SaveWindowBounds();
                _config.Save();
                HideAccessPopup();
                Window.Hide();
                ScheduleSuspend(350, false);
                ShowBalloon("程序仍在托盘运行，双击图标可重新打开。");
            }
            else
            {
                _exiting = true;
                StopServiceForExit();
                SaveWindowBounds();
                _config.Save();
            }
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            try
            {
                if (_timer != null) _timer.Stop();
            }
            catch
            {
            }
            DisposeTray();
            Logger.LineWritten -= OnLogLine;
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (Window.WindowState == WindowState.Minimized) HideAccessPopup();
        }

        private async void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                if (_webView != null && _webView.CoreWebView2 != null && !string.IsNullOrEmpty(_connectedUrl))
                {
                    _webView.CoreWebView2.Reload();
                }
                else
                {
                    await RetryConnectionAsync();
                }
            }
            else if (e.Key == Key.Escape && _accessPopup.IsOpen)
            {
                HideAccessPopup();
                e.Handled = true;
            }
        }

        private void ShowMainWindow()
        {
            if (Window == null) return;
            if (!_webHidden) ResumeWebView();
            if (!Window.IsVisible) Window.Show();
            if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
            Window.Activate();
            Window.Topmost = true;
            Window.Topmost = false;
            Window.Focus();
        }

        private void HideToTray()
        {
            HideAccessPopup();
            Window.Hide();
            ScheduleSuspend(350, false);
            ShowBalloon("程序仍在托盘运行，双击图标可重新打开。");
        }

        private void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            try
            {
                Window.Hide();
            }
            catch
            {
            }

            try
            {
                StopServiceForExit();
                SaveWindowBounds();
                _config.Save();
            }
            catch (Exception ex)
            {
                try
                {
                    Logger.Error("退出时保存状态失败: " + ex);
                }
                catch
                {
                }
            }
            finally
            {
                DisposeTray();
                try
                {
                    Window.Close();
                }
                catch (Exception ex)
                {
                    try
                    {
                        Logger.Error("关闭主窗口失败: " + ex);
                    }
                    catch
                    {
                    }
                    try
                    {
                        if (_application != null) _application.Shutdown();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void StopServiceForExit()
        {
            if (!_config.StopOnExit || string.IsNullOrEmpty(_openCodePath)) return;
            if (ServiceRuntime.Read() == null) return;
            Logger.Info("退出时停止后台服务");
            _runner.RunDetached(_openCodePath, new[] { "service", "stop" });
        }

        private void LocateOpenCode(bool save)
        {
            var result = _locator.Find(_config.OpenCodePath);
            if (result == null)
            {
                _openCodePath = string.Empty;
                _openCodePathTemporary = false;
                return;
            }

            _openCodePath = result.Path;
            _openCodePathTemporary = result.IsTemporary;
            if (save && !result.IsTemporary)
            {
                _config.OpenCodePath = result.Path;
                _config.Save();
            }
            else if (result.IsTemporary)
            {
                Logger.Warn("检测到临时 opencode 路径，本次使用但不写入配置");
            }
        }

        private void SetAction(string text)
        {
            if (_actionText != null) _actionText.Text = text;
        }

        private void UpdateStatusBar()
        {
            if (_exeText == null) return;
            var version = string.IsNullOrEmpty(_version) ? string.Empty : " · v" + _version.TrimStart('v');
            var path = string.IsNullOrEmpty(_openCodePath) ? "未找到 opencode" : _openCodePath;
            _exeText.Text = "opencode" + version + " · " + path;
            _exeText.ToolTip = path;
        }

        private void UpdateWebToggle()
        {
            if (_btnWebToggle == null) return;
            _btnWebToggle.Content = _webHidden ? "显示页面" : "隐藏页面";
            _btnWebToggle.ToolTip = _webHidden ? "恢复内嵌 Web 页面" : "隐藏内嵌 Web 页面，降低 CPU/内存占用";
        }

        private bool IsServiceAlive()
        {
            return _serviceState == ServiceUiState.Running ||
                   ((_serviceState == ServiceUiState.Error || _serviceState == ServiceUiState.Unknown) &&
                    !string.IsNullOrEmpty(_serviceUrl));
        }

        private void SetUiState(ServiceUiState state)
        {
            _serviceState = state;
            if (state != ServiceUiState.Running) ClearPairCode();
            if (_statusText == null) return;

            var background = "#1D232C";
            var dot = "#8B95A6";
            var foreground = "#B7C0CE";
            var label = "检测中";
            switch (state)
            {
                case ServiceUiState.Running:
                    background = "#16281F";
                    dot = "#45D09E";
                    foreground = "#7BE3B4";
                    label = "运行中";
                    break;
                case ServiceUiState.Stopped:
                    background = "#2A1A1C";
                    dot = "#F07178";
                    foreground = "#FFA3A8";
                    label = "已停止";
                    break;
                case ServiceUiState.Busy:
                    background = "#2A2418";
                    dot = "#F2C078";
                    foreground = "#F6D39E";
                    label = "处理中";
                    break;
                case ServiceUiState.Error:
                    background = "#2A1A1C";
                    dot = "#F07178";
                    foreground = "#FFA3A8";
                    label = "出错";
                    break;
            }

            _statusPill.Background = BrushFromHex(background);
            _statusDot.Fill = BrushFromHex(dot);
            _statusText.Foreground = BrushFromHex(foreground);
            _statusText.Text = label;
            Window.Title = "OpenCode Launcher · " + label;

            var serviceAlive = IsServiceAlive();
            var canStart = !serviceAlive && state != ServiceUiState.Busy;
            _btnStart.IsEnabled = canStart;
            _btnOverlayStart.IsEnabled = canStart;
            _btnRestart.IsEnabled = serviceAlive;
            _btnStop.IsEnabled = serviceAlive;
            _btnBrowser.IsEnabled = serviceAlive;

            if (_trayIcon != null) _trayIcon.Text = "OpenCode Launcher · " + label;
            if (_trayStart != null) _trayStart.Enabled = canStart;
            if (_trayStop != null) _trayStop.Enabled = serviceAlive;
            if (_trayBrowser != null) _trayBrowser.Enabled = serviceAlive;
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _lastStatePoll).TotalSeconds >= 5)
            {
                _lastStatePoll = DateTime.UtcNow;
                SyncServiceState();
            }

            if (_connectDeadline.HasValue && string.IsNullOrEmpty(_connectedUrl))
            {
                if (DateTime.UtcNow > _connectDeadline.Value)
                {
                    _connectDeadline = null;
                    if (_connectLastSuccess.HasValue &&
                        (DateTime.UtcNow - _connectLastSuccess.Value).TotalSeconds < 30 &&
                        _webView != null &&
                        _webView.Source != null &&
                        _webView.Source.ToString().IndexOf("/auth/connect/", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _connecting = false;
                        _connectedUrl = _serviceUrl;
                        _pendingUrl = string.Empty;
                        SetUiState(ServiceUiState.Running);
                        HideOverlay();
                        SetAction("已连接 · " + DateTime.Now.ToString("HH:mm:ss"));
                        Logger.Info("一次性链接页未跳转，但导航成功，按已连接处理");
                        return;
                    }

                    _connecting = false;
                    _pendingUrl = string.Empty;
                    Logger.Warn("连接内嵌页面超时");
                    var message = "无法完成内嵌页面连接。可以重试，或改用系统浏览器打开。";
                    if (!string.IsNullOrEmpty(_lastError)) message = _lastError;
                    ShowViewError("连接超时", message, true, false);
                    SetAction("连接超时");
                }
            }

            if (_accessCodeMode && ((_accessPopup != null && _accessPopup.IsOpen) || _webHidden))
            {
                UpdatePairStatus();
            }
        }

        private async void SyncServiceState()
        {
            if (_exiting || !string.IsNullOrEmpty(_operation) || _connecting) return;
            if (string.IsNullOrEmpty(_openCodePath)) return;

            var state = ServiceRuntime.Read();
            if (state != null)
            {
                _serviceGoneCount = 0;
                var url = AccessUrlBuilder.ConvertToLocal(state.Url);
                var changed = _servicePid != state.Pid ||
                              !string.Equals(url, _serviceUrl, StringComparison.Ordinal) ||
                              !string.Equals(state.Url, _serviceRawUrl, StringComparison.Ordinal);

                if ((_serviceState == ServiceUiState.Running || _serviceState == ServiceUiState.Error) && !changed)
                {
                    return;
                }

                _serviceUrl = url;
                _serviceRawUrl = state.Url;
                _servicePid = state.Pid;
                _autoStartAttempted = true;
                _urlText.Text = url;
                _urlText.ToolTip = AccessHintText;

                if (changed)
                {
                    _connectedUrl = string.Empty;
                    _connecting = false;
                    _connectDeadline = null;
                    _pendingUrl = string.Empty;
                    _manualLogin = false;
                    ClearPairCode();
                }

                SetUiState(ServiceUiState.Running);
                if (_accessPopup.IsOpen) SetAccessPopupContent();

                if (changed)
                {
                    SetAction("检测到服务已启动 · " + DateTime.Now.ToString("HH:mm:ss"));
                    Logger.Info("轮询检测到服务已启动或实例已变化");
                    if (_webHidden) ShowHiddenPlaceholder();
                    else await ConnectWebAsync();
                }
                return;
            }

            if (_serviceState != ServiceUiState.Running &&
                _serviceState != ServiceUiState.Error &&
                _serviceState != ServiceUiState.Unknown)
            {
                return;
            }

            if (_serviceGoneCount < 1)
            {
                _serviceGoneCount++;
                return;
            }

            _serviceGoneCount = 0;
            _serviceUrl = string.Empty;
            _serviceRawUrl = string.Empty;
            _servicePid = 0;
            _connectedUrl = string.Empty;
            _connecting = false;
            _connectDeadline = null;
            _pendingUrl = string.Empty;
            _manualLogin = false;
            _urlText.Text = "—";
            _urlText.ToolTip = string.Empty;
            SetUiState(ServiceUiState.Stopped);
            SetAction("检测到服务已停止");
            Logger.Warn("轮询检测到服务已停止");
            if (_accessPopup.IsOpen) SetAccessPopupContent();
            if (_webHidden) ShowHiddenPlaceholder();
            else ShowOverlay("服务未运行", "OpenCode 后台服务当前未运行。启动后即可在窗口内使用 Web 界面。", false, true, false, false, false, false);
        }

        private async Task InitializeWebViewAsync()
        {
            if (_webView == null) return;
            if (_webViewInitializing) return;
            _webViewInitializing = true;
            try
            {
                await _webView.EnsureCoreWebView2Async(null);
            }
            catch (Exception ex)
            {
                _webViewInitializing = false;
                _webViewInitFailed = true;
                _lastError = ex.Message;
                Logger.Error("启动 WebView2 失败: " + ex.Message);
                ShowViewError("无法初始化内嵌浏览器", ex.Message + Environment.NewLine + Environment.NewLine + "请确认已安装 Microsoft Edge WebView2 Runtime。", false, false);
            }
        }

        private async Task UpdateServiceRuntimeAsync()
        {
            if (string.IsNullOrEmpty(_openCodePath))
            {
                _serviceUrl = string.Empty;
                _serviceRawUrl = string.Empty;
                _servicePid = 0;
                _connectedUrl = string.Empty;
                _connecting = false;
                _connectDeadline = null;
                _pendingUrl = string.Empty;
                _manualLogin = false;
                if (_urlText != null)
                {
                    _urlText.Text = "—";
                    _urlText.ToolTip = string.Empty;
                }
                SetUiState(ServiceUiState.Error);
                ShowOverlay("未找到 opencode", "请点击“设置”，在“opencode 可执行文件”中指定完整路径。", false, false, true, false, false, false);
                return;
            }

            var state = ServiceRuntime.Read();
            if (state != null)
            {
                var url = AccessUrlBuilder.ConvertToLocal(state.Url);
                var identityChanged = _servicePid != state.Pid ||
                                      !string.Equals(url, _serviceUrl, StringComparison.Ordinal) ||
                                      !string.Equals(state.Url, _serviceRawUrl, StringComparison.Ordinal);
                _serviceUrl = url;
                _serviceRawUrl = state.Url;
                _servicePid = state.Pid;
                _autoStartAttempted = true;
                _urlText.Text = url;
                _urlText.ToolTip = AccessHintText;
                _lastError = string.Empty;
                if (identityChanged)
                {
                    _connectedUrl = string.Empty;
                    ClearPairCode();
                }
                _serviceGoneCount = 0;
                if (_serviceState != ServiceUiState.Running) SetUiState(ServiceUiState.Running);
                if (_accessPopup.IsOpen) SetAccessPopupContent();
                SetAction("服务运行中 · " + DateTime.Now.ToString("HH:mm:ss"));
                if (!string.IsNullOrEmpty(_connectedUrl) && !string.Equals(_connectedUrl, url, StringComparison.Ordinal)) _connectedUrl = string.Empty;
                if (_webHidden) ShowHiddenPlaceholder();
                else if (string.IsNullOrEmpty(_connectedUrl) && !_connecting) await ConnectWebAsync();
                return;
            }

            _serviceUrl = string.Empty;
            _serviceRawUrl = string.Empty;
            _servicePid = 0;
            _connectedUrl = string.Empty;
            _connecting = false;
            _connectDeadline = null;
            _pendingUrl = string.Empty;
            _manualLogin = false;
            _urlText.Text = "—";
            _urlText.ToolTip = string.Empty;
            SetUiState(ServiceUiState.Stopped);
            if (_accessPopup.IsOpen) SetAccessPopupContent();

            if (_config.AutoStart && !_autoStartAttempted)
            {
                _autoStartAttempted = true;
                Logger.Info("检测到后台服务未运行，正在自动启动");
                await StartServiceAsync();
                return;
            }

            ShowOverlay("服务未运行", "OpenCode 后台服务当前未运行。启动后即可在窗口内使用 Web 界面。", false, true, false, false, false, false);
            SetAction("服务未运行");
        }

        private async Task StartServiceAsync()
        {
            if (!string.IsNullOrEmpty(_operation) || _serviceState == ServiceUiState.Running) return;
            if (string.IsNullOrEmpty(_openCodePath))
            {
                SetUiState(ServiceUiState.Error);
                ShowOverlay("未找到 opencode", "请点击“设置”，在“opencode 可执行文件”中指定完整路径。", false, false, true, false, false, false);
                return;
            }
            if (ServiceConfigOps.IsDisabled())
            {
                ShowViewError("后台服务已被禁用", "service.json 中 disabled 为 true。请删除该配置项后重试。", false, false);
                return;
            }

            _operation = "start";
            _connectedUrl = string.Empty;
            _connecting = false;
            _connectDeadline = null;
            _servicePid = 0;
            _serviceUrl = string.Empty;
            _serviceRawUrl = string.Empty;
            _pendingUrl = string.Empty;
            _manualLogin = false;
            SetUiState(ServiceUiState.Busy);
            ShowOverlay("正在启动服务", "正在启动 OpenCode 后台服务…", true, false, false, false, false, false);
            SetAction("正在启动服务…");

            if (!_runner.RunDetached(_openCodePath, new[] { "service", "start" }))
            {
                _operation = null;
                _autoStartAttempted = true;
                SetUiState(ServiceUiState.Error);
                ShowViewError("启动失败", "无法启动 opencode 进程。请检查路径和权限。", true, false);
                SetAction("启动失败");
                return;
            }

            await WaitForServiceAsync("start", 40);
        }

        private async Task WaitForServiceAsync(string operation, int attempts)
        {
            for (var index = 0; index < attempts; index++)
            {
                if (!string.Equals(_operation, operation, StringComparison.Ordinal)) return;
                if (ServiceRuntime.Read() != null)
                {
                    _operation = null;
                    _autoStartAttempted = true;
                    Logger.Info(operation == "restart" ? "后台服务已重启" : "后台服务已启动");
                    await UpdateServiceRuntimeAsync();
                    return;
                }
                await Task.Delay(400);
            }

            if (!string.Equals(_operation, operation, StringComparison.Ordinal)) return;
            _operation = null;
            _autoStartAttempted = true;
            Logger.Error("服务未在规定时间内就绪: " + operation);
            SetUiState(ServiceUiState.Error);
            ShowViewError("操作超时", "已发出命令，但 16 秒内没有等到服务就绪。可点“重试”，或在终端执行 opencode service status 确认状态。", true, false);
            SetAction("操作超时");
        }

        private async Task RestartServiceAsync()
        {
            if (!string.IsNullOrEmpty(_operation)) return;
            if (_serviceState == ServiceUiState.Stopped)
            {
                await StartServiceAsync();
                return;
            }
            if (string.IsNullOrEmpty(_openCodePath))
            {
                SetUiState(ServiceUiState.Error);
                ShowOverlay("未找到 opencode", "请点击“设置”，在“opencode 可执行文件”中指定完整路径。", false, false, true, false, false, false);
                return;
            }
            if (ServiceConfigOps.IsDisabled())
            {
                ShowViewError("后台服务已被禁用", "service.json 中 disabled 为 true。请删除该配置项后重试。", true, false);
                return;
            }

            _operation = "restart";
            _connecting = false;
            _connectedUrl = string.Empty;
            _connectDeadline = null;
            _servicePid = 0;
            _serviceUrl = string.Empty;
            _serviceRawUrl = string.Empty;
            _pendingUrl = string.Empty;
            _manualLogin = false;
            SetUiState(ServiceUiState.Busy);
            ShowOverlay("正在重启服务", "正在重新启动 OpenCode 后台服务…", true, false, false, false, false, false);
            SetAction("正在重启服务…");

            if (!_runner.RunDetached(_openCodePath, new[] { "service", "restart" }))
            {
                _operation = null;
                SetUiState(ServiceUiState.Error);
                ShowViewError("重启失败", "无法启动 opencode 进程。请检查路径和权限。", true, false);
                SetAction("重启失败");
                return;
            }

            await WaitForServiceAsync("restart", 40);
        }

        private async Task StopServiceAsync()
        {
            if (!string.IsNullOrEmpty(_operation)) return;
            if (_serviceState != ServiceUiState.Running && ServiceRuntime.Read() == null) return;
            if (string.IsNullOrEmpty(_openCodePath))
            {
                ShowViewError("未找到 opencode", "无法停止服务：没有 opencode 可执行文件。请先在设置中指定路径。", true, false);
                SetAction("未找到 opencode");
                return;
            }

            var answer = MessageBox.Show(
                Window,
                "停止后台服务后，浏览器、终端和本窗口中的连接都会中断。确定停止？",
                "停止 OpenCode 服务",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            _operation = "stop";
            _autoStartAttempted = true;
            _connecting = false;
            _connectedUrl = string.Empty;
            _connectDeadline = null;
            _pendingUrl = string.Empty;
            _manualLogin = false;
            SetUiState(ServiceUiState.Busy);
            ShowOverlay("正在停止服务", "正在停止 OpenCode 后台服务…", true, false, false, false, false, false);
            SetAction("正在停止服务…");

            var commands = new List<string[]>();
            commands.Add(new[] { "service", "stop" });
            var result = await _runner.RunAsync(_openCodePath, commands, 45000);
            _operation = null;

            if (!result.Ok)
            {
                Logger.Warn("停止服务返回异常: " + result.Output);
                await UpdateServiceRuntimeAsync();
                SetAction("停止服务失败");
                return;
            }

            Logger.Info("后台服务已停止");
            _serviceUrl = string.Empty;
            _serviceRawUrl = string.Empty;
            _servicePid = 0;
            _connectedUrl = string.Empty;
            _connecting = false;
            _connectDeadline = null;
            _pendingUrl = string.Empty;
            _manualLogin = false;
            _urlText.Text = "—";
            _urlText.ToolTip = string.Empty;
            if (_accessPopup.IsOpen) SetAccessPopupContent();
            SetUiState(ServiceUiState.Stopped);
            if (_webHidden) ShowHiddenPlaceholder();
            else ShowOverlay("服务已停止", "OpenCode 后台服务已停止。点击“启动服务”即可重新启动。", false, true, false, false, false, false);
            SetAction("服务已停止");
        }

        private async Task OpenInBrowserAsync()
        {
            if (_serviceState != ServiceUiState.Running || string.IsNullOrEmpty(_serviceUrl))
            {
                var runtime = ServiceRuntime.Read();
                if (runtime == null)
                {
                    SetAction("服务未运行，无法打开浏览器");
                    return;
                }
                _serviceUrl = AccessUrlBuilder.ConvertToLocal(runtime.Url);
            }

            SetAction("正在生成浏览器访问链接…");
            var commands = new List<string[]>();
            commands.Add(new[] { "pair" });
            var result = await _runner.RunAsync(_openCodePath, commands, 30000);
            var url = GetPairUrl(result.Output);
            var manualLogin = false;
            if (string.IsNullOrEmpty(url))
            {
                url = _serviceUrl;
                manualLogin = true;
            }

            if (_runner.OpenExternalUrl(url))
            {
                if (manualLogin)
                {
                    SetAction("已打开登录页，请手动输入密码");
                    Logger.Warn("未能生成一次性链接，已在浏览器打开普通登录页");
                }
                else
                {
                    SetAction("已在浏览器中打开 · " + DateTime.Now.ToString("HH:mm:ss"));
                    Logger.Info("已在系统浏览器中打开 Web 界面");
                }
            }
            else
            {
                SetAction("打开浏览器失败");
            }
        }

        private async Task RetryConnectionAsync()
        {
            if (string.IsNullOrEmpty(_openCodePath) || !File.Exists(_openCodePath))
            {
                LocateOpenCode(true);
                UpdateStatusBar();
                StartVersionCheck();
            }
            if (string.IsNullOrEmpty(_openCodePath))
            {
                SetUiState(ServiceUiState.Error);
                ShowOverlay("未找到 opencode", "请点击“设置”，在“opencode 可执行文件”中指定完整路径。", false, false, true, false, false, false);
                return;
            }

            if (!_webReady)
            {
                if (_webViewInitializing)
                {
                    _retryAfterInit = true;
                    ShowOverlay("正在重新初始化内嵌浏览器", "WebView2 正在初始化，完成后会自动继续连接…", true, false, false, false, false, false);
                    return;
                }
                _retryAfterInit = true;
                _webViewInitFailed = false;
                _webViewInitializing = true;
                ShowOverlay("正在重新初始化内嵌浏览器", "正在重新加载 WebView2…", true, false, false, false, false, false);
                try
                {
                    await _webView.EnsureCoreWebView2Async(null);
                }
                catch (Exception ex)
                {
                    _webViewInitializing = false;
                    _retryAfterInit = false;
                    _webViewInitFailed = true;
                    ShowViewError("无法初始化内嵌浏览器", ex.Message, false, false);
                }
                return;
            }

            _connecting = false;
            await UpdateServiceRuntimeAsync();
        }

        private void StartVersionCheck()
        {
            if (string.IsNullOrEmpty(_openCodePath)) return;
            if (_versionCheckRunning) return;
            if ((DateTime.UtcNow - _lastVersionCheck).TotalSeconds < 2) return;
            _lastVersionCheck = DateTime.UtcNow;
            _versionCheckRunning = true;
            CheckVersionAsync();
        }

        private async void CheckVersionAsync()
        {
            try
            {
                var commands = new List<string[]>();
                commands.Add(new[] { "--version" });
                var result = await _runner.RunAsync(_openCodePath, commands, 15000);
                if (!result.Ok) return;
                var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return;
                _version = lines[0].Replace("opencode", string.Empty).Trim().TrimStart('v');
                UpdateStatusBar();
                Logger.Info("opencode 版本: " + _version);
            }
            catch
            {
            }
            finally
            {
                _versionCheckRunning = false;
            }
        }

        private void SaveWindowBounds()
        {
            if (Window == null) return;
            try
            {
                var bounds = Window.RestoreBounds;
                if (double.IsNaN(bounds.Width) || bounds.Width < 1000 || bounds.Height < 600) return;
                _config.Window.Width = bounds.Width;
                _config.Window.Height = bounds.Height;
                _config.Window.Left = bounds.Left;
                _config.Window.Top = bounds.Top;
                _config.Window.Maximized = Window.WindowState == WindowState.Maximized;
            }
            catch
            {
            }
        }

        private void RestoreWindowBounds()
        {
            var window = _config.Window;
            var width = window.Width >= 1000 ? window.Width : 1360;
            var height = window.Height >= 600 ? window.Height : 900;
            var maxWidth = Math.Max(1000, SystemParameters.VirtualScreenWidth - 32);
            var maxHeight = Math.Max(600, SystemParameters.VirtualScreenHeight - 32);
            if (width > maxWidth) width = maxWidth;
            if (height > maxHeight) height = maxHeight;
            Window.Width = width;
            Window.Height = height;

            if (window.Left.HasValue && window.Top.HasValue &&
                TestOnScreen(window.Left.Value, window.Top.Value, width, height))
            {
                Window.WindowStartupLocation = WindowStartupLocation.Manual;
                Window.Left = window.Left.Value;
                Window.Top = window.Top.Value;
            }
            if (window.Maximized) Window.WindowState = WindowState.Maximized;
        }

        private static bool TestOnScreen(double left, double top, double width, double height)
        {
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
            return left < virtualRight && left + width > virtualLeft &&
                   top < virtualBottom && top + height > virtualTop;
        }

        private void OnLogLine(string line)
        {
            if (_logBox == null) return;
            try
            {
                _logBox.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (_logBox == null) return;
                    _logBox.AppendText(line + Environment.NewLine);
                    if (_logBox.Text.Length > 120000)
                    {
                        var text = _logBox.Text;
                        var cut = text.Length - 90000;
                        var next = text.IndexOf('\n', cut);
                        if (next > 0) cut = next + 1;
                        _logBox.Text = text.Substring(cut);
                    }
                    _logBox.ScrollToEnd();
                }));
            }
            catch
            {
            }
        }
    }
}
