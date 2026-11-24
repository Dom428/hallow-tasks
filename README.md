# Hallow Tasks

**A better Task Manager, Hallow style.**

Part of the Hallow family of Windows apps. Hallow Tasks shows what's using your PC, grouped by app, and - while you play -
**what's slowing your game**, with one click to lower it, put it in **Efficiency mode** or end it. **History** shows what spiked
your PC and when, with the stutter moments of your game. It never touches the game by itself and never touches anti-cheat, never
ends anything without asking, and needs **no admin** for normal use. Light and dark mode. 1.3: **Close game...** for a game that
hangs on exit. 1.4: **Not responding** apps tagged with a one-click **Close**, **Startup apps** switched exactly like Task Manager
does it, and **Game boost** - free up the PC before or while you play, with everything put back after the game.
1.5: **closing a game is instant now** - one confirm, then it ends right away.

![Processes grouped by app, sorted by Impact (made-up data)](docs/processes.png)

## What it does

- **Processes, grouped by app** like Task Manager: Discord, Google Chrome, Steam, OBS... with their child processes inside
  (double-click or the arrow), and every Hallow app under **Hallow**. Helpers of one app (same exe, `conhost`, WebView2, programs in
  the app's own folder) go with it; Windows' own processes are never merged into whoever started them.
  Columns: **CPU %**, **memory** (private working set, like Task Manager), **GPU %** (Windows' "GPU Engine" counters, `engtype_3D`,
  summed per process), **GPU memory** (dedicated), **disk** read + write, **network** (open TCP connections - see the limits below),
  a **power usage hint**, **publisher** (the Authenticode signer, with a tick when the signature is valid; Windows' catalog-signed
  files count) and **path**. Cells are tinted by how busy they are.
- **Sort, search and filter**: the default order is **Impact** = CPU % + 0.8 x GPU % + disk (2 points per MB/s, at most 30) +
  memory (2 points per GB, at most 10) - an estimate of what's slowing your PC. Click a column to sort by it (twice: the other way,
  three times: back to Impact). Search by name, description, publisher, path or PID (Ctrl+F). Filters: All, Apps, Background,
  Windows, Hallow.
- **Details for each process** (click a row, or right-click for the menu): **End task** (a whole app group, or one process) and
  **End process tree**, always with a confirmation (Cancel has the focus). **Set priority** (Idle to High - never Realtime) and
  **set affinity** (with a warning). **Open file location**, **Search online** (opens your browser with a web search, only when you
  click), **Scan with Defender** (through Hallow Guard when it offers a file scan; otherwise Defender's own `MpCmdRun -Scan
  -ScanType 3 -File ... -DisableRemediation`, the engine Guard uses), **Copy details**.
  Windows' core processes (csrss, lsass, svchost, dwm, explorer...), session-0 programs in the Windows folder, your game and
  anti-cheat are **refused with a clear message** (priority, affinity and Efficiency mode stay off for games and anti-cheat).
- **Close game...** (1.3; **instant since 1.5**) - for a game that hangs on exit, or when you just want it gone now. Only on a
  process tagged **Game** (never anti-cheat), in the right-click menu and the details strip, and only when you click it. One
  confirm - *"End Delta Force now? Unsaved progress is lost."* - then **Close now** ends the game and its own helpers (in its
  install folder, or its crash / web helpers) right away with `TerminateProcess`. No 10-second wait. Anti-cheat (ACE / SGuard,
  Easy Anti-Cheat, BattlEye, Vanguard...), Steam, Battle.net and other launchers, Windows and Hallow are never touched. Tick
  **Don't ask again** to skip the confirm next time (Settings > Games > **Confirm before closing** turns it back on). Prefer the
  1.3 way? Settings > Games > **When closing a game**: **End it right away** (default) or **Ask it to close first** - Windows asks
  the game's windows to close (`WM_CLOSE`), a 10-second countdown, then **Force close** only if you click it.
  If Windows refuses (anti-cheat protects the game), it says so at once and plainly - *"Delta Force's anti-cheat (ACE) protects
  the game, so Windows won't let any app end it"* - and lists what you can do: the Stop button in your Steam library, wait a
  minute (ACE often finishes its shutdown), or sign out / restart. It never tries to get around the protection: no admin, no debug
  privilege, no driver, nothing done to anti-cheat. Nothing is automatic. Tasks' log gets one line: ended right away, closed
  nicely, force closed or denied.

  ![Close game: the confirm (made-up data)](docs/close-game.png) ![Close game: Windows refuses because anti-cheat protects the game (made-up data)](docs/close-game-denied.png)

- **Not responding** (1.4): any app whose window Windows calls "Not responding" (`IsHungAppWindow` - it hasn't read its messages
  for 5 s) gets a red **Not responding** tag in Processes, and **Close (not responding)...** in the details strip and the menu. Since
  1.5 it's one confirm, then the app and its own helpers end right away (it's hung - it can't answer a close request anyway). Never a game (games keep **Close game...**), anti-cheat, a launcher (Steam,
  Battle.net... - closing one can take your game with it), Windows or a Hallow app. An optional **quiet note** through Hallow Hub
  after 15 s (Settings, on by default) - held while you play, and given after the game if the app is still stuck. The window title
  is read without sending the window a message, so a hung app can never stall Tasks' sampling.

  ![A Not responding app (made-up data)](docs/processes-hung.png) ![Close a Not responding app: one confirm, then it ends right away (made-up data)](docs/close-app.png)

- **Game boost** (1.4): before or while you play, a list of what's using the **CPU, RAM, GPU or network** right now (never your
  game, anti-cheat, Windows, Hallow apps or the apps you opt out with **Skip**). Per app: **Below normal**, **Efficiency mode**, or
  **Close** (1.5: ends it right away after one confirm - or, with Settings' **Ask it to close first**, asks nicely and shows **Force
  close** after 10 s), and a **reopen after** tick; or tick several and
  **Close selected**. Launchers can be lowered but never closed. **After the game** - or when you click **Restore now** -
  priorities and Efficiency go back and the apps you ticked start again from their own program file with the same command line
  (read with `NtQueryInformationProcess`, no admin), started normally by you. Quitting Tasks puts priorities and Efficiency back.
  **Nothing is automatic** unless you **Save as Boost profile** and tick **Apply it when a game starts** - and even then closing only
  asks nicely; Force close is always your click (a profile applied by itself never ends anything).

  ![Game boost with a few apps boosted and a profile saved (made-up data)](docs/boost-active.png)

- **What's slowing my game**: when a game runs - DeltaForceClient-Win64-Shipping.exe, Discovery-d.exe, the exe names you add in
  Settings, or whatever Hallow Hub says you're playing - a panel ranks the other apps by the CPU, GPU and disk they took over the
  last 10 seconds. Each row: **Below normal** or **Idle** for as long as the game runs (put back when the game closes, when you click
  Restore, or when you quit Hallow Tasks - also on update and uninstall), or **End task** (with confirmation).
  The game itself is never listed; **anti-cheat** (SGuard / ACE, EasyAntiCheat, BattlEye, Vanguard, FACEIT, nProtect, XIGNCODE,
  PunkBuster and similar) is listed as **"Anti-cheat - leave alone"** with no buttons, and Windows' own processes as
  "Windows - leave alone".

  ![What's slowing my game (made-up data)](docs/game.png)
- **Performance**: live graphs of the last 60 seconds - CPU (or **one per core**), GPU (3D, video encode, video decode, copy),
  memory, GPU memory, disk (read / write, active time) and network (send / receive, all adapters) - in the family console style.
  **CPU temperature isn't shown**: Windows only gives it to programs with a kernel driver, and Hallow Tasks (like Hallow Overlay)
  doesn't install one.

  ![Performance (made-up data)](docs/performance.png)
- **History - "what spiked my PC"**: a rolling hour of every app's CPU, GPU, RAM and disk in memory (a point a second while the
  window is open, every 5 s in the tray - Settings can turn the tray part off), plus an optional **24-hour log** on disk
  (`%APPDATA%\HallowTasks\history-24h.jsonl`, one line a minute with that minute's peaks and busiest 8 apps, capped at about
  1 MB; off by default). A **timeline** of the PC's CPU and GPU with the game's time shaded, and a list of **what spiked**: apps
  that took 15%+ CPU, 20%+ GPU or 20+ MB/s of disk, when, for how long and whether you were playing (your game itself isn't
  listed - it's meant to be busy). While a game runs it marks the **stutter moments**: from **Hallow Overlay**'s live numbers
  when Overlay runs (its status `live` - a sudden drop of the 1% low below half its usual value, or a `frameSpikes` counter if
  Overlay offers one), otherwise from CPU / GPU peaks (the PC's CPU at 90%+, or another app suddenly taking 3x what it did the
  minute before). Each one says what else was busy right then.

  ![History (made-up data)](docs/history.png)
- **Efficiency mode**, the same thing **Windows 11 Task Manager's Efficiency mode** does: Windows' EcoQoS
  (`SetProcessInformation` `ProcessPowerThrottling`, execution speed - it runs on the slower, cooler cores at lower clocks) plus
  Idle priority. No admin needed for your own apps. One click in the game panel, the details strip or the right-click menu; it
  stays until you turn it off or the app closes (like Windows'), and turning it off puts its priority back. **Refused** for
  Windows' own processes, your game and anti-cheat.
- **Services**: every Windows service with its status, startup type (Automatic / delayed / trigger / Manual / Disabled), PID
  and description; search and filter (Running, Not Microsoft). **Start / Stop / Restart** where Windows lets you; otherwise the
  button says **(admin)** and starts a tiny elevated helper (`HallowTasks.exe --svc stop "<name>"`) - **Windows asks you (UAC)
  first**. Microsoft's own services are marked **"Windows - leave alone"** (no buttons) unless you switch that off in Settings;
  anti-cheat's services always are.

  ![Services (made-up data)](docs/services.png)
- **Process tree view** (the **Tree** chip on Processes): every process under the one that started it (a "parent" that
  started later is a reused PID, so that process is a root), with **End tree**.
- **Quiet alerts while you play** (off by default): when a background app takes more than X% CPU or GPU (default 25%) for more
  than N seconds (default 20), a note through **Hallow Hub**'s notification - never a pop-up of Tasks' own over the game. At
  most one per app every 10 minutes; without Hub they only appear in History.
- **Precise network (needs admin)**, optional: Settings starts a small helper - Hallow Tasks itself with `--netetw` -
  **elevated after Windows asks you (UAC)**. It runs one real-time ETW session on Microsoft-Windows-Kernel-Network (TCP / UDP
  sent and received, IPv4 and IPv6) and reads only each event's process id and byte count - never addresses or contents - and
  sends per-process bytes per second over a local pipe only you can open. The Network column then shows send + receive speed.
  It stops when you switch it off or close Hallow Tasks, and never starts by itself.
- **Light and dark mode** (the Hallow family spec): follows `"mode"` in `%APPDATA%\Hallow\theme.json` (dark / light / "Same as
  Windows") live, with **Settings > Look: Follow Hallow / Light / Dark** for Tasks alone. Light uses the family palette; the
  accent is darkened for text until it reads at 4.5:1, status text uses the text-safe green / amber / red, and Windows' title bar
  follows.

  ![Light mode (made-up data)](docs/processes-light.png)
- **Startup apps** with an impact estimate: the Run keys (yours and all users'), the Startup folders and (1.4) **your scheduled
  tasks that run at sign-in** where Windows lets you read them (Windows' own `\Microsoft` tasks left out). Impact uses the RAM and
  CPU time per hour each app uses now, Windows' own **StartupApproved** switch (on, or **off since** a date) and, 1.4, the **time
  Windows measured at boot** (its Diagnostics-Performance log, read-only - often shown only to administrators, then "-").
  **Disable / Enable** (1.4) work **exactly like Task Manager**: Windows' `StartupApproved` value for the entry (`02` = on,
  `03` + the time = off) for **your own** entries (HKCU Run and your Startup folder) - reversible, nothing deleted or moved.
  Entries for all users are shown with a button to Windows' **Startup apps** page (they need admin); scheduled tasks open Task
  Scheduler. Entries 1.1-1.3 moved to their backup key can still be **restored**.

  ![Startup apps (made-up data)](docs/startup.png)
- **Hallow family**: the family pipe (`ping`, `status` with the top CPU / GPU users, `quick` (1.2: the same fields at once, from a
  cached sample never more than 5 s old - for Hallow Remote and Hub), `open [processes|game|boost|performance|startup|
  settings|history|services]`, `menu`, `invoke <id>`, `quit`), `HallowTasks.exe --cmd "<line>"`, `%APPDATA%\HallowTasks\status.json`, Hallow Hub's
  tray menu (Hub 2.13+ lists any app answering `menu`; Tasks hides its own icon only when Hub lists it), the Hallow accent colour
  from `%APPDATA%\Hallow\theme.json`, crash reports kept on this PC, and the GitHub updater (checks the download's SHA-256). Status carries `busyApps` (1.2: how many apps
  use at least `busyPercent` = 5% of the CPU or GPU - a count, no names). While Hub or Remote runs, or someone asked `quick` in the last
  2 minutes, status.json is rewritten with every sample (about every 5 s); otherwise every 60 s. 1.4 adds `notResponding`
  (how many processes are Not responding), `boosted` and `boostAuto`.
- **Ctrl+Alt+T** opens it - **off by default** (Settings). Ctrl+Shift+Esc belongs to Windows' Task Manager and can't be taken
  over safely.
- **Low overhead**: once a second while its window is open and not minimized (one `NtQuerySystemInformation` call for all
  processes, the GPU counters read in one go, about 10 ms per sample); every 5 s in the tray for History (or not at all, if you
  switch that off). Measured on a 32-thread PC: **0.02% of the CPU in the tray, 0.05% with the window open**. 1.2, with Hallow
  Remote / Hub asking every 3 s: 0.018% with `quick`, 0.028% with `status` (1.1: 0.050%); idle in the tray unchanged (0.012-0.016%).

## Limits (no admin)

- **Per-process network speed** needs admin: Windows only gives bytes per process to ETW tracing. Without "Precise network" the
  Network column shows each app's **open TCP connections**, and the Performance tab the PC's total send / receive speed.
- **Services** you may not control (most of them) need admin to start or stop - that's what the (admin) buttons and UAC are for.
- **Disk** per process is file and device I/O (Windows' I/O counters, like Process Explorer's I/O column); it can include some
  network and device I/O. The Performance tab's disk graph is the physical disks.
- Processes of other users and protected services don't show their path to a normal user; they're marked "(protected)" and
  Hallow Tasks leaves them alone.

## Install

1. Download `Hallow.Tasks.zip` from the [latest release](https://github.com/Dom428/hallow-tasks/releases/latest).
2. Right-click the zip > Properties > tick **Unblock** > OK, then Extract All.
3. Double-click `INSTALL.bat` (no admin needed). It installs to `%LOCALAPPDATA%\Programs\Hallow Tasks` with Start menu and
   Desktop shortcuts and an entry in Windows' Apps list. Settings live in `%APPDATA%\HallowTasks`.

Or install it from [Hallow Setup](https://github.com/Dom428/hallow-setup).

## Build

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1` (`build.ps1 -Tests` also builds
`tests\TasksTests.exe` and `tests\Dummy.exe`). WPF in code, `src\Theme.xaml` embedded - **every colour is a theme brush** there, so
light mode swaps the values of the same keys (`src\Look.cs`). `HallowTasks.exe --snap <folder>` renders every page in dark and light with made-up data.

Tests: grouping, Impact, the protected / anti-cheat / game lists, the game panel, the priority keeper, startup impact and
Disable / Restore on a fake HKCU key, the family pipe of a test copy with made-up processes (a fake process control - nothing real
is ended or changed), read-only sampling of the real PC, and kill / priority / affinity / process tree / Efficiency mode **only
on Dummy.exe children the tests start themselves**. 1.1 adds: mode resolution (missing = dark, system through a fake, the per-app
Look wins) and a contrast check of every light text / background pair with several accents (>= 4.5:1, 3:1 for graph lines);
History spikes, stutters (CPU peaks and Overlay's live numbers), the 24-hour log and its cap; alerts; the tree; services with a
**fake controller** (the real list is only read - no service is ever started or stopped in tests, and no UAC prompt is ever
shown); the precise-network protocol, a made-up kernel event, and the helper started **without** elevation (it must answer
"denied"); and the overhead of a real test copy in the tray and with its window "open". 1.2 adds: the busy-app count, `quick`
(fresh within 5 s over 14 s, answered from the cache, also with the real sampler), status.json kept fresh while asked, and no History
from samples taken only for Remote / Hub; 1.2.1: right after start `quick` never answers with the first sample's zeros. 1.3: Close game - who it's offered for (never anti-cheat or launchers, even listed as games), what Force close may end, the steps with a fake process layer (closed nicely, forced, access denied, the log), and **stand-ins the tests start in their own temp folder** (tests\StandIn.exe: an off-screen window that closes on WM_CLOSE or ignores it, with a helper, an anti-cheat-named and a launcher-named child that must stay untouched). 1.4: who Close (not responding) is offered for (never games, anti-cheat, launchers, Windows, Hallow or Tasks), the 15 s note held while a game runs, the safe flow in app mode, and a **stand-in that really stops reading its messages** (Windows marks it Not responding; the sampler isn't slowed; Force close ends it); Startup apps in a **fake registry root and temp folders only** - StartupApproved values written exactly like Task Manager's, the entries themselves untouched, all-users entries refused - with the real startup entries read before and after and required to be unchanged; Game boost's list and every exclusion, lower / Efficiency / close / Force close / restore / reopen with fakes and then with **stand-ins** (closed nicely, forced, reopened with the same command line, priority put back); and no "45 %" anywhere in the app's text. 1.5: closing is instant - Close now ends a stand-in game that ignores WM_CLOSE and its helper at once while the anti-cheat-named and launcher-named stand-ins stay untouched, access denied is shown at once (no waiting, nothing else touched), **Ask it to close first** still waits (fake clock and a stand-in), **Don't ask again** skips the confirm (the click ends it; the dialog still shows when Windows refuses) and is saved / undone in Settings, a 1.4 settings file upgrades to the instant default, a really hung stand-in is ended right away, and Game boost's Close ends a stand-in at once (a saved profile applied by itself still only asks). The suite stops itself (and the ship script kills it) after 5 minutes.

## Credits

Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed CC BY 4.0 - see `FontAwesome-LICENSE.txt`.
