// Hallow Tasks 1.4 - Game boost. Before or while you play: what's using CPU / RAM / GPU / network right now, never games, anti-cheat,
// Windows, Hallow or the apps you opted out. Per app: Below normal, Efficiency mode, or Close (Close game's safe flow: ask nicely with
// WM_CLOSE, then Force close only when you click it). After the game (or Restore now) priorities and Efficiency go back and the
// apps you ticked "reopen after" start again - from their own program file with the same command line, started normally by you
// (Hallow Tasks runs as you). Nothing happens by itself unless you save a Boost profile and tick "apply when a game starts" - and
// even then closing only asks nicely; Force close is always a click.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace HallowTasks
{
    [DataContract]
    public class BoostItem
    {
        [DataMember] public string Exe;      // exe name without .exe, lower case
        [DataMember] public string Action;   // "below", "idle", "eco", "close"
        [DataMember] public bool Reopen;     // close: start it again after the game
        [DataMember] public string Title;
        public static string Word(string a) { return a == "below" ? "Below normal" : a == "idle" ? "Idle" : a == "eco" ? "Efficiency mode" : a == "close" ? "Close" : a; }
    }

    public class BoostRow
    {
        public AppGroup Group; public double Cpu, Gpu, Net, Score; public long Ram; public int Conns = -1;
        public bool CanClose; public string Note;   // Note: why it can only be lowered (a launcher)
    }

    public static class Boost
    {
        public const int WaitSeconds = 10;
        public static string Exe(AppGroup g) { var m = g == null ? null : g.Main; return m == null ? "" : Known.Bare(m.Name); }
        public static bool Skipped(AppGroup g, IEnumerable<string> skip)
        {
            if (skip == null) return false; var s = new HashSet<string>(skip.Select(Known.Bare));
            return g.Members.Any(m => s.Contains(Known.Bare(m.Name)));
        }
        // null = Game boost may list and change it
        public static string WhyNot(AppGroup g, ProcInfo game, IEnumerable<string> extraGames, IEnumerable<string> skip, string windowsDir, int selfPid)
        {
            if (g == null || g.Members.Count == 0) return "That app has closed.";
            if (g.IsGame || (game != null && g.Members.Any(m => m.Key == game.Key)) || g.Members.Any(m => Known.IsGameName(m.Name, extraGames))) return "That's your game.";
            if (g.IsAntiCheat || g.Members.Any(m => Known.IsAntiCheat(m.Name))) return "That's anti-cheat - never touched.";
            if (g.Key == "hallow" || g.Members.Any(Known.IsHallow)) return "That's a Hallow app.";
            if (g.IsProtected || g.Category == "Windows" || g.Members.Any(m => m.Pid <= 4 || Known.IsCritical(m.Name) || Known.IsProtected(m, windowsDir))) return "That's part of Windows.";
            if (g.Members.Any(m => m.Pid == selfPid)) return "That's Hallow Tasks itself.";
            if (Skipped(g, skip)) return "You opted it out of Game boost.";
            return null;
        }
        public static bool CanClose(AppGroup g) { return !g.Members.Any(m => GameClose.IsLauncher(m.Name)); }
        // what's using the PC right now (biggest first)
        public static List<BoostRow> List(IList<AppGroup> groups, ProcInfo game, IEnumerable<string> extraGames, IEnumerable<string> skip, string windowsDir, int selfPid, int max)
        {
            var rows = new List<BoostRow>();
            foreach (var g in groups)
            {
                if (WhyNot(g, game, extraGames, skip, windowsDir, selfPid) != null) continue;
                var net = g.NetBytes >= 0 ? g.NetBytes : 0;
                var r = new BoostRow { Group = g, Cpu = g.Cpu, Gpu = g.Gpu, Ram = g.Ram, Net = g.NetBytes, Conns = g.Conns, CanClose = CanClose(g) };
                r.Score = g.Cpu + 0.8 * g.Gpu + Math.Min(12, g.Ram / 1073741824.0 * 6) + Math.Min(10, net / 1048576.0 * 4) + Math.Min(3, Math.Max(0, g.Conns) * 0.2);
                if (!r.CanClose) r.Note = "A game launcher - lower it only (your game may need it).";
                if (r.Score >= 0.6 || g.Ram >= 250L * 1048576) rows.Add(r);
            }
            return rows.OrderByDescending(r => r.Score).ThenBy(r => r.Group.Title).Take(max).ToList();
        }
    }

    // starts a closed app again after the game (real: Windows; tests and --snap: FakeLauncher)
    public interface IAppLauncher { string Start(string path, string args, string dir); }
    public class WindowsLauncher : IAppLauncher
    {
        public string Start(string path, string args, string dir)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "Its program file isn't there any more.";
            try { using (Process.Start(new ProcessStartInfo(path, args ?? "") { UseShellExecute = true, WorkingDirectory = dir ?? Path.GetDirectoryName(path) })) { } return null; }
            catch (Exception e) { return e.Message; }
        }
    }
    public class FakeLauncher : IAppLauncher
    {
        public readonly List<string> Started = new List<string>();
        public string Start(string path, string args, string dir) { Started.Add(path + (string.IsNullOrEmpty(args) ? "" : " " + args)); return null; }
    }

    // a process's command line, so "reopen after" starts it the same way (NtQueryInformationProcess ProcessCommandLineInformation:
    // Windows 8.1+, only PROCESS_QUERY_LIMITED_INFORMATION - no admin, and it reads nothing else)
    public static class ProcCmd
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr buf, int len, out int ret);
        public static string Read(int pid)
        {
            var h = OpenProcess(0x1000, false, pid); if (h == IntPtr.Zero) return null;
            try
            {
                int len = 4096;
                for (int i = 0; i < 3; i++)
                {
                    var b = Marshal.AllocHGlobal(len);
                    try
                    {
                        int ret; var st = NtQueryInformationProcess(h, 60, b, len, out ret);
                        if (st == 0) { var n = Marshal.ReadInt16(b) & 0xFFFF; var p = Marshal.ReadIntPtr(b, IntPtr.Size); return Marshal.PtrToStringUni(p, n / 2); }
                        if (ret <= len) return null; len = ret + 64;
                    }
                    finally { Marshal.FreeHGlobal(b); }
                }
            }
            catch { }
            finally { CloseHandle(h); }
            return null;
        }
        // "C:\x\app.exe" --a b  ->  (C:\x\app.exe, --a b)
        public static string Args(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return "";
            var s = cmd.TrimStart(); int end;
            if (s.StartsWith("\"")) { end = s.IndexOf('"', 1); end = end < 0 ? s.Length : end + 1; }
            else { end = s.IndexOf(' '); if (end < 0) end = s.Length; }
            return s.Substring(end).Trim();
        }
    }

    // one boost: what you lowered / put in Efficiency mode / closed, and putting it all back
    public class BoostEngine
    {
        public class Closing
        {
            public string Key, Title, Exe, Path, Args; public bool Reopen, Instant; public DateTime AskedAt;   // Instant (1.5): ended right away on your click
            public List<ProcInfo> Members = new List<ProcInfo>();
            public string State = "asking";   // asking, still (Force close offered), closed, forced, denied, reopened, failed
        }
        readonly IProcessControl ctl; readonly EcoKeeper eco; readonly IGameCloser io; readonly IAppLauncher launcher; readonly Func<int, string> cmdLine; readonly Action<string> log;
        public readonly PriorityKeeper Keeper;
        public readonly List<EcoKeeper.Entry> EcoSet = new List<EcoKeeper.Entry>();
        public readonly List<Closing> Closed = new List<Closing>();
        public readonly List<BoostItem> Done = new List<BoostItem>();   // what you did this time (Save as Boost profile)
        public int WaitSeconds = Boost.WaitSeconds;
        public bool SawGame;   // a game ran while boosted: put everything back when it closes
        public BoostEngine(IProcessControl ctl, EcoKeeper eco, IGameCloser io, IAppLauncher launcher, Func<int, string> cmdLine, Action<string> log)
        {
            this.ctl = ctl; this.eco = eco; this.io = io; this.launcher = launcher; this.cmdLine = cmdLine; this.log = log ?? (s => { }); Keeper = new PriorityKeeper(ctl);
        }
        public bool Active { get { return Keeper.Lowered.Count > 0 || EcoSet.Count > 0 || Closed.Count > 0; } }
        void Remember(AppGroup g, string action, bool reopen)
        {
            var exe = Boost.Exe(g); Done.RemoveAll(d => d.Exe == exe);
            Done.Add(new BoostItem { Exe = exe, Action = action, Reopen = reopen, Title = g.Title });
        }
        IEnumerable<ProcInfo> Own(AppGroup g, int selfPid) { return g.Members.Where(m => m.Pid > 4 && m.Pid != selfPid); }

        public string Lower(AppGroup g, Prio to, int selfPid)
        {
            int n = 0; string err = null;
            foreach (var m in Own(g, selfPid)) { var e = Keeper.Lower(m, to); if (e == null) n++; else err = err ?? e; }
            log("boost " + g.Title + ": " + PriorityKeeper.Word(to) + " (" + n + ")");
            if (n == 0) return err ?? "Nothing to lower.";
            Remember(g, to == Prio.Idle ? "idle" : "below", false); return null;
        }
        public string Efficiency(AppGroup g, int selfPid)
        {
            int n = 0; string err = null;
            foreach (var m in Own(g, selfPid))
            {
                var e = eco.Set(m, true);
                if (e == null) { n++; if (!EcoSet.Any(x => x.Pid == m.Pid && x.Created == m.CreateTime)) EcoSet.Add(new EcoKeeper.Entry { Pid = m.Pid, Created = m.CreateTime, Name = m.Name }); Keeper.Lowered.RemoveAll(x => x.Pid == m.Pid); }
                else err = err ?? e;
            }
            log("boost " + g.Title + ": efficiency (" + n + ")");
            if (n == 0) return err ?? "Nothing to change.";
            Remember(g, "eco", false); return null;
        }
        // step 1 of closing: ask nicely (WM_CLOSE to its windows). Step 2 (Force) only when you click it.
        public string Close(AppGroup g, bool reopen, DateTime now, int selfPid)
        {
            Closing c2; var e = Start(g, reopen, now, selfPid, out c2); if (e != null) return e;
            int asked = 0; foreach (var m in c2.Members) asked += io.AskToClose(m.Pid);
            log("boost " + g.Title + ": asked to close (" + asked + " window" + (asked == 1 ? "" : "s") + ")");
            return null;
        }
        // 1.5: your click on Close (Settings' default "end it right away"): ends it at once - no WM_CLOSE, no waiting. The same per-process
        // checks as Force close (never a game, anti-cheat, launcher, Windows or Hallow). A saved profile applied by itself still only asks.
        public string CloseNow(AppGroup g, bool reopen, DateTime now, ProcInfo game, IEnumerable<string> extraGames, string windowsDir, int selfPid)
        {
            Closing c2; var e = Start(g, reopen, now, selfPid, out c2); if (e != null) return e;
            c2.State = "still"; c2.Instant = true;
            return Force(c2.Key, game, extraGames, windowsDir, selfPid);
        }
        string Start(AppGroup g, bool reopen, DateTime now, int selfPid, out Closing c2)
        {
            c2 = null;
            if (!Boost.CanClose(g)) return g.Title + " is a game launcher - lower it instead (your game may need it).";
            var main = g.Main; var own = Own(g, selfPid).ToList(); if (main == null || own.Count == 0) return "That app has closed.";
            var old = Closed.FirstOrDefault(c => c.Key == g.Key); if (old != null) Closed.Remove(old);
            c2 = new Closing { Key = g.Key, Title = g.Title, Exe = Boost.Exe(g), Path = main.Path, Reopen = reopen, AskedAt = now, Members = own };
            if (reopen) { string cl = null; try { cl = cmdLine == null ? null : cmdLine(main.Pid); } catch { } c2.Args = ProcCmd.Args(cl); }
            Closed.Add(c2); Remember(g, "close", reopen);
            return null;
        }
        bool AnyAlive(Closing c) { return c.Members.Any(m => io.Alive(m.Pid, m.CreateTime)); }
        public void Tick(DateTime now)
        {
            foreach (var c in Closed)
            {
                if (c.State != "asking" && c.State != "still" && c.State != "denied") continue;
                if (!AnyAlive(c)) { c.State = "closed"; log("boost " + c.Title + ": closed"); }
                else if (c.State == "asking" && (now - c.AskedAt).TotalSeconds >= WaitSeconds) c.State = "still";
            }
        }
        public int SecondsLeft(Closing c, DateTime now) { return c.State != "asking" ? 0 : Math.Max(0, WaitSeconds - (int)Math.Floor((now - c.AskedAt).TotalSeconds)); }
        // step 2, only on a click, only after asking didn't work. Rechecked: never a game, anti-cheat, launcher, Windows or Hallow.
        public string Force(string key, ProcInfo game, IEnumerable<string> extraGames, string windowsDir, int selfPid)
        {
            var c = Closed.FirstOrDefault(x => x.Key == key); if (c == null) return "Nothing to force close.";
            if (c.State != "still") return c.State == "asking" ? "Still waiting for it to close." : null;
            int ended = 0; bool denied = false;
            foreach (var m in c.Members.OrderByDescending(x => x.CreateTime))
            {
                if (HungClose.WhyNot(m, game, extraGames, windowsDir, selfPid) != null) continue;
                int err; var r = io.Terminate(m.Pid, m.CreateTime, out err);
                if (r == KillResult.Ok) ended++; else if (r == KillResult.Denied) denied = true;
            }
            c.State = denied && ended == 0 ? "denied" : "forced";
            log("boost " + c.Title + ": " + (c.State == "denied" ? "Windows denied " + (c.Instant ? "closing it" : "force close") : (c.Instant ? "ended right away (" : "force closed (") + ended + ")"));
            return c.State == "denied" ? "Windows won't let Hallow Tasks end " + c.Title + " (it may run as admin)." : null;
        }

        // after the game, Restore now, or quit (reopen = false on quit): priorities and Efficiency back, ticked apps started again
        public string Restore(bool reopen)
        {
            int prios = Keeper.RestoreAll(), ecos = 0, opened = 0; var failed = new List<string>();
            foreach (var e in EcoSet.ToList())
            {
                var ct = ctl.CreateTime(e.Pid);
                if (ct != 0 && ct == e.Created && eco.Set(new ProcInfo { Pid = e.Pid, CreateTime = e.Created, Name = e.Name }, false) == null) ecos++;
            }
            EcoSet.Clear();
            if (reopen)
                foreach (var c in Closed.Where(x => x.Reopen && (x.State == "closed" || x.State == "forced")))
                {
                    var err = launcher.Start(c.Path, c.Args, null);
                    if (err == null) { opened++; c.State = "reopened"; } else failed.Add(c.Title + ": " + err);
                }
            Closed.Clear(); SawGame = false;
            log("boost restored: " + prios + " priorities, " + ecos + " efficiency, " + opened + " reopened" + (failed.Count > 0 ? ", " + failed.Count + " failed" : ""));
            var parts = new List<string>();
            if (prios > 0) parts.Add(prios + " priorit" + (prios == 1 ? "y" : "ies") + " back to normal");
            if (ecos > 0) parts.Add(ecos + " out of Efficiency mode");
            if (opened > 0) parts.Add(opened + " app" + (opened == 1 ? "" : "s") + " reopened");
            var s = parts.Count == 0 ? "Nothing needed putting back." : string.Join(", ", parts) + ".";
            if (failed.Count > 0) s += " Couldn't reopen " + string.Join("; ", failed) + ".";
            return s;
        }

        // a saved profile, when a game starts (only if you ticked it): lower / Efficiency / ask nicely to close - never Force close
        public int Apply(List<BoostItem> profile, IList<AppGroup> groups, ProcInfo game, IEnumerable<string> extraGames, IEnumerable<string> skip, string windowsDir, int selfPid, DateTime now)
        {
            int n = 0; if (profile == null) return 0;
            foreach (var it in profile)
                foreach (var g in groups.Where(x => Boost.Exe(x) == it.Exe).ToList())
                {
                    if (Boost.WhyNot(g, game, extraGames, skip, windowsDir, selfPid) != null) continue;
                    string e = it.Action == "below" ? Lower(g, Prio.BelowNormal, selfPid) : it.Action == "idle" ? Lower(g, Prio.Idle, selfPid) : it.Action == "eco" ? Efficiency(g, selfPid) : it.Action == "close" ? Close(g, it.Reopen, now, selfPid) : "unknown";
                    if (e == null) n++;
                }
            log("boost profile applied: " + n);
            return n;
        }
    }
}
