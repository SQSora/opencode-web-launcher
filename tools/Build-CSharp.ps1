#requires -Version 5.1
<#
    Build the C# WPF launcher with the .NET Framework compiler that ships with Windows.
    No Visual Studio, MSBuild, .NET SDK or NuGet is required.

    Usage:
        powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-CSharp.ps1
        powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-CSharp.ps1 -RunTests
#>

param(
    [switch]$RunTests,
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root 'src'
$out = Join-Path $root 'build\csharp'
$appOut = Join-Path $out 'app'
$zip = Join-Path $root 'build\OpenCodeLauncher-csharp.zip'
$exe = Join-Path $out 'OpenCodeLauncher.exe'

if (-not $Version) {
    $versionFile = Join-Path $root 'VERSION'
    if (Test-Path -LiteralPath $versionFile) {
        $Version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    }
}
if (-not $Version) { $Version = '0.0.0' }

if (-not (Test-Path -LiteralPath $src)) { throw "Source directory not found: $src" }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "C# compiler not found: $csc" }
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

function Find-GacAssembly([string]$name) {
    $base = Join-Path $env:WINDIR ('Microsoft.NET\assembly\GAC_MSIL\' + $name)
    if (Test-Path -LiteralPath $base) {
        $hit = Get-ChildItem -LiteralPath $base -Recurse -Filter ($name + '.dll') -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    $fallback = Join-Path $fx ($name + '.dll')
    if (Test-Path -LiteralPath $fallback) { return $fallback }
    throw "Assembly not found: $name"
}

$references = @(
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Find-GacAssembly 'WindowsBase'),
    (Find-GacAssembly 'System.Xaml'),
    (Join-Path $fx 'System.dll'),
    (Join-Path $fx 'System.Core.dll'),
    (Join-Path $fx 'System.Xml.dll'),
    (Join-Path $fx 'System.Drawing.dll'),
    (Join-Path $fx 'System.Windows.Forms.dll'),
    (Join-Path $fx 'System.Web.Extensions.dll'),
    (Join-Path $fx 'System.Web.dll'),
    (Join-Path $fx 'Microsoft.CSharp.dll'),
    (Join-Path $root 'app\lib\Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $root 'app\lib\Microsoft.Web.WebView2.Wpf.dll')
)

foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Missing reference: $reference" }
}

if (Test-Path -LiteralPath $out) {
    Remove-Item -LiteralPath $out -Recurse -Force
}
New-Item -ItemType Directory -Path $appOut -Force | Out-Null

$sources = @(Get-ChildItem -LiteralPath $src -Recurse -Filter '*.cs' -File | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "No C# sources found in $src" }

$compilerArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/utf8output',
    '/codepage:65001',
    ('/out:' + $exe),
    ('/win32icon:' + (Join-Path $root 'app\assets\opencode.ico'))
)
foreach ($reference in $references) { $compilerArgs += ('/r:' + $reference) }
$compilerArgs += $sources

Write-Host 'Compiling C# launcher...'
& $csc $compilerArgs
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $exe)) { throw "Compiler did not produce $exe" }

Copy-Item -Path (Join-Path $root 'app\*.xaml') -Destination $appOut -Force
Copy-Item -LiteralPath (Join-Path $root 'app\assets') -Destination $appOut -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'app\lib') -Destination $appOut -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $out 'README.md') -Force
foreach ($name in @('LICENSE', 'VERSION')) {
    $source = Join-Path $root $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $out -Force }
}
[System.IO.File]::WriteAllText(
    (Join-Path $out 'BUILD.txt'),
    ('Version: ' + $Version + "`r`nBuilt: " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "`r`nCompiler: " + $csc + "`r`n"),
    (New-Object System.Text.UTF8Encoding($false))
)

if ($RunTests) {
    Write-Host 'Running self-test...'
    $process = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Self-test failed with exit code $($process.ExitCode)" }
    Write-Host 'Self-test passed.'
}

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force

Write-Host ''
Write-Host 'C# build complete:'
Write-Host "  EXE: $exe"
Write-Host "  ZIP: $zip"
