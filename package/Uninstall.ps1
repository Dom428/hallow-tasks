# Hallow Tasks uninstaller (run from Windows Settings > Apps, or directly).
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow Tasks'
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.MessageBox]::Show("Uninstall Hallow Tasks?", 'Uninstall Hallow Tasks', 'YesNo', 'Question') -ne 'Yes') { return }
# quit through the family pipe first, so apps it lowered for a game get their priority back
if (Get-Process HallowTasks -ErrorAction SilentlyContinue) { try { & (Join-Path $dest "HallowTasks.exe") --cmd quit | Out-Null; Start-Sleep -Milliseconds 1500 } catch { } }
Get-Process HallowTasks -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'HallowTasks' -ErrorAction SilentlyContinue
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) { Remove-Item (Join-Path $dir 'Hallow Tasks.lnk') -ErrorAction SilentlyContinue }
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowTasks' -Recurse -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'HallowTasks') -Recurse -Force -ErrorAction SilentlyContinue
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$dest`"" -WindowStyle Hidden
[System.Windows.Forms.MessageBox]::Show("Hallow Tasks is uninstalled.`n`nStartup entries you disabled with it stay in its backup (HKCU\Software\HallowTasks) - reinstall Hallow Tasks to restore them.", 'Hallow Tasks') | Out-Null
