# Screenshots the taskbar notification area (the always-visible tray strip) of
# the already-running Caffeine.  Nothing is started or stopped.
# ASCII-only.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$outD = 'D:\CodeBase\Caffeine\assets'
$cafe = [string][char]0x5496 + [char]0x5561 + [char]0x56E0

Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.IntPtr e);
'@ -ErrorAction SilentlyContinue

function Get-TaskbarRect {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')
    $w = $r.FindFirst([System.Windows.Automation.TreeScope]::Children, $c)
    if ($w) { return $w.Current.BoundingRectangle }
    return $null
}
function Get-CaffeineBtn {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $r.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
        if ($b.Current.Name -and $b.Current.Name.TrimStart().StartsWith($cafe)) { return $b }
    }
    return $null
}
function Click($e) {
    $r = $e.Current.BoundingRectangle
    [W.U]::SetCursorPos([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2)) | Out-Null
    Start-Sleep -Milliseconds 350
    [W.U]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 60
    [W.U]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero) | Out-Null
}
# crop to the right-hand end of the taskbar so the tray icons are big
function Shot($file) {
    $t = Get-TaskbarRect
    if (-not $t) { Write-Host '  no taskbar'; return }
    $w = [int]$t.Width
    $x = [int]$t.X + [int]($w * 0.62)
    $cw = $w - [int]($w * 0.62)
    $pad = 6
    $bmp = New-Object System.Drawing.Bitmap(($cw + 2*$pad), ([int]$t.Height + 2*$pad))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(($x - $pad), ([int]$t.Y - $pad), 0, 0, $bmp.Size)
    $g.Dispose(); $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host ("  wrote {0}  ({1}x{2})" -f $file, $bmp.Width, $bmp.Height)
}

$b = Get-CaffeineBtn
if (-not $b) { Write-Host '  Caffeine tray button not found'; exit 1 }
$br = $b.Current.BoundingRectangle
Write-Host ("  caffeine button at x={0} y={1} {2}x{3}" -f [int]$br.X, [int]$br.Y, [int]$br.Width, [int]$br.Height)
Write-Host ("  taskbar: " + (Get-TaskbarRect).ToString())

Shot (Join-Path $outD 'taskbar_idle.png')
Click $b
Start-Sleep -Seconds 2
Shot (Join-Path $outD 'taskbar_awake.png')
Click $b
Start-Sleep -Seconds 2
Shot (Join-Path $outD 'taskbar_back_to_idle.png')
Write-Host '  done (left idle)'
