HALLOW TASKS - a better Task Manager, Hallow style

See what's using your PC, grouped by app, what's slowing your game, and what spiked your PC.
No admin needed for normal use.

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
- History - what spiked my PC: a rolling hour of each app's CPU, GPU, RAM and disk, a timeline,
  and which apps spiked and when. While you play it marks the stutter moments (from Hallow
  Overlay's live numbers when it runs, otherwise CPU / GPU peaks) and what else was busy then.
  An optional 24-hour log on disk (off by default, about 1 MB at most).
- Efficiency mode, like Windows 11 Task Manager's (EcoQoS + Idle priority), in one click from
  the game panel or the right-click menu. Never for Windows, your game or anti-cheat.
- Services: status, startup type, PID and description. Start / stop / restart where Windows
  lets you; otherwise "(admin)" - Windows asks you (UAC) first. Microsoft's own: leave alone.
- Process tree view (parent -> children) with End tree.
- Quiet alerts while you play (off by default): a note through Hallow Hub when a background app
  takes more than X% CPU or GPU for more than N seconds - never a pop-up over the game.
- Precise network (optional, needs admin): bytes per second per app, through a small helper
  that Windows asks you about (UAC) each time; it stops when Hallow Tasks closes.
- Light and dark mode: follows Hallow Toolkit's setting, or pick one in Settings > Look.
- Startup apps with an impact estimate. Your own startup entries can be disabled and restored.
- Hallow family: Hallow Hub's tray menu lists it, the Hallow accent colour, status for other apps.
- Ctrl+Alt+T opens it (off until you turn it on in Settings). Ctrl+Shift+Esc stays Windows' own.

GOOD TO KNOW
- It samples once a second while its window is open; in the tray, a sample every 5 s for
  History (well under 1% CPU - Settings can turn that off).
- Per-process network speed needs admin (Windows only gives it to ETW): without "Precise
  network" the Network column counts each app's connections.
- CPU temperature needs a kernel driver, so it isn't shown (same as Hallow Overlay).
- Nothing is sent anywhere. Crash reports stay on this PC.

WHAT'S NEW IN 1.2.1
- Right after Hallow Tasks starts, Remote and Hub get real CPU numbers (1.2 could show 0% for
  the first few seconds).

WHAT'S NEW IN 1.2
- Hallow Remote and Hallow Hub get live numbers at once (never more than 5 s old) and a count of
  busy apps, without Hallow Tasks working any harder in the tray.

WHAT'S NEW IN 1.1
- Light and dark mode (Follow Hallow / Light / Dark).
- History: what spiked my PC, with a timeline and the stutter moments while you play.
- Efficiency mode (like Windows 11's), a Services tab, a process tree view with End tree,
  quiet in-game alerts (off by default) and optional precise network (needs admin).

WHAT'S NEW IN 1.0
- The first version: processes by app, What's slowing my game, Performance, Startup apps,
  details and actions for each process, and the family link.

CREDITS
Icons: Font Awesome Free 7.3.1 by Fonticons, Inc. (https://fontawesome.com), licensed CC BY 4.0 - see FontAwesome-LICENSE.txt.
