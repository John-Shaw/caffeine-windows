# Toggles the two checkable menu options a few times, screenshots the menu after
# each state, and verifies the setting really landed on disk.  ASCII-only.
# Usage: capture-menu2.ps1 [outPrefix]
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$prefix = if ($args.Count -ge 1) { $args[0] } else { "$env:TEMP\menu" }

$cafe = [string][char]0x5496 + [char]0x5561 + [char]0x56E0
$show = [string][char]0x663e + [char]0x793a + [char]0x9690 + [char]0x85cf + [char]0x7684 + [char]0x56fe + [char]0x6807
$flyout = 'TopLevelWindowForOverflowXamlIsland'
# menu item prefixes we care about
$itemDisplay = [string][char]0x5141 + [char]0x8BB8 + [char]0x663E + [char]0x793A + [char]0x5668
$itemAuto = [string][char]0x5F00 + [char]0x673A + [char]0x81EA + [char]0x52A8 + [char]0x542F + [char]0x52A8
$settingsFile = Join-Path $env:LOCALAPPDATA 'Caffeine\settings.cfg'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.IntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
'@

function Buttons {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    return $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function Items {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)
    return $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function FindPrefix($list, [string]$p) {
    foreach ($e in $list) { if ($e.Current.Name -and $e.Current.Name.TrimStart().StartsWith($p)) { return $e } }
    return $null
}
function FlyoutOpen {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $flyout)
    return ($null -ne $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $c))
}
function Click($e, [uint32]$down, [uint32]$up) {
    $r = $e.Current.BoundingRectangle
    [W.U]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 300
    [W.U]::mouse_event($down, 0, 0, 0, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 60
    [W.U]::mouse_event($up, 0, 0, 0, [IntPtr]::Zero) | Out-Null
}
function Escape {
    [W.U]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero) | Out-Null
    [W.U]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 400
}
function Shot($file) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $menu = $null
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($w.Current.ClassName -match 'WindowsForms|DropDown') {
            $r = $w.Current.BoundingRectangle
            if ($r.Width -gt 100 -and $r.Height -gt 100) { $menu = $w }
        }
    }
    if (-not $menu) { Write-Host "  no menu window"; return $false }
    $r = $menu.Current.BoundingRectangle
    $pad = 8
    $bmp = New-Object System.Drawing.Bitmap(([int]$r.Width + 2 * $pad), ([int]$r.Height + 2 * $pad))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(([int]$r.X - $pad), ([int]$r.Y - $pad), 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  saved $file"
    return $true
}
function Get-AppState {
    $s = @{}
    if (Test-Path $settingsFile) {
        foreach ($l in Get-Content $settingsFile) {
            $p = $l.Split('=')
            if ($p.Length -ge 2) { $s[$p[0].Trim()] = $p[1].Trim() }
        }
    }
    $s['runkey'] = [string](Get-ItemProperty $runKey -Name Caffeine -ErrorAction SilentlyContinue).Caffeine
    return $s
}

$exe = (Resolve-Path 'D:\CodeBase\Caffeine\bin\Caffeine.exe').Path
# Kill any instance left over from a previous run, but let it put the power
# settings back first: a bare force-kill while it is keeping the machine awake
# would leave STANDBYIDLE at 0/0 for whoever runs this next.
$old = @(Get-Process -Name Caffeine -ErrorAction SilentlyContinue)
if ($old.Count -gt 0) {
    Write-Host ("  stopping {0} leftover instance(s) and restoring power settings" -f $old.Count)
    & $exe '--restore-quiet'
    $old | ForEach-Object { try { $_.Kill() } catch [Exception] { } }
    Start-Sleep -Seconds 1
}
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
if ($proc.HasExited) {
    Write-Host "  [FAIL] Caffeine.exe exited immediately - it did not take ownership of the tray"
    exit 1
}
[W.U]::SetCursorPos(200, 200) | Out-Null
Escape; Escape

function Open-Menu {
    $icon = $null
    for ($i = 0; $i -lt 8 -and -not $icon; $i++) {
        if (-not (FlyoutOpen)) {
            $b = FindPrefix (Buttons) $show
            if ($b) { Click $b 0x0002 0x0004 }
            for ($k = 0; $k -lt 20 -and -not (FlyoutOpen); $k++) { Start-Sleep -Milliseconds 150 }
        }
        Start-Sleep -Milliseconds 400
        $c = FindPrefix (Buttons) $cafe
        if ($c) {
            $r = $c.Current.BoundingRectangle
            if ($r.Width -gt 0 -and $r.Y -gt 0 -and $r.Y -lt 2060) { $icon = $c }
        }
        if (-not $icon) { Escape }
    }
    if (-not $icon) { throw 'tray icon not reachable' }
    Click $icon 0x0008 0x0010
    Start-Sleep -Seconds 2
}

function Verdict($ok) { if ($ok) { '[PASS]' } else { '[FAIL]' } }
$script:fail = 0
function Check($what, $ok, [string]$extra = '') {
    if ($ok) { Write-Host ("  {0} {1}" -f (Verdict $ok), $what) }
    else { $script:fail++; Write-Host ("  {0} {1} {2}" -f (Verdict $ok), $what, $extra) }
}
function Flip([string]$v) { if ($v -eq '1') { '0' } else { '1' } }

# The starting state is whatever the user happens to have, so the assertions
# below are written relative to it and the whole thing is put back at the end.
# This test used to print a hard-coded "[PASS]" next to a boolean and never
# check anything, and it used to leave the Run key pointing at bin\Caffeine.exe
# instead of the real install.
$s0 = Get-AppState
Write-Host ("  starting state: allow_display_sleep=" + $s0['allow_display_sleep'] +
            " auto_start=" + $s0['auto_start'] + " runkey=" + $s0['runkey'])
$wantDisplay = Flip $s0['allow_display_sleep']
$wantAuto = Flip $s0['auto_start']

Write-Host "=== state 0 (screenshot) ==="
Open-Menu
Shot "$prefix-off.png" | Out-Null
Escape

Write-Host "=== click the two checkable items once ==="
foreach ($item in @($itemDisplay, $itemAuto)) {
    Open-Menu
    $mi = FindPrefix (Items) $item
    if (-not $mi) { Check "menu item not found: $item" $false; Escape; continue }
    Click $mi 0x0002 0x0004
    Start-Sleep -Seconds 1
    Escape
}
$st = Get-AppState
Write-Host ("  cfg after 2 clicks: " + (($st.GetEnumerator() | Sort-Object Name | ForEach-Object { $_.Name + '=' + $_.Value }) -join ' '))
Check "allow_display_sleep flipped to $wantDisplay" ($st['allow_display_sleep'] -eq $wantDisplay) ("got " + $st['allow_display_sleep'])
Check "auto_start flipped to $wantAuto" ($st['auto_start'] -eq $wantAuto) ("got " + $st['auto_start'])
if ($wantAuto -eq '1') {
    Check 'Run key now present' ([bool]$st['runkey'])
} else {
    Check 'Run key now removed' (-not $st['runkey']) ("got " + $st['runkey'])
}

Write-Host "=== state 1 (screenshot) ==="
Open-Menu
Shot "$prefix-on.png" | Out-Null
Escape

Write-Host "=== click them again -> should go back off ==="
foreach ($item in @($itemDisplay, $itemAuto)) {
    Open-Menu
    $mi = FindPrefix (Items) $item
    if ($mi) { Click $mi 0x0002 0x0004; Start-Sleep -Seconds 1; Escape }
}
$st = Get-AppState
Write-Host ("  cfg after 4 clicks: " + (($st.GetEnumerator() | Sort-Object Name | ForEach-Object { $_.Name + '=' + $_.Value }) -join ' '))
Check 'allow_display_sleep back to the starting value' ($st['allow_display_sleep'] -eq $s0['allow_display_sleep']) ("got " + $st['allow_display_sleep'])
Check 'auto_start back to the starting value' ($st['auto_start'] -eq $s0['auto_start']) ("got " + $st['auto_start'])
# The app writes its OWN path into the Run key, so when this test runs the
# build in bin\ the key legitimately points at bin\Caffeine.exe rather than at
# wherever it was before.  Assert the real invariant, not the old value.
Check 'Run key points at the running executable' ($st['runkey'] -like "*$exe*") ("got " + $st['runkey'])

Write-Host "=== restore whatever the user had before this test ==="
if ($s0['runkey']) {
    New-ItemProperty -Path $runKey -Name Caffeine -Value $s0['runkey'] -PropertyType String -Force | Out-Null
} else {
    Remove-ItemProperty $runKey -Name Caffeine -ErrorAction SilentlyContinue
}
$end = Get-AppState
Check 'Run key restored' ($end['runkey'] -eq $s0['runkey']) ("got " + $end['runkey'])
Check 'allow_display_sleep restored' ($end['allow_display_sleep'] -eq $s0['allow_display_sleep'])
Check 'auto_start restored' ($end['auto_start'] -eq $s0['auto_start'])

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Host ("RESULT: {0} failure(s)" -f $script:fail)
exit $script:fail
