# Screenshot the uninstall dialog.  Installs to a temp dir first, opens the
# uninstaller GUI, grabs the window, then clicks uninstall and waits for the
# real work to finish.  ASCII-only.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$root  = 'D:\CodeBase\Caffeine'
$setup = Join-Path $root 'dist\Caffeine-Setup.exe'
$out   = Join-Path $root 'assets\uninstall.png'
$dir   = Join-Path $env:TEMP 'CaffeineUninstallShot'

Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.IntPtr e);
'@ -ErrorAction SilentlyContinue

function Shot($file) {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $w = $null
    foreach ($x in $r.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($x.Current.ClassName -match 'WindowsForms') { $w = $x }
    }
    if (-not $w) { Write-Host '  no window'; return $false }
    $b = $w.Current.BoundingRectangle
    $pad = 8
    $bmp = New-Object System.Drawing.Bitmap(([int]$b.Width + 2*$pad), ([int]$b.Height + 2*$pad))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(([int]$b.X - $pad), ([int]$b.Y - $pad), 0, 0, $bmp.Size)
    $g.Dispose(); $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host ("  screenshot {0}  window {1}x{2}" -f $file, [int]$b.Width, [int]$b.Height)
    return $true
}

function ClickByPrefix([string]$p) {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    foreach ($x in $r.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
        if ($x.Current.Name -and $x.Current.Name.TrimStart().StartsWith($p)) {
            $b = $x.Current.BoundingRectangle
            [W.U]::SetCursorPos([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2)) | Out-Null
            Start-Sleep -Milliseconds 300
            [W.U]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero) | Out-Null
            Start-Sleep -Milliseconds 60
            [W.U]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero) | Out-Null
            return $true
        }
    }
    return $false
}

$uninstall = [string][char]0x5378 + [char]0x8F7D
$ok = [string][char]0x786E + [char]0x5B9A

Write-Host '  installing into a temp dir first...'
Start-Process -FilePath $setup -ArgumentList @('--silent', '--dir', $dir, '--no-autostart') -Wait | Out-Null

Write-Host '  opening the uninstaller GUI...'
$p = Start-Process -FilePath (Join-Path $dir 'uninstall.exe') -ArgumentList @('--uninstall') -PassThru
Start-Sleep -Seconds 3
Shot $out | Out-Null
if (ClickByPrefix $uninstall) {
    Start-Sleep -Seconds 2
    # a "已卸载" message box may be up; dismiss it
    $null = ClickByPrefix $ok
    Start-Sleep -Seconds 2
}
Start-Sleep -Seconds 2
if (-not $p.HasExited) { try { $p.Kill() } catch [Exception] { } }
Start-Sleep -Seconds 2
if (Test-Path (Join-Path $dir 'uninstall.exe')) {
    & (Join-Path $dir 'uninstall.exe') --uninstall --quiet
    Start-Sleep -Seconds 2
}
if (Test-Path $dir) { cmd /c "rmdir /s /q `"$dir`"" | Out-Null }
Write-Host ("  temp dir gone: " + (-not (Test-Path $dir)))
Write-Host ("  uninstall key gone: " + (-not (Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Caffeine')))
