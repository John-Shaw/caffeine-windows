# Compiles and runs the test harness (tests\Harness.cs + the real sources).
# ASCII-only on purpose: Windows PowerShell 5.1 reads .ps1 as ANSI without a BOM.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin  = Join-Path $root 'bin'
$exe  = Join-Path $bin 'Caffeine.Tests.exe'

# The harness drives the real TrayContext against the real data folder
# (%LOCALAPPDATA%\Caffeine) -- the same settings.cfg and the same
# pending-restore.cfg an installed copy uses.  Two preconditions, both checked
# before anything is compiled or touched:
#   1. no Caffeine may be running, or the two processes fight over
#      pending-restore.cfg and every "file exists / file gone" assertion lies;
#   2. no pending-restore.cfg may be left over, or the machine is still pinned to
#      "never sleep" and the tests would snapshot that as the user's defaults.
# Refuse loudly instead of producing 13 confusing failures.
$running = Get-Process -Name Caffeine -ErrorAction SilentlyContinue
if ($running) {
    Write-Host ("Caffeine is running (PID {0})." -f ($running.Id -join ', '))
    Write-Host "The test harness shares %LOCALAPPDATA%\Caffeine with a real install"
    Write-Host "and has to own pending-restore.cfg by itself."
    Write-Host "Quit Caffeine from its tray menu, then run this again."
    exit 1
}
$pending = Join-Path $env:LOCALAPPDATA 'Caffeine\pending-restore.cfg'
if (Test-Path $pending) {
    Write-Host ("A pending-restore.cfg is sitting at {0}." -f $pending)
    Write-Host "The machine is still in the 'never sleep' state Caffeine left behind;"
    Write-Host "the tests would snapshot that as your defaults."
    Write-Host "Run 'Caffeine.exe --restore' first, then run this again."
    exit 1
}

# csc does not create the output directory - it fails with CS1567 if bin\ is
# missing.  Do not rely on build.ps1 having run first: CONTRIBUTING tells
# contributors to run this script on a fresh clone, and CI used to hide the
# dependency by building before testing.
New-Item -ItemType Directory -Force -Path $bin | Out-Null

# Program.cs asks for AppVersion, which the build generates from version.txt
& (Join-Path $root 'gen-version.ps1') -Root $root

$sources = @(
    "$root\src\Program.cs",
    "$root\src\AppPaths.cs",
    "$root\src\PowerKeeper.cs",
    "$root\src\Settings.cs",
    "$root\src\TrayContext.cs",
    "$root\src\SelfTest.cs",
    "$root\obj\AppVersion.g.cs",
    "$root\tests\Harness.cs"
) | Where-Object { Test-Path $_ }
# Program.cs has its own Main; the harness needs the only entry point.
$defines = '/main:Caffeine.Harness'

$cscArgs = @(
    '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/utf8output',
    '/codepage:65001', $defines, "/out:$exe",
    "/resource:$root\assets\caffeine_empty.ico,Caffeine.empty.ico",
    "/resource:$root\assets\caffeine_full.ico,Caffeine.full.ico",
    '/reference:System.dll', '/reference:System.Core.dll',
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll'
) + $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "harness compile failed (exit $LASTEXITCODE)" }

& $exe
exit $LASTEXITCODE
