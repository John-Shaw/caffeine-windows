# This is the exact thing the Microsoft Store requires of an EXE
# submission, so it is worth testing for real rather than trusting the
# build log.  The runner is a disposable VM, so installing onto it is
# safe - and it doubles as a check that --silent is a real switch and
# that the embedded payload unpacks.
$setup = (Get-ChildItem .\dist\Caffeine-Setup-*.exe | Select-Object -First 1)
if (-not $setup) { throw 'installer was not produced' }
Write-Host ("installer: " + $setup.Name + "  " + $setup.Length + " bytes")

# Must be Start-Process -Wait, NOT `& $setup ... ; $LASTEXITCODE`.
# The installer is compiled /target:winexe, and PowerShell does not
# wait for GUI programs and does not update $LASTEXITCODE for them -
# it just keeps whatever the previous native command left behind
# ($null in a fresh session, so `-ne 0` is true and the step dies).
# It also means a Test-Path straight after `&` races the extraction.
# Measured: `&` on a 3s WinExe returns in 27ms with the stale code.
# --no-launch must stay: -Wait blocks on the whole process tree, so
# letting it launch Caffeine.exe here would hang the step forever.
$installArgs = @('--silent', '--no-launch')
$p = Start-Process -FilePath $setup.FullName -ArgumentList $installArgs -PassThru -Wait
if ($p.ExitCode -ne 0) { throw "silent install exited $($p.ExitCode)" }

$dir = Join-Path $env:LOCALAPPDATA 'Programs\Caffeine'
$exe = Join-Path $dir 'Caffeine.exe'
if (-not (Test-Path $exe)) { throw "silent install did not write $exe" }
Write-Host ("installed: " + $exe + "  " + (Get-Item $exe).Length + " bytes")

$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Caffeine'
if (-not (Test-Path $key)) { throw 'uninstall key was not registered' }
Write-Host ("DisplayVersion: " + (Get-ItemProperty $key).DisplayVersion)

# Uninstall via the installed uninstall.exe, which is what the Start
# Menu entry points at and what tests\verify-install.ps1 covers.
# (The setup binary can also self-uninstall, but that hands itself off
# to a temp copy and is a different path - not the one to gate CI on.)
$un = Join-Path $dir 'uninstall.exe'
if (-not (Test-Path $un)) { throw "silent install did not write $un" }
$p = Start-Process -FilePath $un -ArgumentList '--uninstall', '--quiet' -PassThru -Wait
if ($p.ExitCode -ne 0) { throw "uninstall exited $($p.ExitCode)" }
if (Test-Path $key) { throw 'uninstall key survived --uninstall --quiet' }
Write-Host 'silent install + uninstall OK'
