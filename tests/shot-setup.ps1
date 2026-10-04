# Screenshot the installer window without installing anything.
# Launch the setup with --no-launch, grab the window, then close it.
# ASCII-only.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$root  = Split-Path -Parent $PSScriptRoot
$setup = Join-Path $root 'dist\Caffeine-Setup.exe'
$out   = Join-Path $root 'assets\setup_step1.png'
$stage = if ($args.Count -ge 2) { $args[1] } else { '1' }

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
    if (-not $w) { Write-Host '  no setup window'; return $false }
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

$dir = Join-Path $env:TEMP 'CaffeineShotTest'
$install = [string][char]0x5B89 + [char]0x88C5
$done    = [string][char]0x5B8C + [char]0x6210
$cancel  = [string][char]0x53D6 + [char]0x6D88

$p = Start-Process -FilePath $setup -ArgumentList @('--dir', $dir, '--no-autostart', '--no-launch') -PassThru
Start-Sleep -Seconds 3
Shot $out | Out-Null
if ($stage -eq '3') {
    if (ClickByPrefix $install) {
        Start-Sleep -Seconds 4
        Shot (Join-Path $root 'assets\setup_step3.png') | Out-Null
        ClickByPrefix $done | Out-Null
    }
}
Start-Sleep -Seconds 1
if (-not $p.HasExited) { ClickByPrefix $cancel | Out-Null }
Start-Sleep -Seconds 1
if (-not $p.HasExited) { try { $p.Kill() } catch [Exception] { } }
Write-Host '  done'
