# OpenCode Launcher

一个轻量的 Windows 托盘工具，用原生 **C# WPF + WebView2** 封装 OpenCode v2 的 Web 界面。

- 启动、停止、重启 OpenCode 后台服务
- 内嵌 WebView2 显示 OpenCode Web UI
- 通过 `opencode pair` 自动生成一次性登录链接
- 支持系统托盘、访问地址、设置、日志和手动登录

## 快速开始

1. 安装 **OpenCode v2**。
2. 下载并运行用户版安装包，或解压免安装 zip 后双击 `OpenCodeLauncher.exe`。
3. Launcher 会自动探测 `opencode`，必要时启动后台服务，并生成一次性链接登录内嵌页面。
4. 关闭窗口默认最小化到托盘；右键托盘图标可以退出或执行常用操作。

## 界面与按钮

| 按钮 | 作用 |
| --- | --- |
| 启动 | 启动 OpenCode 后台服务（`opencode service start`） |
| 重启 | 重启后台服务（`opencode service restart`） |
| 停止 | 停止后台服务，会中断所有已连接会话，需要二次确认 |
| 隐藏页面 / 显示页面 | 隐藏内嵌页面并挂起 WebView，降低 CPU/内存；隐藏时显示访问地址和一次性链接开关 |
| 在浏览器打开 | 生成新的一次性链接并用系统默认浏览器打开 |
| 访问地址 | 列出全部可访问地址（绑定 `0.0.0.0` 时包含本机和局域网 IP）；默认点击复制、Ctrl+点击在浏览器打开；开关默认开启，开启后地址显示为带 code 的一次性链接（5 分钟内有效，可刷新） |
| 设置 | 修改服务参数和程序设置 |
| 最小化 | 隐藏到系统托盘，程序继续运行 |

状态栏右侧的「日志」可以展开运行日志；「配置目录」直接打开 `~/.config/opencode`。

自动登录失败时，覆盖层会提供：

- **重试**：重新生成一次性链接
- **打开登录页**：直接加载服务登录页，手动输入 `service.json` 中的密码
- **启动服务**：服务未运行时显示

## 设置项

服务参数会写入 OpenCode 自己的 `~/.config/opencode/service.json`，与命令行、终端共享同一份配置。

| 参数 | 说明 |
| --- | --- |
| 监听地址 | 留空 = `127.0.0.1`；`0.0.0.0` 允许局域网访问 |
| 端口 | 留空 = OpenCode 分配的默认端口；保存后自动重启服务生效 |
| 访问密码 | 留空 = 取消密码保护（会二次确认）；可切换明文显示 |
| CORS 来源 | 多个地址用英文逗号分隔 |
| opencode 路径 | 自动探测失败时手动指定 `opencode.exe`、`opencode.cmd` 或 `opencode.ps1` |

程序级设置默认保存在程序目录的 `config.json`，便于免安装版随程序一起拷贝；程序目录不可写时会提示选择，默认回退到 `%APPDATA%\OpenCodeLauncher\config.json`：

- 启动 Launcher 时自动启动后台服务
- 关闭窗口时最小化到系统托盘
- 最小化到托盘时挂起内嵌页面（默认开启）
- 退出 Launcher 时停止后台服务

设置窗口里还可以随时创建桌面快捷方式。

## 数据与日志

| 内容 | 位置 |
| --- | --- |
| Launcher 配置 | 程序目录 `config.json`（不可写时为 `%APPDATA%\OpenCodeLauncher\config.json`） |
| Launcher 配置目录记录 | `%LOCALAPPDATA%\OpenCodeLauncher\config-dir.txt`、`config-migrated.txt` |
| 运行日志 | `%LOCALAPPDATA%\OpenCodeLauncher\launcher.log` |
| WebView2 用户数据（含登录 Cookie） | `%LOCALAPPDATA%\OpenCodeLauncher\WebView2-<会话ID>` |
| OpenCode 服务配置 | `%USERPROFILE%\.config\opencode\service.json` |
| OpenCode 运行时状态 | `%USERPROFILE%\.local\state\opencode\service.json` |

## 从源码构建

使用的是 Windows 自带的 .NET Framework 4.8 C# 编译器（`csc.exe`）。

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-CSharp.ps1 -RunTests
```

输出：

- `build\csharp\OpenCodeLauncher.exe`
- `build\csharp\app\`（XAML、图标、WebView2 DLL）
- `build\csharp\README.md`、`LICENSE`、`VERSION`、`BUILD.txt`
- `build\OpenCodeLauncher-csharp.zip`

运行：

```powershell
build\csharp\OpenCodeLauncher.exe
```

构建辅助脚本 `tools\Build-CSharp.ps1` 和 UI 冒烟测试脚本使用 Windows 自带 PowerShell 5.1。

### 自测与 UI 冒烟测试

`-RunTests` 会执行 `OpenCodeLauncher.exe --self-test`，覆盖：

- 命令行参数传递与 `.cmd` 特殊字符防护
- 密码、pair token 打码
- 本地 URL / IPv4 / IPv6 地址转换
- XAML 加载和关键控件存在性

交互式桌面环境可以运行：

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File tools\UiSmokeTest.ps1
```

它会实际点击访问地址、隐藏/显示页面、日志、设置保存/取消、最小化、关闭到托盘和恢复，最后用 `-Exit` 退出。

> `UiSmokeTest.ps1 -TestRestart` 会重启 OpenCode 服务，从而断开任何正在运行的 OpenCode 会话，请谨慎使用。

## 源码结构

```
src/
  Program.cs                    入口、单实例、-Exit
  LauncherApp.cs                WPF Application、资源、会话结束
  LauncherController.cs         状态、窗口事件、服务操作
  LauncherController.Ui.cs      覆盖层、访问地址、开关
  LauncherController.WebView.cs WebView2 初始化/连接/挂起/手动登录
  LauncherController.Tray.cs    托盘图标和菜单
  LauncherController.Settings.cs 设置窗口与应用流程
  AppPaths.cs                   路径、配置、日志、WebView 数据
  Config.cs                     Launcher 配置、服务配置 diff
  OpenCodeLocator.cs            从 npm/fnm shim 解析真实 opencode.exe
  ProcessRunner.cs              异步进程执行与超时
  ServiceRuntime.cs             运行时状态、访问地址
  CommandLine.cs                Windows/cmd 参数转义
  SecretMasker.cs               密码/pair token 打码
  Json.cs                       JavaScriptSerializer 封装
  Logger.cs                     文件日志 + UI 事件
  SelfTest.cs                   --self-test
app/
  MainWindow.xaml               主窗口
  SettingsWindow.xaml           设置窗口
  Styles.xaml                   样式
  assets/                       图标和 logo
  lib/                          WebView2 SDK
installer/
  OpenCodeLauncherCSharp.iss    C# 版安装包
tools/
  Build-CSharp.ps1              csc 构建脚本
  UiSmokeTest.ps1               UI Automation 冒烟测试
  Update-WebView2.ps1           更新 WebView2 SDK（可选）
```


## 安全提示

- `service.json` 中的密码是明文存储的，和 OpenCode 官方行为一致
- 保存密码时 `opencode service set password <value>` 会短暂出现在进程命令行中，同用户的其他进程可能看到
- 将监听地址设为 `0.0.0.0` 会把服务暴露到局域网，请务必设置**强密码**
- WebView2 用户数据目录包含登录 Cookie，不要随意分享
- 日志会对密码和 pair token 打码，但不要把日志直接发到公开渠道
- 当前安装包没有代码签名，首次运行可能出现 SmartScreen 提示

## 常见问题

**提示「未找到 opencode」**  
在设置中点「浏览…」，选择 `opencode.exe`、`opencode.cmd` 或 `opencode.ps1`。可用 `where opencode` 查找。

**启动失败，提示服务被禁用**  
检查 `~/.config/opencode/service.json` 中是否存在 `"disabled": true`，删除该项后重试。

**端口被占用导致重启失败**  
换一个端口，或在设置中修改端口后点「保存并重启」。

**内嵌窗口白屏 / 初始化失败**  
确认已安装 WebView2 Runtime；仍失败时可手动删除 `%LOCALAPPDATA%\OpenCodeLauncher\WebView2-<会话ID>` 后重启 Launcher。

**第三方登录弹窗打不开**  
内嵌页面的 `target=_blank` 链接和第三方 OAuth 弹窗会交给系统默认浏览器。若服务商要求弹窗和原页面共享 `window.opener`，请在系统浏览器中完成登录，或使用支持外部回跳的登录方式。

**停止服务会不会影响终端里的 OpenCode？**  
会。v2 的终端界面与 Web 界面共用同一个后台服务，停止后所有连接都会中断。停止后 Launcher 不会调用 `opencode service status`，所以服务会保持停止状态。

**如何开机启动？**  
安装时勾选启动项，或把桌面快捷方式复制到 `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`。

## 许可证

本项目使用 MIT 许可证，详见根目录 [`LICENSE`](LICENSE)。随包分发的 WebView2 SDK 仍受其自身许可证约束（见 `app\lib\LICENSE-WebView2.txt`）。
