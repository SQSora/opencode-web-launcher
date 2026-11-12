#requires -Version 5.1
<#
    更新 app\lib 下的 WebView2 SDK 文件。
    默认下载 NuGet 上最新的稳定版本，也可以传入 -Version 指定版本。
#>

param(
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 在旧系统上默认可能不启用 TLS 1.2，NuGet 会直接拒绝连接
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
} catch { }

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$lib = Join-Path $root 'app\lib'
New-Item -ItemType Directory -Path $lib -Force | Out-Null

if (-not $Version) {
    Write-Host '正在查询 WebView2 SDK 最新版本…'
    $index = Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/index.json'
    $Version = ($index.versions | Where-Object { $_ -notmatch '-' } | Select-Object -Last 1)
}

Write-Host "目标版本: $Version"

$work = Join-Path $env:TEMP ('opencode-launcher-webview2-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null

$nupkg = Join-Path $work 'webview2.nupkg'
$zip   = Join-Path $work 'webview2.zip'
$url   = "https://globalcdn.nuget.org/packages/microsoft.web.webview2.$Version.nupkg"

try {
    Write-Host "下载: $url"
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source -L --retry 3 --retry-delay 2 --connect-timeout 20 -o $nupkg $url
        if ($LASTEXITCODE -ne 0) { throw "curl 下载失败（退出码 $LASTEXITCODE）" }
    } else {
        Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
    }

    Copy-Item -LiteralPath $nupkg -Destination $zip -Force
    $pkg = Join-Path $work 'pkg'
    Expand-Archive -LiteralPath $zip -DestinationPath $pkg -Force

    Copy-Item -LiteralPath (Join-Path $pkg 'lib\net462\Microsoft.Web.WebView2.Core.dll') -Destination $lib -Force
    Copy-Item -LiteralPath (Join-Path $pkg 'lib\net462\Microsoft.Web.WebView2.Wpf.dll') -Destination $lib -Force
    Copy-Item -LiteralPath (Join-Path $pkg 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $lib -Force

    $license = Join-Path $pkg 'LICENSE.txt'
    if (Test-Path -LiteralPath $license) {
        Copy-Item -LiteralPath $license -Destination (Join-Path $lib 'LICENSE-WebView2.txt') -Force
    }

    Set-Content -LiteralPath (Join-Path $lib 'VERSION.txt') -Value $Version -Encoding ASCII
    Write-Host "已更新到 $Version。请重新启动 Launcher 使新版本生效。"
    try { Remove-Item -LiteralPath $work -Recurse -Force } catch { Write-Host "临时文件保留在：$work（可手动删除）" }
} catch {
    Write-Host "更新失败，临时文件保留在：$work（可手动删除）"
    throw
}
