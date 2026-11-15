# Repository Guidelines

## What this repo is

Windows-only **C# WPF (.NET Framework 4.8, x64)** tray launcher for the OpenCode v2 Web UI, using **WebView2**. The PowerShell wrapper (`Launcher.ps1`, VBS/CMD entrypoints, PowerShell installer/publish scripts) has been removed; do not reintroduce it. Only build/test helper scripts remain PowerShell.

- There is **no `.csproj`, solution, `dotnet build`, MSBuild, NuGet, or test framework**. The product is compiled with Windows' built-in `csc.exe` through `tools\Build-CSharp.ps1`.
- `README.md` is intentionally concise and user-maintained; read it for product behavior, setup, paths and build commands. Release/download naming is not documented there — treat `installer\OpenCodeLauncherCSharp.iss` and the workflows as authoritative, and do not re-add removed README sections without the user asking.

## Build and verify

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-CSharp.ps1 -RunTests
```

- Output: `build\csharp\OpenCodeLauncher.exe`, `build\csharp\app\`, `build\OpenCodeLauncher-csharp.zip`.
- `-RunTests` runs `OpenCodeLauncher.exe --self-test`, the only automated test suite. Add pure-logic/XAML checks to `src\SelfTest.cs`; do not invent a separate test project.
- The compiler is the **C# 5** compiler from .NET Framework. Do not use string interpolation, `nameof`, `?.`, expression-bodied members, tuples, pattern matching, or other C# 6+ syntax. The build script passes `/codepage:65001`.
- The app is x64; `WebView2Loader.dll` is x64. Do not switch to AnyCPU/.NET Core without revalidating WebView2.
- Inno Setup is not guaranteed locally; the local build does **not** compile installers. Installer changes are validated by `.github/workflows/build-csharp.yml` on `windows-latest` (or install Inno Setup 6 locally).

### UI smoke test

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File tools\UiSmokeTest.ps1
```

- Requires an interactive desktop and a built `build\csharp\OpenCodeLauncher.exe`.
- Tests access popup, hide/show page, log drawer, settings save/cancel, minimize/close-to-tray, restore, and `-Exit`.
- **Never run `-TestRestart` while an OpenCode session is active.** It restarts the OpenCode service and disconnects the current session. Use only with explicit user approval.

## Code layout and UI traps

Source tree (same as `README.md`):

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

- Entry flow: `src\Program.cs` → `LauncherApp` → `LauncherController`. Controller behavior is split across `LauncherController.cs`, `.Ui.cs`, `.WebView.cs`, `.Settings.cs`, `.Tray.cs`; put new behavior in the matching partial.
- XAML is loaded at runtime with `XamlReader.Load`; there is no BAML/XAML compiler. `app\*.xaml` is copied into the build output.
- When adding/renaming/removing a control, update `LauncherController.LoadControls` (`FindName` fields) **and** the relevant `SelfTest` name list. The self-test deliberately fails if controls are missing.
- WebView2 SDK assemblies live in `app\lib`; `Program.ResolveAssembly` and `Native.AddLibraryDirectory` depend on that location. Do not move them or introduce NuGet. Use `tools\Update-WebView2.ps1` if the SDK must be updated.
- The single-instance mutex `Local\OpenCodeLauncher_7F3A9C21` is duplicated as `AppMutex` in `installer\OpenCodeLauncherCSharp.iss`; keep both in sync.
- Debug from `%LOCALAPPDATA%\OpenCodeLauncher\launcher.log`; Launcher config is `config.json` in the program directory, falling back to `%APPDATA%\OpenCodeLauncher\config.json` when that directory is not writable; service config/state are under `%USERPROFILE%\.config\opencode` and `.local\state\opencode`.

## OpenCode integration and safety

- Never pass user-controlled values (password, CORS) to a `.cmd`/`.bat` via PowerShell/cmd; cmd metacharacters can execute commands. Use `ProcessRunner`; it prefers real `.exe`/`.ps1` and rejects unsafe `.cmd` arguments. `OpenCodeLocator` resolves npm/fnm shims to the real `opencode.exe`; preserve that behavior.
- Do **not** call `opencode service status` to detect state: it can wake a stopped service. Read `~/.local/state/opencode/service.json` and verify the PID, as `ServiceRuntime.Read` does.
- `service start`/`restart` must be launched detached and then polled via the state file; do not wait for the CLI to exit. `pair`, `stop`, and config updates use `ProcessRunner` timeouts.
- Mask passwords and `pair` tokens in every log/UI error via `SecretMasker`. When applying a new password, pass it as an extra mask; keep `/auth/connect/<token>` redaction.

## Packaging, versioning, git

- Installer names are `OpenCodeLauncher-<version>-user.exe` and `OpenCodeLauncher-<version>-system.exe` (no `-setup-`). Keep `installer\OpenCodeLauncherCSharp.iss` and the workflow upload globs in sync. `README.md` intentionally does not list these names.
- `VERSION` is the release version. `release.yml` runs on `v*` tags; `build-csharp.yml` and `ci.yml` run on push/PR.
- **Do not ordinary push, force-push, delete remote branches/tags, or delete releases without explicit user approval.** A new release needs a new version (for example `1.0.1`) if `v1.0.0` already exists remotely.
- This machine cannot reach `github.com:443` directly. `origin` fetch is HTTPS (may need the local proxy) and push is SSH via `ssh.github.com:443`; use the existing SSH key.
- PowerShell 5.1 reads `.ps1` without a BOM as ANSI. Keep Chinese helper scripts UTF-8 **with BOM** (for example `UiSmokeTest.ps1`), or ASCII-only (`Build-CSharp.ps1`). Do not rewrite them as BOM-less UTF-8.
