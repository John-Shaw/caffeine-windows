# Builds Caffeine.exe with the C# compiler that ships inside Windows
# (no Visual Studio / .NET SDK required).  Target: .NET Framework 4.x, which
# every Windows 10/11 machine already has.
#
# NOTE: keep this file ASCII-only - Windows PowerShell 5.1 reads .ps1 files
# as ANSI unless they carry a BOM, so non-ASCII text here turns into mojibake.
$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc    = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outDir = Join-Path $root 'bin'
$exe    = Join-Path $outDir 'Caffeine.exe'

if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# stamp the version (version.txt -> obj/AppVersion.g.cs) before compiling
& (Join-Path $root 'gen-version.ps1') -Root $root

$sources = @(
    (Join-Path $root 'src\Program.cs'),
    (Join-Path $root 'src\AppPaths.cs'),
    (Join-Path $root 'src\PowerKeeper.cs'),
    (Join-Path $root 'src\Settings.cs'),
    (Join-Path $root 'src\TrayContext.cs'),
    (Join-Path $root 'src\SelfTest.cs'),
    (Join-Path $root 'obj\AppVersion.g.cs')
) | Where-Object { Test-Path $_ }

foreach ($missing in @('AppPaths.cs','PowerKeeper.cs','Settings.cs','TrayContext.cs','SelfTest.cs','Program.cs')) {
    if (-not (Test-Path (Join-Path $root "src\$missing"))) { throw "missing source: $missing" }
}

$cscArgs = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output',
    # sources are UTF-8; without this csc reads them as the system ANSI code
    # page and mangles every Chinese string literal
    '/codepage:65001',
    "/out:$exe",
    "/win32icon:$(Join-Path $root 'assets\caffeine_full.ico')",
    "/win32manifest:$(Join-Path $root 'src\app.manifest')",
    "/resource:$(Join-Path $root 'assets\caffeine_empty.ico'),Caffeine.empty.ico",
    "/resource:$(Join-Path $root 'assets\caffeine_full.ico'),Caffeine.full.ico",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
) + $sources

Write-Host "==> csc $($sources.Count) source files -> $exe"
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed (exit $LASTEXITCODE)" }

$info = Get-Item $exe
Write-Host ("==> OK: {0}  {1:N0} bytes" -f $info.FullName, $info.Length)
