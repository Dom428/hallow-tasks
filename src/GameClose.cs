// Hallow Tasks 1.3 - "Close game...": the one deliberate way to close a game that hangs on exit. Only when you click it and
// confirm - nothing automatic, ever. Step 1 asks nicely: WM_CLOSE to the game's visible top-level windows (what Task Manager's
// End task does for apps), then up to 10 s of waiting. Step 2, only if you click Force close: TerminateProcess on the game and its
// own helpers (in the game's install folder, or known crash / web helpers it started). Never anti-cheat, never launchers (Steam,
// Battle.net, Epic, EA, Ubisoft...), never Windows or Hallow. When Windows says no (anti-cheat protects the game: access denied),
// Tasks says so plainly and lists what you can do - it never tries to get around the protection (no admin, no debug privilege, no
// driver, nothing done to anti-cheat). The log gets one line per outcome: the exe name and what happened.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HallowTasks
{
    public enum KillResult { Ok, Gone, Denied, Failed }

    // the process layer "Close game..." uses (real: Windows; tests and --snap: FakeGameCloser)
    public interface IGameCloser
    {
        int AskToClose(int pid);                 // WM_CLOSE to its visible top-level windows; how many were asked
        bool Alive(int pid, long created);       // still running (and the pid is still that process)
        KillResult Terminate(int pid, long created, out int error);
    }

    public class WindowsGameCloser : IGameCloser
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr h, int code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(IntPtr h, out long create, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        const int QUERY_LIMITED = 0x1000, TERMINATE = 0x0001, SYNCHRONIZE = 0x00100000;
        const uint WM_CLOSE = 0x0010, GW_OWNER = 4, WAIT_TIMEOUT = 0x102;

        public int AskToClose(int pid)
        {
            var hs = new List<IntPtr>();
            EnumWindows((h, l) => { int p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h) && GetWindow(h, GW_OWNER) == IntPtr.Zero) hs.Add(h); return true; }, IntPtr.Zero);
            int n = 0; foreach (var h in hs) if (PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)) n++;
            return n;
        }
        public bool Alive(int pid, long created)
        {
            var h = OpenProcess(QUERY_LIMITED | SYNCHRONIZE, false, pid);
            if (h == IntPtr.Zero) return Marshal.GetLastWin32Error() == 5;   // access denied: it's there, Windows just won't open it
            try
            {
                long c, e, k, u;
                if (created != 0 && GetProcessTimes(h, out c, out e, out k, out u) && c != created) return false;   // the pid is someone else now
                return WaitForSingleObject(h, 0) == WAIT_TIMEOUT;
            }
            finally { CloseHandle(h); }
        }
        public KillResult Terminate(int pid, long created, out int error)
        {
            error = 0;
            var h = OpenProcess(TERMINATE | QUERY_LIMITED | SYNCHRONIZE, false, pid);
            if (h == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                if (error == 87) return KillResult.Gone;
                if (error == 5) return Alive(pid, created) ? KillResult.Denied : KillResult.Gone;
                return KillResult.Failed;
            }
            try
            {
                long c, e, k, u;
                if (created != 0 && GetProcessTimes(h, out c, out e, out k, out u) && c != created) return KillResult.Gone;
                if (WaitForSingleObject(h, 0) != WAIT_TIMEOUT) return KillResult.Gone;
                if (TerminateProcess(h, 1)) { WaitForSingleObject(h, 3000); return KillResult.Ok; }
                error = Marshal.GetLastWin32Error();
                if (WaitForSingleObject(h, 0) != WAIT_TIMEOUT) return KillResult.Gone;   // it finished closing by itself meanwhile
                return error == 5 ? KillResult.Denied : KillResult.Failed;
            }
            finally { CloseHandle(h); }
        }
    }

    // made-up processes for tests and --snap: remembers what it was asked, never touches a real process
    public class FakeGameCloser : IGameCloser
    {
        public readonly HashSet<int> Gone = new HashSet<int>(), IgnoresClose = new HashSet<int>(), Denied = new HashSet<int>();
        public readonly List<string> Calls = new List<string>();
        public int AskToClose(int pid) { Calls.Add("close " + pid); if (Gone.Contains(pid)) return 0; if (!IgnoresClose.Contains(pid) && !Denied.Contains(pid)) Gone.Add(pid); return 1; }
        public bool Alive(int pid, long created) { return !Gone.Contains(pid); }
        public KillResult Terminate(int pid, long created, out int error)
        {
            error = 0; Calls.Add("kill " + pid);
            if (Gone.Contains(pid)) return KillResult.Gone;
            if (Denied.Contains(pid)) { error = 5; return KillResult.Denied; }
            Gone.Add(pid); return KillResult.Ok;
        }
    }

    // which processes "Close game..." may be offered for, and which it may end
    public static class GameClose
    {
        public const int WaitSeconds = 10;
        // launchers and their helpers: never closed with the game (exe names without .exe)
        static readonly HashSet<string> Launchers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "steam", "steamwebhelper", "steamservice", "steamerrorreporter", "steamerrorreporter64", "gameoverlayui", "gameoverlayui64",
            "epicgameslauncher", "epicwebhelper", "eosoverlayrenderer-win64-shipping", "epiconlineservices", "epiconlineserviceshost",
            "battle.net", "battle.net helper", "agent", "blizzard error", "eadesktop", "eabackgroundservice", "eaconnect_microsoft", "origin", "originwebhelperservice",
            "ubisoftconnect", "upc", "uplaywebcore", "ubisoftgamelauncher", "ubisoftgamelauncher64", "galaxyclient", "galaxyclient helper", "galaxycommunication",
            "riotclientservices", "riotclientux", "riotclientuxrender", "riotclientcrashhandler", "wegame", "wegameapp", "tgp_daemon", "rockstarservice", "launcher", "launcherpatcher",
            "socialclubhelper", "playnite.desktopapp", "playnite.fullscreenapp", "gog galaxy", "xboxapp", "xboxpcapp", "gamingservices", "gamingservicesnet",
            "delta force launcher", "deltaforcelauncher", "garena", "bsglauncher", "itch", "amazon games", "amazongames", "pcl", "lghub", "overwolf" };
        // helpers a game starts that live outside its folder but are part of it
        static readonly HashSet<string> Helpers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "crashreportclient", "unrealcefsubprocess", "crashpad_handler", "cefsharp.browsersubprocess", "qtwebengineprocess", "msedgewebview2", "unitycrashhandler64", "unitycrashhandler32" };
        public static bool IsLauncher(string exe) { var b = Known.Bare(exe); return b.Length > 0 && Launchers.Contains(b); }

        // may "Close game..." be offered for this process? Only a process tagged Game; never anti-cheat, a launcher, Windows or Hallow
        public static bool Offerable(ProcInfo p, ProcInfo game, IEnumerable<string> extraGames, int selfPid)
        {
            if (p == null || p.Pid <= 4 || p.Pid == selfPid) return false;
            if (Known.IsAntiCheat(p.Name) || IsLauncher(p.Name) || Known.IsCritical(p.Name) || Known.IsHallow(p)) return false;
            return Known.IsGameName(p.Name, extraGames) || (game != null && game.Key == p.Key);
        }
        // the game process behind a row (a group or one of its processes), or null when "Close game..." isn't offered for it
        public static ProcInfo Pick(AppGroup g, ProcInfo p, ProcInfo game, IEnumerable<string> extraGames, int selfPid)
        {
            if (p != null) return Offerable(p, game, extraGames, selfPid) ? p : null;
            if (g == null) return null;
            var hit = game == null ? null : g.Members.FirstOrDefault(m => m.Key == game.Key);
            if (hit == null) hit = g.Members.Where(m => Known.IsGameName(m.Name, extraGames)).OrderByDescending(m => m.HasWindow).ThenBy(m => m.CreateTime).FirstOrDefault();
            return Offerable(hit, game, extraGames, selfPid) ? hit : null;
        }

        // the game's install folder: Unreal games live in <game>\Binaries\Win64, so their folder is <game>; never a drive, Program Files or Windows
        public static string InstallRoot(string exePath, string windowsDir)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            string dir; try { dir = Path.GetDirectoryName(exePath); } catch { return null; }
            if (string.IsNullOrEmpty(dir)) return null;
            var i = dir.IndexOf("\\Binaries\\", StringComparison.OrdinalIgnoreCase); if (i < 0 && dir.EndsWith("\\Binaries", StringComparison.OrdinalIgnoreCase)) i = dir.Length - 9;
            var root = i > 3 ? dir.Substring(0, i) : dir;
            if (TooBroad(root, windowsDir)) root = TooBroad(dir, windowsDir) ? null : dir;
            return root;
        }
        static bool TooBroad(string d, string windowsDir)
        {
            d = d.TrimEnd('\\'); if (d.Length <= 3) return true;
            foreach (var f in new[] { windowsDir, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
                if (!string.IsNullOrEmpty(f) && string.Equals(f.TrimEnd('\\'), d, StringComparison.OrdinalIgnoreCase)) return true;
            var leaf = Path.GetFileName(d);
            return leaf.Equals("common", StringComparison.OrdinalIgnoreCase) || leaf.Equals("steamapps", StringComparison.OrdinalIgnoreCase) || leaf.Equals("Games", StringComparison.OrdinalIgnoreCase);
        }
        static bool Under(string path, string root) { return path != null && root != null && path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }

        // a process the game started that belongs to the game: never anti-cheat, a launcher, Windows, Hallow or Tasks itself
        public static bool PartOfGame(ProcInfo c, string root, string windowsDir, int selfPid)
        {
            if (c == null || c.Pid <= 4 || c.Pid == selfPid) return false;
            if (Known.IsAntiCheat(c.Name) || IsLauncher(c.Name) || Known.IsCritical(c.Name) || Known.IsProtected(c, windowsDir) || Known.IsHallow(c)) return false;
            return Under(c.Path, root) || Helpers.Contains(Known.Bare(c.Name));
        }
        // the game and its own helpers (the game first). Something it started that isn't part of it is skipped with everything under it.
        public static List<ProcInfo> Targets(ProcInfo game, IList<ProcInfo> all, string windowsDir, int selfPid)
        {
            var r = new List<ProcInfo>(); if (game == null) return r;
            var root = InstallRoot(game.Path, windowsDir);
            var q = new Queue<ProcInfo>(); q.Enqueue(game); var seen = new HashSet<int>();
            while (q.Count > 0)
            {
                var p = q.Dequeue(); if (!seen.Add(p.Pid)) continue; r.Add(p);
                if (all == null) continue;
                foreach (var c in all.Where(x => x.ParentPid == p.Pid && x.Pid != p.Pid && x.CreateTime >= p.CreateTime))
                    if (PartOfGame(c, root, windowsDir, selfPid)) q.Enqueue(c);
            }
            return r;
        }
        // the anti-cheat that's running, in plain words (null: none)
        public static string AntiCheatName(IEnumerable<ProcInfo> all)
        {
            var a = all == null ? null : all.FirstOrDefault(p => Known.IsAntiCheat(p.Name)); if (a == null) return null;
            var b = Known.Bare(a.Name);
            if (b.StartsWith("sguard") || b.StartsWith("ace-") || b.StartsWith("anticheatexpert") || b.StartsWith("acebase") || b.StartsWith("tencentace")) return "ACE";
            if (b.StartsWith("easyanticheat") || b.StartsWith("eaanticheat") || b == "start_protected_game") return "Easy Anti-Cheat";
            if (b.StartsWith("beservice") || b.StartsWith("bedaisy") || b.StartsWith("battleye")) return "BattlEye";
            if (b == "vgc" || b == "vgtray" || b == "vgk") return "Vanguard";
            if (b.StartsWith("faceit")) return "FACEIT";
            return a.Name;
        }
    }

    // one "Close game..." from the confirm to the end. The window drives it; tests drive it with made-up times.
    public class GameCloseRun
    {
        public enum State { Ready, Asking, Closed, StillRunning, Forced, Denied, Failed }
        public readonly ProcInfo Game; public readonly string Title;
        public State Now = State.Ready; public int WaitSeconds = GameClose.WaitSeconds; public int Ended; public int Error;
        public DateTime AskedAt; public double TookSeconds;
        readonly IGameCloser io; readonly Func<IList<ProcInfo>> procs; readonly string windowsDir; readonly int selfPid; readonly Action<string> log;

        public GameCloseRun(ProcInfo game, string title, IGameCloser io, Func<IList<ProcInfo>> procs, string windowsDir, int selfPid, Action<string> log)
        {
            Game = game; Title = string.IsNullOrEmpty(title) ? game.Name : title; this.io = io; this.procs = procs; this.windowsDir = windowsDir; this.selfPid = selfPid; this.log = log ?? (s => { });
        }
        IList<ProcInfo> All { get { var a = procs == null ? null : procs(); return a ?? new List<ProcInfo>(); } }
        // what Force close would end now (the game first)
        public List<ProcInfo> Targets() { return GameClose.Targets(All.FirstOrDefault(p => p.Pid == Game.Pid && p.CreateTime == Game.CreateTime) ?? Game, All, windowsDir, selfPid); }
        public string AntiCheat { get { return GameClose.AntiCheatName(All); } }
        public bool FromSteam { get { return Game.Path != null && Game.Path.IndexOf("\\steamapps\\", StringComparison.OrdinalIgnoreCase) >= 0; } }
        void Log(string what) { log("close game " + Game.Name + ": " + what); }

        // step 1: ask nicely (WM_CLOSE to the game's windows)
        public void Ask(DateTime now)
        {
            if (Now == State.Closed || Now == State.Forced || Now == State.Failed) return;
            if (!GameClose.Offerable(Game, Game, null, selfPid)) { Now = State.Failed; Log("refused (not a game)"); return; }
            if (!io.Alive(Game.Pid, Game.CreateTime)) { Now = State.Closed; Log("it had already closed"); return; }
            foreach (var t in Targets()) io.AskToClose(t.Pid);
            Now = State.Asking; AskedAt = now;
        }
        public int SecondsLeft(DateTime now) { return Now != State.Asking ? 0 : Math.Max(0, WaitSeconds - (int)Math.Floor((now - AskedAt).TotalSeconds)); }
        // while asking: closed yet? After WaitSeconds it's "still running" and Force close is offered. Denied: did it close meanwhile?
        public State Tick(DateTime now)
        {
            if (Now == State.Asking || Now == State.StillRunning || Now == State.Denied)
            {
                if (!io.Alive(Game.Pid, Game.CreateTime))
                {
                    var was = Now; Now = State.Closed; TookSeconds = (now - AskedAt).TotalSeconds;
                    Log(was == State.Asking ? "closed nicely" : was == State.Denied ? "closed by itself after Windows refused" : "closed by itself");
                }
                else if (Now == State.Asking && (now - AskedAt).TotalSeconds >= WaitSeconds) Now = State.StillRunning;
            }
            return Now;
        }
        // step 2, only when you click Force close: the game first; if Windows refuses, nothing else is touched
        public State Force()
        {
            if (Now != State.StillRunning) return Now;   // only after asking nicely didn't work
            var ts = Targets(); var game = ts[0];
            if (!GameClose.Offerable(game, Game, null, selfPid)) { Now = State.Failed; Log("refused (not a game)"); return Now; }
            int err; var r = io.Terminate(game.Pid, game.CreateTime, out err); Error = err;
            if (r == KillResult.Denied) { Now = State.Denied; Log("Windows denied it (protected - anti-cheat or admin)"); return Now; }
            if (r == KillResult.Failed) { Now = State.Failed; Log("Windows couldn't end it (error " + err + ")"); return Now; }
            Ended = r == KillResult.Ok ? 1 : 0;
            foreach (var h in ts.Skip(1).Reverse()) { int e2; if (io.Terminate(h.Pid, h.CreateTime, out e2) == KillResult.Ok) Ended++; }
            Now = r == KillResult.Gone && Ended == 0 ? State.Closed : State.Forced;
            Log(Now == State.Closed ? "closed by itself just before Force close" : "force closed (" + Ended + " process" + (Ended == 1 ? "" : "es") + ")");
            return Now;
        }
        // the dialog was closed before the end
        public void Cancelled() { if (Now == State.Asking || Now == State.StillRunning) Log("still running - not forced"); }

        public string DeniedText()
        {
            var ac = AntiCheat;
            return ac != null ? Title + "'s anti-cheat (" + ac + ") protects the game, so Windows won't let any app end it."
                              : "Windows protects " + Title + " (it may run as admin), so it won't let Hallow Tasks end it.";
        }
        public string[] DeniedOptions()
        {
            var ac = AntiCheat;
            return new[] {
                FromSteam ? "Close it from Steam: the Stop button on the game in your library." : "Close it from its launcher (in Steam: the Stop button on the game in your library).",
                "Wait a minute - " + (ac == "ACE" ? "ACE often finishes its shutdown" : ac != null ? "anti-cheat often finishes its shutdown" : "it may finish closing") + " and the game closes by itself.",
                "Sign out of Windows, or restart." };
        }
    }

    // the confirm dialog: explains both steps, then shows the countdown, Force close, or what to do when Windows refuses
    public static class GameCloseWindow
    {
        public static Window Make(GameCloseRun run, Action done)
        {
            var w = new Window(); Ui.Dress(w, "Hallow Tasks", 500);
            w.ResizeMode = ResizeMode.NoResize; w.ShowInTaskbar = false; w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) }; w.Content = sp;
            DispatcherTimer timer = null; Action render = null;
            Action stop = () => { if (timer != null) timer.Stop(); };
            Action<Button> focus = b => { if (w.IsLoaded) b.Focus(); else w.Loaded += (s, e) => b.Focus(); };
            render = () =>
            {
                sp.Children.Clear(); var st = run.Now;
                Func<WrapPanel> buttons = () => { var row = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right }; sp.Children.Add(row); return row; };
                Func<string, string, UIElement> step = (n, text) =>
                {
                    var d = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                    var num = Ui.Pill(n, "AccentBrush", "AccentGlow"); num.VerticalAlignment = VerticalAlignment.Top; num.Margin = new Thickness(0, 1, 10, 0); DockPanel.SetDock(num, Dock.Left); d.Children.Add(num);
                    var t = Ui.T(text, 12.5, false, Ui.R("Text")); t.TextWrapping = TextWrapping.Wrap; d.Children.Add(t); return d;
                };
                Func<string, string, Border> box = (text, color) =>
                {
                    var t = Ui.T(text, 12.5, false, Ui.R("Text")); t.TextWrapping = TextWrapping.Wrap;
                    return new Border { Background = Ui.R(color + "Bg"), BorderBrush = Ui.R(color), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 4, 0, 8), Child = t };
                };
                int count = run.Targets().Count; string them = count <= 1 ? run.Game.Name : run.Game.Name + " and its own helpers (" + count + " processes)";
                if (st == GameCloseRun.State.Ready)
                {
                    sp.Children.Add(Ui.T("Close " + run.Title + "?", 15, true, null, 6));
                    sp.Children.Add(Ui.Muted("For when the game hangs on exit. Hallow Tasks only does this when you click it - never by itself. If you're in a match, you'll leave it.", 12.5, 12));
                    sp.Children.Add(step("1", "Ask nicely: Windows asks " + run.Title + " to close its windows (what Task Manager's End task does for apps), then Hallow Tasks waits up to " + run.WaitSeconds + " s."));
                    sp.Children.Add(step("2", "If it's still running, Force close - only if you click it: ends " + them + " right away. Anything unsaved is lost."));
                    sp.Children.Add(Ui.Muted("Never touched: anti-cheat (ACE / SGuard, Easy Anti-Cheat, BattlEye, Vanguard...), Steam, Battle.net and other launchers.", 11.5, 0));
                    var row = buttons(); var cancel = Ui.Btn("Cancel", () => w.Close()); row.Children.Add(cancel);
                    var go = Ui.Btn("Close game", () => { run.Ask(DateTime.Now); if (timer != null) timer.Start(); render(); }, true); go.Margin = new Thickness(0); go.MinWidth = 100; row.Children.Add(go);
                    focus(cancel);   // Cancel has the focus: Enter never closes the game by accident
                }
                else if (st == GameCloseRun.State.Asking)
                {
                    sp.Children.Add(Ui.T("Asking " + run.Title + " to close...", 15, true, null, 6));
                    sp.Children.Add(Ui.Muted("Windows asked its windows to close - the way Task Manager's End task does for apps. Games often need a few seconds to save and shut down.", 12.5, 10));
                    var left = run.SecondsLeft(DateTime.Now);
                    sp.Children.Add(Ui.T("Waiting " + left + " s", 22, true, Ui.R("AccentBrush")));
                    var row = buttons(); var cancel = Ui.Btn("Cancel", () => w.Close()); cancel.Margin = new Thickness(0); row.Children.Add(cancel);
                }
                else if (st == GameCloseRun.State.StillRunning)
                {
                    sp.Children.Add(Ui.T(run.Title + " is still running", 15, true, null, 6));
                    sp.Children.Add(Ui.Muted("It didn't close within " + run.WaitSeconds + " s of being asked.", 12.5, 6));
                    sp.Children.Add(box("Force close ends " + them + " right away. Anything unsaved is lost. Anti-cheat and launchers aren't touched.", "Amber"));
                    var row = buttons(); var cancel = Ui.Btn("Cancel", () => w.Close()); row.Children.Add(cancel);
                    row.Children.Add(Ui.Btn("Ask again", () => { run.Ask(DateTime.Now); render(); }));
                    var force = Ui.Danger("Force close", () => { run.Force(); stop(); if (run.Now == GameCloseRun.State.Denied && timer != null) timer.Start(); render(); }); force.Margin = new Thickness(0); force.MinWidth = 100; row.Children.Add(force);
                    focus(cancel);
                }
                else if (st == GameCloseRun.State.Denied)
                {
                    sp.Children.Add(Ui.T("Windows won't let Hallow Tasks end " + run.Title, 15, true, null, 6));
                    sp.Children.Add(box(run.DeniedText(), "Amber"));
                    sp.Children.Add(Ui.T("What you can do", 12.5, true, null, 4));
                    foreach (var o in run.DeniedOptions()) { var t = Ui.T("•  " + o, 12.5, false, Ui.R("Text")); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 4); sp.Children.Add(t); }
                    sp.Children.Add(Ui.Muted("Hallow Tasks never tries to get around the protection - no admin tricks, nothing done to anti-cheat. This message closes by itself if the game does.", 11.5, 0));
                    var row = buttons(); var ok = Ui.Btn("OK", () => w.Close(), true); ok.Margin = new Thickness(0); ok.MinWidth = 80; row.Children.Add(ok);
                }
                else
                {
                    var good = st == GameCloseRun.State.Closed || st == GameCloseRun.State.Forced;
                    sp.Children.Add(Ui.T(st == GameCloseRun.State.Closed ? run.Title + " closed" : st == GameCloseRun.State.Forced ? run.Title + " was force closed" : "Windows couldn't end " + run.Title, 15, true, null, 6));
                    sp.Children.Add(Ui.Muted(st == GameCloseRun.State.Closed ? "It closed after being asked nicely." : st == GameCloseRun.State.Forced ? run.Ended + " process" + (run.Ended == 1 ? "" : "es") + " ended. Anti-cheat and launchers weren't touched - they close by themselves." : "Windows said error " + run.Error + ". Try closing it from its launcher, or sign out / restart.", 12.5, 0));
                    var row = buttons(); var ok = Ui.Btn("OK", () => w.Close(), good); ok.Margin = new Thickness(0); ok.MinWidth = 80; row.Children.Add(ok);
                }
            };
            if (!Snap.On)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (s, e) =>
                {
                    var before = run.Now; var left = run.SecondsLeft(DateTime.Now); run.Tick(DateTime.Now);
                    if (run.Now != GameCloseRun.State.Asking && run.Now != GameCloseRun.State.Denied) stop();
                    if (run.Now != before || run.Now == GameCloseRun.State.Asking) render();
                };
            }
            render();
            w.Closed += (s, e) => { stop(); run.Cancelled(); if (done != null) done(); };
            return w;
        }
    }
}
