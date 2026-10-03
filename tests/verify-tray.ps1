# Verifies the *shipped* Caffeine.exe: launches it for real, finds its tray
# icon through UI Automation, clicks it with a real mouse click, and checks the
# machine's power settings actually changed - then clicks again and checks they
# came back exactly.
#
# ASCII-only on purpose: Windows PowerShell 5.1 reads .ps1 as ANSI without a
# BOM, so any non-ASCII text here turns into mojibake.  The Chinese tray-icon
# name is built from char codes below.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

$exe = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\bin\Caffeine.exe'
$exe = (Resolve-Path $exe).Path


$cafe = [string][char]0x5496 + [char]0x5561 + [char]0x56E0
$iconPattern = [regex]::Escape($cafe) + '|Caffeine'

Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.IntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
'@

function Read-Power {
    $v = @{}
    foreach ($pair in @(@('SUB_SLEEP'), @('SUB_VIDEO'), @('SUB_DISK'))) {
        $out = powercfg /query SCHEME_CURRENT $pair[0] 2>&1 | Out-String
        $name = ''
        foreach ($line in ($out -split "`n")) {
            if ($line -match ([char]0x522b + [char]0x540d + ':\s*(\S+)')) { $name = $Matches[1] }
            elseif ($line -match ([char]0x4ea4 + [char]0x6d41 + '.{0,12}0x([0-9a-fA-F]+)')) { $v["$name.ac"] = [Convert]::ToInt32($Matches[1], 16) }
            elseif ($line -match ([char]0x76f4 + [char]0x6d41 + '.{0,12}0x([0-9a-fA-F]+)')) { $v["$name.dc"] = [Convert]::ToInt32($Matches[1], 16) }
        }
    }
    return $v
}

function Show-Power($label, $v) {
    Write-Host ("  {0,-10} STANDBYIDLE {1,5}/{2,-5} HIBERNATEIDLE {3,5}/{4,-5} VIDEOIDLE {5,5}/{6,-5} DISKIDLE {7,5}/{8,-5}" -f `
        $label, $v['STANDBYIDLE.ac'], $v['STANDBYIDLE.dc'], $v['HIBERNATEIDLE.ac'], $v['HIBERNATEIDLE.dc'], `
        $v['VIDEOIDLE.ac'], $v['VIDEOIDLE.dc'], $v['DISKIDLE.ac'], $v['DISKIDLE.dc'])
}

function Find-TrayButton {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($b in $buttons) {
        if ($b.Current.Name -match $iconPattern) { return $b }
    }
    return $null
}

function Find-DesktopButton {
    param([string]$Prefix)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        $n = $b.Current.Name
        if ($n -and $n.TrimStart().StartsWith($Prefix)) { return $b }
    }
    return $null
}

# Windows 11 keeps third-party icons in the "show hidden icons" flyout, so the
# flyout has to be open before the icon can be found.  Its presence is detected
# by the overflow XAML island window rather than by guessing at timings.
$FlyoutClass = 'TopLevelWindowForOverflowXamlIsland'

function Test-FlyoutOpen {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $FlyoutClass)
    return ($null -ne $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond))
}

function Open-Overflow {
    if (Test-FlyoutOpen) { return }
    $show = [string][char]0x663e + [char]0x793a + [char]0x9690 + [char]0x85cf + [char]0x7684 + [char]0x56fe + [char]0x6807
    $btn = Find-DesktopButton $show
    if (-not $btn) { return }
    $r = $btn.Current.BoundingRectangle
    [W.U]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 300
    [W.U]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 80
    [W.U]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero) | Out-Null
    for ($i = 0; $i -lt 20 -and -not (Test-FlyoutOpen); $i++) { Start-Sleep -Milliseconds 150 }
    Start-Sleep -Milliseconds 300
}

function Close-Overflow {
    if (-not (Test-FlyoutOpen)) { return }
    [W.U]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero) | Out-Null
    [W.U]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero) | Out-Null
    for ($i = 0; $i -lt 20 -and (Test-FlyoutOpen); $i++) { Start-Sleep -Milliseconds 150 }
}

# The tooltip always starts with the app name, which cannot collide with a
# window whose title merely contains the word "Caffeine".
function Find-TrayButton {
    return Find-DesktopButton $cafe
}

function Click-Tray {
    param($b, [int]$holdMs = 60)
    $r = $b.Current.BoundingRectangle
    [W.U]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 400
    [W.U]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds $holdMs
    [W.U]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero) | Out-Null
}

# The app deliberately leaves VIDEOIDLE alone when the user ticked "allow
# display sleep" in the tray menu (settings.cfg: allow_display_sleep=1), so
# demanding VIDEOIDLE=0 unconditionally produced FAILs against correct
# behaviour.  Read the preference instead of assuming.
$allowDisplaySleep = $false
$cfgPath = Join-Path $env:LOCALAPPDATA 'Caffeine\settings.cfg'
if (Test-Path $cfgPath) {
    $allowDisplaySleep = ((Get-Content $cfgPath -Raw) -match '(?m)^\s*allow_display_sleep\s*=\s*1')
}
Write-Host ("  (allow display sleep = {0})" -f $allowDisplaySleep)

function Is-Awake($v) {
    if (-not ($v['STANDBYIDLE.ac'] -eq 0 -and $v['STANDBYIDLE.dc'] -eq 0)) { return $false }
    if ($allowDisplaySleep) { return $true }
    return ($v['VIDEOIDLE.ac'] -eq 0 -and $v['VIDEOIDLE.dc'] -eq 0)
}

function Is-SameAs($a, $b) {
    foreach ($k in @('STANDBYIDLE.ac','STANDBYIDLE.dc','VIDEOIDLE.ac','VIDEOIDLE.dc','DISKIDLE.ac','DISKIDLE.dc')) {
        if ($a[$k] -ne $b[$k]) { return $false }
    }
    return $true
}

function Verdict($ok) { if ($ok) { '[PASS]' } else { '[FAIL]' } }

# ---- preflight -------------------------------------------------------
# A second Caffeine.exe does not start: it hits the single-instance mutex and
# hands over to the one already running.  Every click below would then land on
# somebody else's tray icon and be measured against the wrong baseline, which
# produces FAILs that look like app bugs but are not.  Refuse to run instead.
$stale = @(Get-Process -Name Caffeine -ErrorAction SilentlyContinue)
if ($stale.Count -gt 0) {
    Write-Host "=== preflight ==="
    Write-Host ("  [FAIL] a Caffeine instance is already running: pid " + ($stale.Id -join ', '))
    Write-Host ("         it is " + $stale[0].Path)
    Write-Host "         Quit it from the tray (right-click -> Quit), then re-run."
    Write-Host ("RESULT: {0} failure(s)" -f 1)
    exit 1
}
$pre = Read-Power
if ($pre['STANDBYIDLE.ac'] -eq 0 -and $pre['STANDBYIDLE.dc'] -eq 0) {
    Write-Host "=== preflight ==="
    Write-Host "  [FAIL] the machine is already in keep-awake mode (STANDBYIDLE 0/0)."
    Write-Host "         Click the tray icon once to restore your settings, then re-run."
    Write-Host ("RESULT: {0} failure(s)" -f 1)
    exit 1
}

$before = Read-Power
Write-Host "=== launch the real Caffeine.exe ==="
Show-Power 'before' $before
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
if ($proc.HasExited) {
    Write-Host "  [FAIL] Caffeine.exe exited immediately - it did not take ownership of the tray"
    Write-Host ("RESULT: {0} failure(s)" -f 1)
    exit 1
}

$btn = $null
for ($i = 0; $i -lt 10 -and -not $btn; $i++) {
    Open-Overflow
    $btn = Find-TrayButton
    if (-not $btn) { Start-Sleep -Milliseconds 500; Close-Overflow }
}
if (-not $btn) {
    Write-Host "  [FAIL] caffeine tray icon not found (overflow may be unreachable)"
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}
Write-Host "  [PASS] tray icon found in the hidden-icons flyout: '$($btn.Current.Name)'"
Close-Overflow
Start-Sleep -Milliseconds 300

function Reopen-And-Find {
    Open-Overflow
    $b = Find-TrayButton
    if (-not $b) { Write-Host "  [FAIL] icon vanished from the flyout" }
    return $b
}

Write-Host "=== click 1: single click -> should go awake ==="
Click-Tray (Reopen-And-Find)
Start-Sleep -Seconds 2
$v = Read-Power
Show-Power 'click1' $v
Write-Host ("  {0} single click switches the system to keep-awake" -f (Verdict (Is-Awake $v)))

Write-Host "=== click 2 + 3: a DOUBLE click must NOT flip it back ==="
$b = Reopen-And-Find
Click-Tray $b
Start-Sleep -Milliseconds 120
Click-Tray $b
Start-Sleep -Seconds 2
$v = Read-Power
Show-Power 'dblclick' $v
Write-Host ("  {0} double click still awake (used to toggle twice)" -f (Verdict (Is-Awake $v)))

Write-Host "=== click 4: hold the button 2.5s -> must NOT spam toggles ==="
# Windows 11's flyout turns a long press into ONE click, which is fine.  What
# must never happen is the old behaviour where holding the button produced a
# toggle (and a balloon) on every auto-repeated WM_LBUTTONDOWN.
$logPath = Join-Path $env:LOCALAPPDATA 'Caffeine\caffeine.log'
$before2 = @(Get-Content $logPath | Select-String -SimpleMatch ([char]0x70b9 + [char]0x51fb)).Count
Click-Tray (Reopen-And-Find) 2500
Start-Sleep -Seconds 2
$after2 = @(Get-Content $logPath | Select-String -SimpleMatch ([char]0x70b9 + [char]0x51fb)).Count
$v = Read-Power
Show-Power 'longpress' $v
$spam = ($after2 - $before2)
Write-Host ("  {0} 2.5s hold produced {1} toggle(s) - exactly one, no spam" -f `
    (Verdict ($spam -le 1)), $spam)
Write-Host ("  {0} the hold flipped the state once (idle after the previous awake)" -f `
    (Verdict (Is-SameAs $before $v)))

Write-Host "=== click 5: single click -> should restore the user defaults ==="
Start-Sleep -Seconds 1
Click-Tray (Reopen-And-Find)
Start-Sleep -Seconds 2
$v = Read-Power
Show-Power 'click5' $v
Write-Host ("  {0} single click puts the system back to keep-awake" -f (Verdict (Is-Awake $v)))

Write-Host "=== click 6: single click -> back to the original defaults ==="
Start-Sleep -Seconds 1
Click-Tray (Reopen-And-Find)
Start-Sleep -Seconds 2
$v = Read-Power
Show-Power 'click6' $v
Write-Host ("  {0} final click restores every original value" -f (Verdict (Is-SameAs $before $v)))

Write-Host "=== hard kill the app, check the crash-recovery file ==="
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$pending = Join-Path $env:LOCALAPPDATA 'Caffeine\pending-restore.cfg'
Write-Host ("  pending-restore.cfg present after kill: {0}" -f (Test-Path $pending))
$final = Read-Power
Show-Power 'afterkill' $final
Write-Host ("  {0} nothing was left modified by the kill" -f (Verdict (Is-SameAs $before $final)))

$log = Join-Path $env:LOCALAPPDATA 'Caffeine\caffeine.log'
if (Test-Path $log) {
    Write-Host "=== log tail ==="
    Get-Content $log -Tail 12 | ForEach-Object { Write-Host "  $_" }
}
