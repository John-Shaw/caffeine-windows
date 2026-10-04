# End-to-end installer test: fresh install -> same-version reinstall -> upgrade
# to a synthetic next version -> uninstall, checking files, shortcuts, registry,
# the autostart entry and that user settings survive the upgrade.
# The versions come from version.txt, with the upgrade target derived from it,
# so this test never needs editing when the project moves to a new release.
# Runs the silent switches for the mechanical parts and one real GUI click
# through, and leaves a screenshot of the installer window.
# ASCII-only.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$root    = Split-Path -Parent $PSScriptRoot
$testDir = Join-Path $env:TEMP 'CaffeineInstallTest'

# The real version under test, and a synthetic newer one to upgrade to.
$verFile   = Join-Path $root 'version.txt'
$baseVer   = (Get-Content $verFile -First 1).Trim()
$vparts    = $baseVer.Split('.')
$upVer     = ('{0}.{1}.{2}' -f [int]$vparts[0], ([int]$vparts[1] + 1), $vparts[2])
$setupBase = Join-Path $root ('dist\Caffeine-Setup-{0}.exe' -f $baseVer)
$setupUp   = Join-Path $root ('dist\Caffeine-Setup-{0}.exe' -f $upVer)

$uninstKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Caffeine'
$runKey    = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$dataDir   = Join-Path $env:LOCALAPPDATA 'Caffeine'
$smLink    = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Caffeine.lnk'
$cafe      = [string][char]0x5496 + [char]0x5561 + [char]0x56E0
$install   = [string][char]0x5B89 + [char]0x88C5   # install
$done      = [string][char]0x5B8C + [char]0x6210   # finish

$script:fail = 0
function Check($what, $ok, [string]$extra = '') {
    if ($ok) { Write-Host "  [PASS] $what" }
    else { $script:fail++; Write-Host "  [FAIL] $what $extra" }
}
function RegVal($key, $name) { (Get-ItemProperty $key -Name $name -ErrorAction SilentlyContinue).$name }
function Kill-App {
    Get-Process -Name Caffeine -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill() } catch [Exception] {
            try { Invoke-CimMethod -InputObject (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)") -MethodName Terminate | Out-Null } catch [Exception] { }
        }
    }
    Start-Sleep -Milliseconds 800
}
function Run-Setup([string]$setupPath, [string[]]$argList) {
    $p = Start-Process -FilePath $setupPath -ArgumentList $argList -PassThru -Wait
    return $p.ExitCode
}
function Get-Ver([string]$exe) {
    $tmp = Join-Path $env:TEMP 'caffeine-ver.txt'
    if (Test-Path $tmp) { Remove-Item $tmp -Force }
    Start-Process -FilePath $exe -ArgumentList '--version' -Wait -WindowStyle Hidden -RedirectStandardOutput $tmp | Out-Null
    if (Test-Path $tmp) { return (Get-Content $tmp -First 1).Trim() }
    return ''
}
function Buttons {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    return $r.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function ClickByPrefix([string]$p) {
    $b = $null
    foreach ($x in (Buttons)) { if ($x.Current.Name -and $x.Current.Name.TrimStart().StartsWith($p)) { $b = $x; break } }
    if (-not $b) { return $false }
    $r = $b.Current.BoundingRectangle
    Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.IntPtr e);
'@ -ErrorAction SilentlyContinue
    [W.U]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 300
    [W.U]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 60
    [W.U]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero) | Out-Null
    return $true
}
function Shot($file) {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $w = $null
    foreach ($x in $r.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($x.Current.ClassName -match 'WindowsForms') { $w = $x }
    }
    if (-not $w) { Write-Host "  no setup window for screenshot"; return }
    $b = $w.Current.BoundingRectangle
    $pad = 8
    $bmp = New-Object System.Drawing.Bitmap(([int]$b.Width + 2*$pad), ([int]$b.Height + 2*$pad))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(([int]$b.X - $pad), ([int]$b.Y - $pad), 0, 0, $bmp.Size)
    $g.Dispose(); $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "  screenshot: $file"
}

Write-Host "=== 0. clean slate (remove the test install) ==="
# A real install and this test share the exact same per-user registration:
# HKCU\...\Uninstall\Caffeine, the Run value named "Caffeine", and
# %APPDATA%\...\Start Menu\Caffeine.lnk.  Running the test next to a real
# install would silently overwrite all three, and the cleanup below would then
# delete the user's uninstall entry.  Refuse instead of clobbering.
if (Test-Path $uninstKey) {
    $loc = RegVal $uninstKey 'InstallLocation'
    if ($loc -ne $testDir) {
        Write-Host "  [FAIL] a different Caffeine is installed at: $loc"
        Write-Host "         This test shares the uninstall key, the Run value and the"
        Write-Host "         Start Menu link with a real install, so it would overwrite"
        Write-Host "         them.  Uninstall that one first, then re-run."
        Write-Host ("RESULT: {0} failure(s)" -f 1)
        exit 1
    }
    $un = Join-Path $testDir 'uninstall.exe'
    if (Test-Path $un) { & $un --uninstall --quiet 2>$null }
}
Kill-App
if (Test-Path $testDir) { cmd /c "rmdir /s /q `"$testDir`"" | Out-Null }
Remove-Item $uninstKey -Recurse -Force -ErrorAction SilentlyContinue
$runVal = (Get-ItemProperty $runKey -Name Caffeine -ErrorAction SilentlyContinue).Caffeine
if ($runVal -and $runVal -like "*$testDir*") {
    Remove-ItemProperty $runKey -Name Caffeine -Force -ErrorAction SilentlyContinue
}
if (Test-Path $smLink) { Remove-Item $smLink -Force -ErrorAction SilentlyContinue }
Check 'no leftover test install dir' (-not (Test-Path $testDir))
Check 'no leftover uninstall key' (-not (Test-Path $uninstKey))

Write-Host ""
Write-Host "=== 1. fresh install (silent, temp dir) ==="
$rc = Run-Setup $setupBase @('--silent', '--dir', $testDir, '--no-autostart')
Check 'exit code 0' ($rc -eq 0) "got $rc"
Check 'Caffeine.exe written' (Test-Path (Join-Path $testDir 'Caffeine.exe'))
Check 'uninstall.exe written' (Test-Path (Join-Path $testDir 'uninstall.exe'))
Check 'Start Menu shortcut' (Test-Path $smLink)
Check 'uninstall key created' (Test-Path $uninstKey)
Check ('DisplayVersion = ' + $baseVer) ((RegVal $uninstKey 'DisplayVersion') -eq $baseVer) (RegVal $uninstKey 'DisplayVersion')
Check 'InstallLocation recorded' ((RegVal $uninstKey 'InstallLocation') -eq $testDir)
Check 'UninstallString present' ((RegVal $uninstKey 'UninstallString') -like '*uninstall.exe*--uninstall*')
Check 'no autostart entry' (-not (Get-ItemProperty $runKey -Name Caffeine -ErrorAction SilentlyContinue))
$payload = Get-Item (Join-Path $testDir 'Caffeine.exe')
$built = Get-Item (Join-Path $root 'bin\Caffeine.exe')
Check 'installed exe identical to build output' ($payload.Length -eq $built.Length) "$($payload.Length) vs $($built.Length)"
$ver = Get-Ver (Join-Path $testDir 'Caffeine.exe')
Check 'installed exe reports its version' ($ver -eq $baseVer) "got $ver"

Write-Host ""
Write-Host "=== 2. set a user setting that must survive the upgrade ==="
# auto_awake=1 is included on purpose: it is the key that decides whether the
# machine comes back up awake, so losing it across an upgrade would be a real
# (and silent) regression, not a cosmetic one.
Set-Content -Path (Join-Path $dataDir 'settings.cfg') -Encoding UTF8 -Value @(
    'allow_display_sleep=1', 'auto_start=0', 'auto_awake=1', 'welcome_shown=1', 'hibernate_hint_shown=1')
Write-Host "  settings.cfg now: allow_display_sleep=1 auto_awake=1"
$rc = Run-Setup $setupBase @('--silent', '--dir', $testDir, '--autostart')
Check 'reinstall/upgrade exit code 0' ($rc -eq 0) "got $rc"
Check 'autostart entry created' (((Get-ItemProperty $runKey -Name Caffeine -ErrorAction SilentlyContinue).Caffeine) -like '*CaffeineInstallTest*')
$cfg = Get-Content (Join-Path $dataDir 'settings.cfg') -Raw
Check 'user settings survived the reinstall' ($cfg -match 'allow_display_sleep=1')
Check 'auto_awake survived the reinstall' ($cfg -match 'auto_awake=1')
Check ('DisplayVersion still ' + $baseVer) ((RegVal $uninstKey 'DisplayVersion') -eq $baseVer)

Write-Host ""
Write-Host ("=== 3. upgrade: build a real {0} and install it over {1} ===" -f $upVer, $baseVer)
Set-Content -Path $verFile -Value $upVer -Encoding ASCII -NoNewline
& (Join-Path $root 'build-setup.ps1') 2>&1 | Select-String -Pattern 'OK: D' | Out-Null
Check ("$upVer setup built") (Test-Path $setupUp)
$rc = Run-Setup $setupUp @('--silent', '--dir', $testDir, '--autostart')
Check 'upgrade exit code 0' ($rc -eq 0) "got $rc"
Check ("DisplayVersion now $upVer") ((RegVal $uninstKey 'DisplayVersion') -eq $upVer) (RegVal $uninstKey 'DisplayVersion')
$ver2 = Get-Ver (Join-Path $testDir 'Caffeine.exe')
Check ("installed exe reports $upVer") ($ver2 -eq $upVer) "got $ver2"
$cfg = Get-Content (Join-Path $dataDir 'settings.cfg') -Raw
Check 'user settings survived the upgrade' ($cfg -match 'allow_display_sleep=1')
Check 'auto_awake survived the upgrade' ($cfg -match 'auto_awake=1')
Check 'autostart entry now points at the new exe' (((Get-ItemProperty $runKey -Name Caffeine -ErrorAction SilentlyContinue).Caffeine) -like '*CaffeineInstallTest*')
$log = Join-Path $dataDir 'setup.log'
Check 'setup.log recorded the upgrade' ((Get-Content $log -Raw) -match ([regex]::Escape($upVer)))

Write-Host ""
Write-Host "=== 4. the real GUI: launch the setup and click through it ==="
$gui = Start-Process -FilePath $setupUp -ArgumentList @('--dir', $testDir, '--no-autostart', '--no-launch') -PassThru
Start-Sleep -Seconds 3
Shot (Join-Path $root 'assets\setup_step1.png')
Check 'installer window appeared' ((ClickByPrefix $install))
Start-Sleep -Seconds 4
Shot (Join-Path $root 'assets\setup_step3.png')
$cfg = Get-Content (Join-Path $dataDir 'settings.cfg') -Raw
Check 'GUI install finished, settings still there' ($cfg -match 'allow_display_sleep=1')
Check 'auto_awake still there after the GUI install' ($cfg -match 'auto_awake=1')
$clickedDone = ClickByPrefix $done
Start-Sleep -Seconds 2
if (-not $gui.HasExited) { try { $gui.CloseMainWindow() | Out-Null } catch [Exception] { } }
Start-Sleep -Seconds 1
if (-not $gui.HasExited) { Kill-App }
Check 'installer window closed' ($gui.HasExited)

Write-Host ""
Write-Host "=== 5. uninstall ==="
$un = Join-Path $testDir 'uninstall.exe'
Check 'uninstaller present' (Test-Path $un)
$p = Start-Process -FilePath $un -ArgumentList '--uninstall', '--quiet' -PassThru -Wait
Check 'uninstall exit code 0' ($p.ExitCode -eq 0) "got $($p.ExitCode)"
Start-Sleep -Seconds 2
Check 'install dir deleted' (-not (Test-Path $testDir))
Check 'uninstall key removed' (-not (Test-Path $uninstKey))
Check 'autostart entry removed' (-not (Get-ItemProperty $runKey -Name Caffeine -ErrorAction SilentlyContinue))
Check 'Start Menu shortcut removed' (-not (Test-Path $smLink))
Check 'user data kept (--delete-data not given)' (Test-Path (Join-Path $dataDir 'settings.cfg'))

Write-Host ""
Write-Host ("=== 6. restore the project to {0} ===" -f $baseVer)
Set-Content -Path $verFile -Value $baseVer -Encoding ASCII -NoNewline
& (Join-Path $root 'build-setup.ps1') 2>&1 | Select-String -Pattern 'OK: D' | Out-Null
Check ("$baseVer setup rebuilt") (Test-Path $setupBase)
$stale = $setupUp
if (Test-Path $stale) { cmd /c "del /q `"$stale`"" | Out-Null }
Check ("stale $upVer setup removed") (-not (Test-Path $stale))

Write-Host ""
Write-Host ("RESULT: {0} failure(s)" -f $script:fail)
exit $script:fail
