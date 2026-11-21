# Hallow Tasks roadmap

## Done

| Version | What |
|---|---|
| 1.0 | Processes grouped by app (Task Manager style: known apps, same exe, hosted helpers such as conhost / WebView2, helpers in the app's folder; PID reuse checked by creation time; Hallow apps under "Hallow"; categories Apps / Background / Windows / Hallow) from one NtQuerySystemInformation(SystemProcessInformation) call: CPU %, private working set, I/O read / write per second, threads, session, parent; paths via QueryFullProcessImageName; GPU % from the "GPU Engine" counters (engtype_3D summed per PID) and dedicated GPU memory from "GPU Process Memory", read with ReadCategory; TCP connections per process from GetExtendedTcpTable; visible windows from EnumWindows; descriptions, publishers and signatures (embedded Authenticode + catalog) read once per file in the background; power hint; Impact as the default sort; sort / search / filter; a virtualised list updated in place. Details strip and menu: End task / End process tree (confirmation; protected, game, anti-cheat and self refused with a reason), priority (Idle..High), affinity (with a warning), open file location, search online (only on click), Scan with Defender (Guard if it offers a file scan, otherwise MpCmdRun -ScanType 3 -DisableRemediation), copy details. What's slowing my game: game from the known list, Settings' extra exe names or Hallow Hub's playing names; the other groups ranked by their 10-second average CPU / GPU / disk; Below normal / Idle while the game runs (PriorityKeeper puts them back when the game closes, on Restore, on quit, before an update), End task; anti-cheat "leave alone". Performance: CPU (per core option), GPU 3D / encode / decode / copy, memory, GPU memory, disk, network, 60 s graphs; no CPU temperature (needs a driver). Startup apps: Run keys and Startup folders, StartupApproved state, Toolkit's impact estimate, Disable / Restore for HKCU Run via a backup key. Family link (ping, status with top CPU / GPU users, open, menu, invoke, quit, --cmd, status.json), Hub tray hosting, Hub notify, accent, crash reports, updater; optional Ctrl+Alt+T (off by default). Samples only while the window is open and not minimized |

## Later

| Idea | Why it isn't in 1.0 |
|---|---|
| Light mode | Coming family-wide. Every colour is already a brush in Theme.xaml, so it's a second dictionary. |
| Network bytes per process | Needs admin: ETW (Microsoft-Windows-Kernel-Network) or switching on `GetPerTcpConnectionEStats` collection. 1.0 counts connections per app and shows the PC's total speed. |
| Real disk bytes per process | Task Manager uses ETW for its Disk column. 1.0 uses Windows' I/O counters (file and device I/O). |
| CPU temperature | Needs a kernel driver (same as Hallow Overlay). |
| Services tab, users tab | Starting / stopping services needs admin. |
| Ctrl+Shift+Esc | Belongs to Windows' Task Manager; replacing it needs the Image File Execution Options "Debugger" hook (admin, and fragile). 1.0 offers Ctrl+Alt+T, off by default. |
| Suspend / resume a process | Easy to leave an app frozen by mistake; games' anti-cheat may react to suspended helpers. |
| Remember "always lower this app while I play" | A per-app rule is Hallow Toolkit's job (per-game rules); 1.0 lowers for one session only. |
| Hallow Guard file scan over its pipe | Guard 1.8 has no `scan file` command yet; Tasks already asks for one first and falls back to Defender's MpCmdRun. |
