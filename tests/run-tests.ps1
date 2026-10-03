# Compiles and runs the test harness (tests\Harness.cs + the real sources).
# ASCII-only on purpose: Windows PowerShell 5.1 reads .ps1 as ANSI without a BOM.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe  = Join-Path $root 'bin\Caffeine.Tests.exe'

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
