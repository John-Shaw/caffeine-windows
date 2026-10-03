# Builds the self-contained installer:  Caffeine-Setup-<version>.exe
#
#   1. build the app          -> bin\Caffeine.exe
#   2. deflate it into a resource payload
#   3. compile the setup with that payload + version embedded
#
# The result needs nothing on the machine except .NET Framework 4.x, and it is
# a per-user install (no admin, no folder picking by default).
# ASCII-only (Windows PowerShell 5.1 reads .ps1 as ANSI without a BOM).
$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc    = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$objDir = Join-Path $root 'obj'
$dist   = Join-Path $root 'dist'

# ---- 1. the app itself -------------------------------------------------
& (Join-Path $root 'build.ps1')
if (-not (Test-Path (Join-Path $root 'bin\Caffeine.exe'))) { throw 'app build failed' }
$appExe = Join-Path $root 'bin\Caffeine.exe'

# ---- 2. version + payload ---------------------------------------------
$short = (Get-Content (Join-Path $root 'version.txt') -First 1).Trim()
$verFile    = Join-Path $objDir 'version.txt'
$payloadDef = Join-Path $objDir 'payload.deflate'
Copy-Item (Join-Path $root 'version.txt') $verFile -Force

$bytes = [System.IO.File]::ReadAllBytes($appExe)
$raw   = [System.IO.File]::Create($payloadDef)
try {
    $ds = New-Object System.IO.Compression.DeflateStream(
        $raw, [System.IO.Compression.CompressionLevel]::Optimal)
    try { $ds.Write($bytes, 0, $bytes.Length) } finally { $ds.Dispose() }
} finally { $raw.Dispose() }

$payloadSize = (Get-Item $payloadDef).Length
Write-Host ("payload: {0:N0} bytes -> {1:N0} bytes deflated ({2:P0})" -f `
    $bytes.Length, $payloadSize, ($payloadSize / $bytes.Length))

# ---- 3. the installer --------------------------------------------------
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$setup = Join-Path $dist ("Caffeine-Setup-" + $short + ".exe")

$sources = @(
    (Join-Path $root 'setup\SetupProgram.cs'),
    (Join-Path $root 'setup\InstallCore.cs'),
    (Join-Path $root 'setup\Dpi.cs'),
    (Join-Path $root 'setup\InstallUI.cs'),
    (Join-Path $root 'setup\UninstallUI.cs')
)
foreach ($s in $sources) { if (-not (Test-Path $s)) { throw "missing source: $s" } }

$cscArgs = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output',
    '/codepage:65001',
    "/out:$setup",
    "/win32manifest:$(Join-Path $root 'src\app.manifest')",
    "/resource:$payloadDef,CaffeineSetup.payload.deflate",
    "/resource:$verFile,CaffeineSetup.version.txt",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
) + $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "setup compile failed (exit $LASTEXITCODE)" }

# a stable name for "latest", handy for a download link
$latest = Join-Path $dist 'Caffeine-Setup.exe'
Copy-Item $setup $latest -Force

# and the bare app next to it, for people who prefer to run it without installing
Copy-Item $appExe (Join-Path $dist 'Caffeine.exe') -Force

$info = Get-Item $setup
Write-Host ("==> OK: {0}  {1:N0} bytes (payload {2:N0})" -f $info.FullName, $info.Length, $info.Length)
Write-Host "    also: $latest"
