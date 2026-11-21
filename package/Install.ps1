# Hallow Tasks installer: copies the app to your user folder (no admin needed) and adds Start menu + Desktop shortcuts.
# It never ends or changes a process by itself: everything happens when you click it in the window.
#   Install.ps1 [-NoStart]   (-NoStart: don't open the app afterwards)
param([switch]$NoStart)
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow Tasks'
New-Item -ItemType Directory -Force $dest | Out-Null
$wasRunning = [bool](Get-Process HallowTasks -ErrorAction SilentlyContinue)
if ($wasRunning) { try { $old = Join-Path $dest "HallowTasks.exe"; if (Test-Path $old) { & $old --cmd quit | Out-Null; Start-Sleep -Milliseconds 1500 } } catch { } }   # lowered priorities go back first
Get-Process HallowTasks -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Copy-Item (Join-Path $PSScriptRoot 'HallowTasks.exe') $dest -Force
foreach ($f in 'README.txt', 'Uninstall.ps1', 'FontAwesome-LICENSE.txt') { Copy-Item (Join-Path $PSScriptRoot $f) $dest -Force -ErrorAction SilentlyContinue }
$exe = Join-Path $dest 'HallowTasks.exe'
# Windows Settings > Apps entry (per user, no admin)
$u = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowTasks'
New-Item $u -Force | Out-Null
$ver = (Get-Item $exe).VersionInfo.FileVersion -replace '\.0\.0$', ''
$vals = @{ DisplayName = 'Hallow Tasks'; DisplayVersion = $ver; Publisher = 'Hallow'; DisplayIcon = $exe; InstallLocation = $dest
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$(Join-Path $dest 'Uninstall.ps1')`""
    URLInfoAbout = 'https://github.com/Dom428/hallow-tasks' }
foreach ($k in $vals.Keys) { Set-ItemProperty $u -Name $k -Value $vals[$k] }
Set-ItemProperty $u -Name NoModify -Value 1 -Type DWord; Set-ItemProperty $u -Name NoRepair -Value 1 -Type DWord
Set-ItemProperty $u -Name EstimatedSize -Value ([int]((Get-Item $exe).Length / 1KB)) -Type DWord
$w = New-Object -ComObject WScript.Shell
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {
    $s = $w.CreateShortcut((Join-Path $dir 'Hallow Tasks.lnk'))
    $s.TargetPath = $exe; $s.WorkingDirectory = $dest; $s.Description = 'A better Task Manager, Hallow style'; $s.Save()
}
if ($NoStart) { Write-Host "Hallow Tasks is installed. It's in your Start menu and on your Desktop."; return }
# after an update it comes back the way it was (in the tray if it was running); a fresh install opens the window
if ($wasRunning) { Start-Process $exe -ArgumentList '--tray' } else { Start-Process $exe }
Write-Host "Hallow Tasks is installed. It's in your Start menu and on your Desktop."
