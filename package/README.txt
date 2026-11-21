HALLOW TASKS - a better Task Manager, Hallow style

See what's using your PC, grouped by app, and what's slowing your game. No admin needed.

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)

WHAT IT DOES
- Processes, grouped by app like Task Manager (Discord, Chrome, Steam... and the Hallow apps under
  "Hallow"): CPU, memory, GPU, GPU memory, disk, network connections, a power hint, publisher
  (signed or not) and path. Sorted by Impact (CPU + GPU + disk + memory) - click a column to sort,
  search, or filter Apps / Background / Windows / Hallow.
- Click a row for details: End task / End process tree (always asks first), priority, affinity
  (with a warning), open file location, search online (only when you click), scan with Microsoft
  Defender, copy details. Windows' own processes, your game and anti-cheat are refused with a reason.
- What's slowing my game: while Delta Force, Discovery or a game Hallow Hub says you're playing
  runs, the other apps ranked by the CPU, GPU and disk they took over the last 10 seconds. Lower
  one to Below normal or Idle for as long as the game runs (it goes back when the game closes, or
  when you quit Hallow Tasks), or end it. The game itself and anti-cheat (SGuard, EasyAntiCheat,
  BattlEye, Vanguard and others) are never touched: "Anti-cheat - leave alone".
- Performance: live graphs for CPU (or one per core), GPU (3D, video encode / decode, copy),
  memory, GPU memory, disk and network.
- Startup apps with an impact estimate. Your own startup entries can be disabled and restored.
- Hallow family: Hallow Hub's tray menu lists it, the Hallow accent colour, status for other apps.
- Ctrl+Alt+T opens it (off until you turn it on in Settings). Ctrl+Shift+Esc stays Windows' own.

GOOD TO KNOW
- It samples once a second only while its window is open (not minimized). In the tray it does
  nothing but wait.
- Per-process network speed needs admin (Windows only gives it to ETW), so the Network column
  counts each app's connections; the Performance tab shows the PC's total network speed.
- CPU temperature needs a kernel driver, so it isn't shown (same as Hallow Overlay).
- Nothing is sent anywhere. Crash reports stay on this PC.

WHAT'S NEW IN 1.0
- The first version: processes by app, What's slowing my game, Performance, Startup apps,
  details and actions for each process, and the family link.

CREDITS
Icons: Font Awesome Free 7.3.1 by Fonticons, Inc. (https://fontawesome.com), licensed CC BY 4.0 - see FontAwesome-LICENSE.txt.
