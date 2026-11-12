param(
    [string]$ExePath = '',
    [switch]$TestRestart
)

# UI smoke test for the C# launcher. It uses UI Automation and needs an interactive desktop.
# WARNING: -TestRestart restarts the OpenCode service, which disconnects any running OpenCode session.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

if (-not $ExePath) {
    $ExePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'build\csharp\OpenCodeLauncher.exe'
}
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$app = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 9

function Get-TopWindows([int]$processId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $condition)
}

function Find-Button([string]$name) {
    $windows = Get-TopWindows $app.Id
    foreach ($window in $windows) {
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)
        $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($element) { return $element }
    }
    return $null
}

function Invoke-Button([string]$name) {
    $button = Find-Button $name
    if (-not $button) { throw "Button not found: $name" }
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Write-Output ("clicked: " + $name)
    Start-Sleep -Milliseconds 700
}

try {
    if ($TestRestart) {
        Invoke-Button '重启'
        Start-Sleep -Seconds 10
    }

    Invoke-Button '访问地址'
    $close = Find-Button '✕'
    if ($close) {
        $pattern = $close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        Write-Output 'clicked: access popup close'
    } else {
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Write-Output 'access popup closed with ESC'
    }
    Start-Sleep -Milliseconds 500

    Invoke-Button '隐藏页面'
    Start-Sleep -Seconds 1
    Invoke-Button '显示页面'
    Start-Sleep -Seconds 1

    Invoke-Button '日志'
    Invoke-Button '收起'

    Invoke-Button '设置'
    Start-Sleep -Seconds 1
    Invoke-Button '保存'
    Start-Sleep -Seconds 1
    Invoke-Button '设置'
    Start-Sleep -Milliseconds 700
    Invoke-Button '取消'
    Start-Sleep -Milliseconds 500

    Invoke-Button '最小化'
    Start-Sleep -Milliseconds 500
    Start-Process -FilePath $exe | Out-Null
    Start-Sleep -Seconds 2
    if (-not (Find-Button '设置')) { throw 'window was not restored from the tray' }
    Write-Output 'window restored from tray'

    $main = $null
    foreach ($window in Get-TopWindows $app.Id) {
        if ($window.Current.Name -like 'OpenCode Launcher*') { $main = $window; break }
    }
    if (-not $main) { throw 'main window not found for close-to-tray test' }
    $windowPattern = $main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $windowPattern.Close()
    Start-Sleep -Seconds 1
    if (-not (Get-Process -Id $app.Id -ErrorAction SilentlyContinue)) {
        throw 'process exited on X close instead of going to tray'
    }
    Start-Process -FilePath $exe | Out-Null
    Start-Sleep -Seconds 2
    if (-not (Find-Button '设置')) { throw 'window was not restored after X close' }
    Write-Output 'window restored after close-to-tray'
    Write-Output 'UI smoke test passed'
}
catch {
    Write-Output ('UI smoke test failed: ' + $_.Exception.Message)
}
finally {
    Start-Sleep -Milliseconds 500
    & $exe -Exit
    Start-Sleep -Seconds 3
    $alive = Get-Process -Id $app.Id -ErrorAction SilentlyContinue
    Write-Output ('app alive after -Exit: ' + [bool]$alive)
    if ($alive) { Stop-Process -Id $app.Id -Force }
}
