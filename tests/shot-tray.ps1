# Screenshots the real tray icon in Windows' "show hidden icons" flyout, once
# idle and once awake, so the new art can be judged at actual tray size on
# both a light and a dark taskbar.  Leaves the machine exactly as it found it.
# ASCII-only.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$root  = 'D:\CodeBase\Caffeine'
$exe   = (Resolve-Path (Join-Path $root 'bin\Caffeine.exe')).Path
$outD  = Join-Path $root 'assets'
$cafe  = [string][char]0x5496 + [char]0x5561 + [char]0x56E0

Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.IntPtr e);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
'@ -ErrorAction SilentlyContinue

$FlyoutClass = 'TopLevelWindowForOverflowXamlIsland'

function Test-FlyoutOpen {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $FlyoutClass)
    return ($null -ne $r.FindFirst([System.Windows.Automation.TreeScope]::Children, $c))
}
function Get-FlyoutRect {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $FlyoutClass)
    $w = $r.FindFirst([System.Windows.Automation.TreeScope]::Children, $c)
    if ($w) { return $w.Current.BoundingRectangle }
    return $null
}
function Find-Btn([string]$Prefix) {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $r.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
        if ($b.Current.Name -and $b.Current.Name.TrimStart().StartsWith($Prefix)) { return $b }
    }
    return $null
}
function Open-Overflow {
    if (Test-FlyoutOpen) { return }
    $show = [string][char]0x663E + [char]0x793A + [char]0x9690 + [char]0x85CF + [char]0x7684 + [char]0x56FE + [char]0x6807
    $b = Find-Btn $show
    if (-not $b) { return }
    $r = $b.Current.BoundingRectangle
    [W.U]::SetCursorPos([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2)) | Out-Null
    Start-Sleep -Milliseconds 300
    [W.U]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 80
    [W.U]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero) | Out-Null
    for ($i=0; $i -lt 20 -and -not (Test-FlyoutOpen); $i++) { Start-Sleep -Milliseconds 150 }
    Start-Sleep -Milliseconds 400
}
function Close-Overflow {
    if (-not (Test-FlyoutOpen)) { return }
    [W.U]::keybd_event(0x1B,0,0,[UIntPtr]::Zero) | Out-Null
    [W.U]::keybd_event(0x1B,0,2,[UIntPtr]::Zero) | Out-Null
    for ($i=0; $i -lt 20 -and (Test-FlyoutOpen); $i++) { Start-Sleep -Milliseconds 150 }
}
function Shot($file) {
    $b = Get-FlyoutRect
    if (-not $b) { Write-Host "  no flyout for $file"; return }
    $pad = 10
    $bmp = New-Object System.Drawing.Bitmap(([int]$b.Width + 2*$pad), ([int]$b.Height + 2*$pad))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(([int]$b.X - $pad), ([int]$b.Y - $pad), 0, 0, $bmp.Size)
    $g.Dispose(); $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host ("  wrote {0}  ({1}x{2})" -f $file, [int]$b.Width, [int]$b.Height)
}
function Click-Tray {
    $b = Find-Btn $cafe
    if (-not $b) { Write-Host '  tray icon not found'; return $false }
    $r = $b.Current.BoundingRectangle
    [W.U]::SetCursorPos([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2)) | Out-Null
    Start-Sleep -Milliseconds 300
    [W.U]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 60
    [W.U]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero) | Out-Null
    return $true
}
function Read-Power {
    $v = @{}
    foreach ($pair in @(@('SUB_SLEEP'), @('SUB_VIDEO'), @('SUB_DISK'))) {
        $out = powercfg /query SCHEME_CURRENT $pair[0] 2>&1 | Out-String
        $name = ''
        foreach ($line in ($out -split "`n")) {
            if ($line -match ([char]0x522B + [char]0x540D + ':\s*(\S+)')) { $name = $Matches[1] }
            elseif ($line -match ([char]0x4EA4 + [char]0x6D41 + '.{0,12}0x([0-9a-fA-F]+)')) { $v["$name.ac"] = [Convert]::ToInt32($Matches[1],16) }
            elseif ($line -match ([char]0x76F4 + [char]0x6D41 + '.{0,12}0x([0-9a-fA-F]+)')) { $v["$name.dc"] = [Convert]::ToInt32($Matches[1],16) }
        }
    }
    return $v
}

Write-Host '=== launch ==='
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3

Write-Host '=== idle: open the flyout and shoot ==='
Open-Overflow
Shot (Join-Path $outD 'tray_idle.png')
Close-Overflow
Start-Sleep -Milliseconds 400

Write-Host '=== click once -> awake, shoot again ==='
Open-Overflow
Click-Tray | Out-Null
Start-Sleep -Seconds 2
$w = Read-Power
Write-Host ("  STANDBYIDLE now {0}/{1}  VIDEOIDLE {2}/{3}" -f $w['STANDBYIDLE.ac'],$w['STANDBYIDLE.dc'],$w['VIDEOIDLE.ac'],$w['VIDEOIDLE.dc'])
Shot (Join-Path $outD 'tray_awake.png')
Close-Overflow
Start-Sleep -Milliseconds 400

Write-Host '=== click again -> restore, then quit ==='
Open-Overflow
Click-Tray | Out-Null
Start-Sleep -Seconds 2
Close-Overflow
$r = Read-Power
Write-Host ("  STANDBYIDLE back to {0}/{1}" -f $r['STANDBYIDLE.ac'], $r['STANDBYIDLE.dc'])
if (-not $proc.HasExited) { try { $proc.Kill() } catch [Exception] { } }
Start-Sleep -Seconds 1
Write-Host '  done'
