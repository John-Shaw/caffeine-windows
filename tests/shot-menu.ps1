# Screenshots the Caffeine tray context menu of the already-running app.
# Opens the menu, shoots it, closes it again.  No setting is toggled, nothing
# is started or stopped.  Used with tools\probe_tray.py to measure the row
# pitch instead of eyeballing it.
#
# Handles both tray layouts: an icon pinned to the visible notification area and
# one sitting in the "show hidden icons" flyout (which is where a build running
# from bin\ lands, because Windows pins per exe path).  ASCII-only.
param(
    [string]$out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\menu.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$cafe = [string][char]0x5496 + [char]0x5561 + [char]0x56E0
$show = [string][char]0x663E + [char]0x793A + [char]0x9690 + [char]0x85CF + [char]0x7684 + [char]0x56FE + [char]0x6807
$flyout = 'TopLevelWindowForOverflowXamlIsland'

Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.IntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, System.UIntPtr e);
'@ -ErrorAction SilentlyContinue

function Buttons {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
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
function Get-MenuWin {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children,
                                [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($w.Current.ClassName -match 'WindowsForms|DropDown') {
            $r = $w.Current.BoundingRectangle
            if ($r.Width -gt 100 -and $r.Height -gt 100) { return $w }
        }
    }
    return $null
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

[W.U]::SetCursorPos(200, 200) | Out-Null
Escape; Escape

# ---- find the tray icon, opening the overflow flyout if needed ----------
$icon = $null
for ($i = 0; $i -lt 8 -and -not $icon; $i++) {
    $icon = FindPrefix (Buttons) $cafe
    if (-not $icon) {
        if (-not (FlyoutOpen)) {
            $b = FindPrefix (Buttons) $show
            if ($b) { Click $b 0x0002 0x0004 }
            for ($k = 0; $k -lt 20 -and -not (FlyoutOpen); $k++) { Start-Sleep -Milliseconds 150 }
        }
        Start-Sleep -Milliseconds 400
        $icon = FindPrefix (Buttons) $cafe
    }
    if (-not $icon) { Escape }
}
if (-not $icon) { Write-Host '  Caffeine tray button not found'; exit 1 }

$r = $icon.Current.BoundingRectangle
Write-Host ("  caffeine button at x={0} y={1} {2}x{3}" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)

Click $icon 0x0008 0x0010            # right click = open the menu
Start-Sleep -Seconds 2

$m = Get-MenuWin
if (-not $m) { Write-Host '  menu window not found'; exit 1 }

$r = $m.Current.BoundingRectangle
Write-Host ("  menu {0}x{1} at x={2} y={3}" -f [int]$r.Width, [int]$r.Height, [int]$r.X, [int]$r.Y)

$pad = 8
$bmp = New-Object System.Drawing.Bitmap(([int]$r.Width + 2 * $pad), ([int]$r.Height + 2 * $pad))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(([int]$r.X - $pad), ([int]$r.Y - $pad), 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("  wrote {0}" -f $out)

Escape; Escape
Write-Host '  done (menu closed, nothing toggled)'
