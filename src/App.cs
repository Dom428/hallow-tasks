// Hallow Tasks - a better Task Manager, Hallow style: processes grouped by app (CPU, RAM, GPU, GPU memory, disk, network
// connections, a power hint, publisher and path), "What's slowing my game", live performance graphs, startup apps with an impact
// estimate, and the family link. No admin needed. It samples once a second only while its window is open (not minimized), and
// never touches a game or anti-cheat process. Ending, lowering or changing a process only happens when you click it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("Hallow Tasks")]
[assembly: AssemblyProduct("Hallow Tasks")]
[assembly: AssemblyDescription("A better Task Manager, Hallow style")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]

namespace HallowTasks
{
    public static class Program
    {
        public const string Version = "1.2.1";
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);

        [STAThread]
        public static void Main(string[] args)
        {
            int ci = Array.IndexOf(args, "--cmd");
            if (ci >= 0) { Environment.Exit(Family.RunCmd(ci + 1 < args.Length ? args[ci + 1] : "")); return; }
            // the two elevated helpers (Windows asks you with UAC before either runs): a service action, and the precise network trace
            int vi = Array.IndexOf(args, "--svc"); if (vi >= 0) { Environment.Exit(WindowsServices.RunHelper(args, vi + 1)); return; }
            int ni = Array.IndexOf(args, "--netetw"); if (ni >= 0) { Environment.Exit(ni + 2 < args.Length ? NetHelper.Run(args[ni + 1], args[ni + 2]) : 2); return; }
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length)
            {
                Snap.On = true; TaskSettings.Frozen = true;
                var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa);
                SnapAll(args[si + 1]); return;
            }
            var leaf = TaskSettings.TestDir != null ? "Test" + Path.GetFileName(TaskSettings.TestDir.TrimEnd('\\')) : "";
            bool created;
            var mutex = new Mutex(true, @"Local\HallowTasks" + leaf, out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HallowTasksShow" + leaf);
            if (!created) { showEvent.Set(); return; }
            Crash.Install();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(app);
            app.DispatcherUnhandledException += (s, e) => { Crash.Write(e.Exception); e.Handled = true; Environment.Exit(1); };
            var c = new Controller(args.Contains("--tray"));
            new Thread(() => { while (showEvent.WaitOne()) app.Dispatcher.BeginInvoke(new Action(() => c.ShowMain(null))); }) { IsBackground = true }.Start();
            app.Run();
            GC.KeepAlive(mutex);
        }
        public static string AccentHex = Accent.Default;
        public static bool Light; public static bool? ForceLight;   // ForceLight: --snap renders both
        // the family console look: dark purple-black (or the family light palette - Look.cs), the Hallow accent (theme.json) swapped
        // in, lilac secondaries. Every colour is a theme resource (Theme.xaml).
        public static void LoadTheme(Application app, TaskSettings st = null)
        {
            if (st == null) st = TaskSettings.Load();
            AccentHex = Accent.Current(st.UseHallowAccent);
            Light = ForceLight ?? Look.Current(st);
            var x = Res("HallowTasks.Theme.xaml"); if (Light) x = Look.ToLight(x);
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Accent.ApplyTo(x, Light ? Look.DarkAccent(AccentHex) : Ui.Readable(AccentHex))));
        }
        static string Res(string name) { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) using (var r = new StreamReader(s)) return r.ReadToEnd(); }

        // --snap <folder>: every page rendered off-screen with made-up processes (never this PC's real ones), dark and light ("-light")
        static void SnapAll(string dir)
        {
            foreach (var light in new[] { false, true }) { ForceLight = light; LoadTheme(Application.Current); SnapPass(dir, light ? "-light" : ""); }
        }
        static void SnapPass(string dir, string sfx)
        {
            Func<string, string> F = n => Path.Combine(dir, n + sfx + ".png");
            var c = new Controller(true, true); Ui.RealIcons = c.RealSnap;
            for (int i = 0; i < 60; i++) { c.SampleNow(); if (c.RealSnap && i < 3) Thread.Sleep(1000); else if (c.RealSnap) break; }   // a little history for the graphs and the game panel
            if (!c.RealSnap) FakeHistory.Fill(c.Hist, DateTime.Now);
            c.RefreshServices();
            var w = new MainWindow(c);
            foreach (var page in MainWindow.Pages.Select(p => p[0]))
            { w.Show(page); w.Refresh(); Snap.Save(w, F(page), 1300, 800); }
            // the processes list with Discord open and selected (the details strip), searched, and filtered
            w.Show("processes"); w.SnapSelect("discord", true); w.Refresh(); Snap.Save(w, F("processes-details"), 1300, 800);
            w.SnapSearch("chrome"); w.Refresh(); Snap.Save(w, F("processes-search"), 1300, 800); w.SnapSearch("");
            w.SnapFilter("Hallow"); w.Refresh(); Snap.Save(w, F("processes-hallow"), 1300, 800); w.SnapFilter("All");
            w.SnapSelect("exe:sguard64", false); w.Refresh(); Snap.Save(w, F("processes-anticheat"), 1300, 800);
            // the tree view with Discord selected
            c.S.TreeView = true; w.Show("processes"); w.SnapSelect("discord", false); w.Refresh(); Snap.Save(w, F("processes-tree"), 1300, 800); c.S.TreeView = false;
            // precise network on (made-up numbers)
            c.Net.FakeOn(); c.Net.Feed("net 1000 4300:12000:2400000 4310:3000:150000 4100:9000:60000 4500:1000:820000 4700:2400000:30000 7000:40000:350000"); c.SampleNow();
            w.Show("processes"); w.Refresh(); Snap.Save(w, F("processes-network"), 1300, 800); c.Net.Stop(); c.SampleNow();
            // the game panel after lowering Wallpaper Engine and putting OBS in Efficiency mode
            var wp = c.Groups.FirstOrDefault(g => g.Key == "wallpaper"); if (wp != null) c.LowerGroup(wp, Prio.BelowNormal);
            var obs = c.Groups.FirstOrDefault(g => g.Key == "obs"); if (obs != null) c.SetEfficiency(obs, true);
            w.Show("game"); w.Refresh(); Snap.Save(w, F("game-lowered"), 1300, 800);
            c.S.PerCore = true; w.Show("performance"); w.Refresh(); Snap.Save(w, F("performance-cores"), 1300, 800); c.S.PerCore = false;
            w.Show("services"); w.SnapServices("Steam Client Service"); w.Refresh(); Snap.Save(w, F("services-selected"), 1300, 800); w.SnapServices(null);
            // no game running
            c.SnapGame(false); for (int i = 0; i < 3; i++) c.SampleNow(); w.Show("game"); w.Refresh(); Snap.Save(w, F("game-none"), 1300, 800);
            // the dialogs
            Snap.Save(Dialogs.ConfirmWindow("End Discord?", "Discord has 4 processes. Anything unsaved in it is lost.", "End task", true, null), F("dialog-end"), 460, 0);
            Snap.Save(Dialogs.PriorityWindow("Discord.exe", Prio.Normal, null), F("dialog-priority"), 420, 0);
            Snap.Save(Dialogs.AffinityWindow("obs64.exe", 16, 0xFFFF, null), F("dialog-affinity"), 460, 0);
            Snap.Save(Dialogs.InfoWindow("Hallow Tasks", Safety.WhyNot(new ProcInfo { Name = "SGuard64.exe", Pid = 5044 }, null, null, @"C:\Windows", 1), null), F("dialog-refused"), 460, 0);
            Snap.Save(Dialogs.ConfirmWindow("Stop Print Spooler?", "This needs admin: Windows will ask you first (UAC). Programs that use it may stop working until it runs again.", "Stop (admin)", false, null), F("dialog-service-admin"), 460, 0);
            Snap.Save(CrashNotice.Make(Path.Combine(dir, "example.txt")), F("crash"), 440, 0);
        }
    }
    // the app: tray icon, family pipe, sampling, the game watch, actions, updates
    public class Controller
    {
        public readonly TaskSettings S = TaskSettings.Load();
        public readonly bool SnapMode; public bool RealSnap { get { return SnapMode && sampler is WindowsSampler; } }
        readonly Dispatcher ui;
        MainWindow main; WF.NotifyIcon tray; bool hubHosts, hubLists, quitting, balloonOwn;
        readonly ISampler sampler; readonly object sampleLock = new object();
        public readonly IProcessControl Ctl; public readonly PriorityKeeper Keeper; public readonly GameWatch Watch = new GameWatch();
        public readonly EcoKeeper Eco; public readonly HistoryStore Hist = new HistoryStore(); public HistoryLog HistLog; readonly AlertWatch alerts = new AlertWatch();
        public readonly NetPrecise Net = new NetPrecise(); public readonly IServiceSource Services;
        public readonly List<string[]> Notes = new List<string[]>();   // the quiet in-game alerts of this session: time, title, text
        public readonly StartupStore Store; readonly IStartupSource startupSrc;
        readonly List<string> log = new List<string>();
        public readonly int SelfPid = Process.GetCurrentProcess().Id;
        public string WindowsDir { get { return sampler.WindowsDir; } }

        // what the pages show
        public Sample Last; public List<AppGroup> Groups = new List<AppGroup>(); public ProcInfo Game; public string GameTitle; public DateTime? GameSince;
        public List<string> HubPlaying = new List<string>(); int hubReadAt = -100000;
        public readonly Dictionary<string, Ring> History = new Dictionary<string, Ring>();
        public double SampleMs; public double OwnCpu;
        public event Action Sampled;
        DispatcherTimer sampleTimer, idleTimer, bgTimer, lookTimer; FileSystemWatcher accentWatch; Hotkey hotkey;

        public Controller(bool trayOnly, bool snapMode = false)
        {
            SnapMode = snapMode; ui = Dispatcher.CurrentDispatcher;
            bool fake = snapMode || TaskSettings.Fake;
            // developer check only (a test folder + HALLOW_TASKS_SNAPREAL=1): --snap with this PC's real processes, read-only (the control stays fake)
            bool real = snapMode && TaskSettings.TestDir != null && Environment.GetEnvironmentVariable("HALLOW_TASKS_SNAPREAL") == "1";
            sampler = fake && !real ? (ISampler)new FakeSampler() : new WindowsSampler();
            Ctl = fake ? (IProcessControl)new FakeControl() : new WindowsControl();
            Keeper = new PriorityKeeper(Ctl); Eco = new EcoKeeper(Ctl);
            Services = fake ? (IServiceSource)new FakeServices() : new WindowsServices();
            Store = TaskSettings.TestDir != null || snapMode ? StartupStore.ForTest(@"Software\HallowTasks\Test-" + (TaskSettings.TestDir == null ? "snap" : Path.GetFileName(TaskSettings.TestDir.TrimEnd('\\')))) : StartupStore.Real();
            startupSrc = fake ? (IStartupSource)new FakeStartup() : new WindowsStartup(Store.RunPath);
            if (snapMode) return;
            Family.Serve("Tasks", Command);
            sampleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; sampleTimer.Tick += (s, e) => SampleAsync();
            // while the window is closed or minimized: only a cheap check every 5 s - is the game still running? (to put lowered priorities back)
            idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; idleTimer.Tick += (s, e) => IdleTick(); idleTimer.Start();
            // History / alerts while the window is closed: a sample every 5 s (Settings can turn it off; the window samples once a second)
            // v1.2: also while Remote / Hub want fresh numbers (someone asked "quick" in the last 2 minutes, or Hub / Remote is running)
            bgTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) }; bgTimer.Tick += (s, e) => { if (!Active && (S.HistoryInTray || S.Alerts || Leased || ConsumersRunning())) SampleAsync(); }; bgTimer.Start();
            if (S.HistoryLog) OpenLog();
            if (!TaskSettings.Headless)
            {
                SetupTray();
                var ht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; ht.Tick += (s, e) => CheckHost(); ht.Start(); CheckHost();
                accentWatch = Accent.Watch(() => ui.BeginInvoke(new Action(ApplyAccent)));
                // light / dark: theme.json's mode (the watcher above), "Same as Windows" and a Hallow folder made later - a cheap check every 5 s while the window is open
                lookTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; lookTimer.Tick += (s, e) => { if (main != null) ApplyAccent(); }; lookTimer.Start();
                if (TaskSettings.TestDir == null || Environment.GetEnvironmentVariable("HALLOW_TASKS_API") != null)
                {
                    var upd = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    upd.Tick += (s, e) => { upd.Interval = TimeSpan.FromHours(6); DateTime last; if (!DateTime.TryParse(S.LastUpdateCheck, out last) || (DateTime.Now - last).TotalHours >= 24) CheckForUpdate(false); };
                    upd.Start();
                }
                ui.BeginInvoke(new Action(ShowCrashNotice), DispatcherPriority.ApplicationIdle);
                if (S.Hotkey) SetHotkey(true, false);
            }
            WriteStatus();
            var st = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) }; st.Tick += (s, e) => WriteStatusFresh(); st.Start();
            if (!trayOnly && !TaskSettings.Headless) ShowMain(null);
        }
        void Log(string s) { lock (log) { log.Add(DateTime.Now.ToString("HH:mm:ss") + " " + s); if (log.Count > 80) log.RemoveAt(0); } }
        public IEnumerable<string> ExtraGames { get { return S.ExtraGames; } }

        // ---------------- sampling
        // the window asks for sampling while it's visible and not minimized; everything else stops
        public bool Active { get; private set; }
        public void SetActive(bool on)
        {
            if (SnapMode || on == Active) return;
            Active = on; Log("sampling " + (on ? "on" : "off"));
            if (on) { SampleAsync(); sampleTimer.Start(); } else sampleTimer.Stop();
        }
        bool sampling;
        void SampleAsync()
        {
            if (sampling) return; sampling = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Sample s = null; try { s = Take(); } catch (Exception e) { Log("sample failed: " + e.Message); }
                ui.BeginInvoke(new Action(() => { sampling = false; if (s != null && !quitting && (Last == null || s.At >= Last.At)) Apply(s); }));
            });
        }
        // --snap and tests: a sample right now, on this thread
        public void SampleNow() { Apply(Take()); }
        DateTime lastTakeAt = DateTime.MinValue;
        // the very first sample has no CPU numbers yet (they are the difference to the one before): never a cached answer (1.2.1)
        Sample firstTake; bool tookAny;
        Sample Take() { lock (sampleLock) { var s = sampler.Take(); lastTakeAt = DateTime.Now; if (!tookAny) { tookAny = true; firstTake = s; } return s; } }
        public void SnapGame(bool on) { var f = sampler as FakeSampler; if (f != null) f.GameOn = on; }
        void Apply(Sample s)
        {
            Last = s; SampleMs = s.SampleMs;
            ReadHub();
            if (Net.On) Net.Apply(s.Procs);
            if (TaskSettings.Fake || SnapMode) { var fc = Ctl as FakeControl; if (fc != null) foreach (var p in s.Procs) fc.Created[p.Pid] = p.CreateTime; }
            Groups = Grouping.Build(s.Procs, sampler.WindowsDir, ExtraGames);
            var self = s.Procs.FirstOrDefault(p => p.Pid == SelfPid); OwnCpu = self == null ? 0 : self.Cpu;
            var game = GameDetect.Find(s.Procs, ExtraGames, HubPlaying);
            SetGame(game);
            Watch.Push(Groups, game, s.At);
            Push(s.Sys);
            // History: the point, Overlay's live numbers while you play, the 24-hour log, the quiet alerts
            // (a sample taken only for Remote / Hub while History in the tray is off isn't kept)
            var hp = Active || S.HistoryInTray || HistLog != null ? Hist.Add(s, Groups, game, GameTitle) : null;
            if (game != null && !SnapMode && !TaskSettings.Fake && (s.At - overlayAt).TotalSeconds >= 1.9)
            {
                overlayAt = s.At; var o = HistoryStore.ReadOverlay(Family.DataDir("Overlay"));
                if (o != null) Hist.OverlayLive(s.At, o[0], o[1], (long)o[2]);
            }
            if (HistLog != null && hp != null) HistLog.Push(hp);
            if (S.Alerts) foreach (var m in alerts.Check(Groups, game, s.At, S.AlertCpu, S.AlertGpu, S.AlertSeconds, SelfPid)) QuietNote(m[0], m[1]);
            if (Eco.On.Count > 0) Eco.On.RemoveAll(x => !s.Procs.Any(p => p.Pid == x.Pid && p.CreateTime == x.Created));
            CacheQuick(s, false);
            var h = Sampled; if (h != null) h();
        }
        DateTime overlayAt = DateTime.MinValue;
        public void OpenLog()
        {
            if (SnapMode) return;
            HistLog = new HistoryLog(Path.Combine(TaskSettings.Dir, "history-24h.jsonl"));
            var l = HistLog; new Thread(() => l.Trim()) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }
        public void SetHistoryLog(bool on)
        {
            S.HistoryLog = on; S.Save();
            if (on && HistLog == null) OpenLog(); else if (!on && HistLog != null) { HistLog.Flush(); HistLog = null; }
        }
        // a quiet note while you play: only through Hallow Hub's notify (Hub shows it from its icon) - never a pop-up of Tasks' own over the game
        public void QuietNote(string title, string text)
        {
            lock (Notes) { Notes.Add(new[] { DateTime.Now.ToString("HH:mm:ss"), title, text }); if (Notes.Count > 50) Notes.RemoveAt(0); }
            Notifications++; Log("alert: " + title);
            if (TaskSettings.TestDir != null || SnapMode) return;
            ThreadPool.QueueUserWorkItem(_ => { try { Family.Send("Hub", "notify Tasks\t" + title + "\t" + text, 2000); } catch { } });
        }
        void SetGame(ProcInfo game)
        {
            bool was = Game != null;
            Game = game; GameTitle = GameDetect.Title(game, HubPlaying);
            if (game != null && !was) { GameSince = DateTime.Now; Log("game: " + game.Name); }
            if (game == null && was) { GameSince = null; Log("game closed"); GameEnded(); Hist.GameEnded(); }
        }
        void GameEnded()
        {
            if (Keeper.Lowered.Count == 0) return;
            int n = Keeper.RestoreAll(); Log("game closed: " + n + " priorities put back");
            if (n > 0) Notify("Priorities are back to normal", "The game closed, so the " + n + " app" + (n == 1 ? "" : "s") + " you lowered are back where they were.");
        }
        void ReadHub()
        {
            if (unchecked(Environment.TickCount - hubReadAt) < 5000) return; hubReadAt = Environment.TickCount;
            HubPlaying = SnapMode || TaskSettings.Fake ? new List<string>() : HubStatus.Playing(Family.DataDir("Hub"));
        }
        // while the window is closed: is the game still running? (a cheap name lookup - only when something was lowered)
        void IdleTick()
        {
            if (Active || Keeper.Lowered.Count == 0) return;
            bool running = false;
            if (Game != null)
            {
                try { var ct = Ctl.CreateTime(Game.Pid); running = ct != 0 && ct == Game.CreateTime; } catch { }
            }
            if (!running && Game != null) SetGame(null);
        }
        void Push(SysStats s)
        {
            Func<string, Ring> r = k => { Ring x; if (!History.TryGetValue(k, out x)) History[k] = x = new Ring(60); return x; };
            r("cpu").Add(s.Cpu);
            for (int i = 0; i < s.Cores.Length; i++) r("core" + i).Add(s.Cores[i]);
            r("gpu3d").Add(s.Gpu3D); r("gpuenc").Add(s.GpuEncode); r("gpudec").Add(s.GpuDecode); r("gpucopy").Add(s.GpuCopy);
            r("ram").Add(s.RamTotal > 0 ? 100.0 * s.RamUsed / s.RamTotal : 0);
            r("vram").Add(s.VramTotal > 0 ? 100.0 * s.VramUsed / s.VramTotal : 0);
            r("diskr").Add(s.DiskRead); r("diskw").Add(s.DiskWrite); r("diskact").Add(Math.Max(0, s.DiskActive));
            r("netup").Add(s.NetUp); r("netdown").Add(s.NetDown);
        }

        // ---------------- window
        public void ShowMain(string page)
        {
            if (TaskSettings.Headless || SnapMode) return;
            if (main == null) { ApplyAccent(); main = new MainWindow(this); }
            if (page != null) main.Show(page);
            main.Show(); if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal; main.Activate();
        }
        public void MainClosed()
        {
            main = null; SetActive(false);
            if (!S.TrayHintShown && !quitting) { S.TrayHintShown = true; S.Save(); Notify("Hallow Tasks is still here", "It waits in the tray without sampling anything. Right-click the icon to quit."); }
        }
        public void Quit()
        {
            if (quitting) return; quitting = true;
            try { if (Keeper.Lowered.Count > 0) Log("quit: " + Keeper.RestoreAll() + " priorities put back"); } catch { }
            try { Net.Stop(); } catch { }
            try { if (HistLog != null) HistLog.Flush(); } catch { }
            if (hotkey != null) hotkey.Dispose();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            WriteStatus(false);
            if (main != null) main.Exiting = true;
            Application.Current.Shutdown();
        }
        public void Say(string text) { if (main != null) main.Say(text); }

        // ---------------- actions (each one only when you click it; the window asks first where it says so)
        ProcInfo Fresh(ProcInfo p)
        {
            if (p == null || Last == null) return p;
            return Last.Procs.FirstOrDefault(x => x.Pid == p.Pid && x.CreateTime == p.CreateTime) ?? p;
        }
        public string WhyNot(ProcInfo p) { return Safety.WhyNot(p, Game, ExtraGames, WindowsDir, SelfPid); }
        public string WhyNot(AppGroup g) { return Safety.WhyNot(g, Game, ExtraGames, WindowsDir, SelfPid); }
        bool Same(ProcInfo p) { var ct = Ctl.CreateTime(p.Pid); return ct != 0 && ct == p.CreateTime; }   // the pid still belongs to that process
        public string EndProcess(ProcInfo p)
        {
            var why = WhyNot(p); if (why != null) return why;
            if (!Same(p)) return p.Name + " has already closed.";
            var e = Ctl.Kill(p.Pid); Log("end " + p.Name + " (" + p.Pid + "): " + (e ?? "ok"));
            return e;
        }
        public string EndGroup(AppGroup g)
        {
            var why = WhyNot(g); if (why != null) return why;
            var errs = new List<string>(); int n = 0;
            foreach (var m in g.Members.Where(x => x.Pid != SelfPid).OrderByDescending(x => x.CreateTime)) { if (!Same(m)) continue; var e = Ctl.Kill(m.Pid); if (e == null) n++; else errs.Add(m.Name + ": " + e); }
            Log("end task " + g.Title + ": " + n + " ended, " + errs.Count + " refused");
            return errs.Count == 0 ? null : string.Join("\n", errs.Distinct().Take(4));
        }
        // the process and everything it started (children first)
        public List<ProcInfo> Tree(ProcInfo root)
        {
            return Grouping.Tree(root, Last == null ? new List<ProcInfo>() : Last.Procs);
        }
        public string EndTree(ProcInfo p)
        {
            var tree = Tree(Fresh(p));
            foreach (var x in tree) { var why = WhyNot(x); if (why != null) return (x.Pid == p.Pid ? "" : "Something it started can't be ended: ") + why; }
            var errs = new List<string>();
            foreach (var x in Enumerable.Reverse(tree)) { if (!Same(x)) continue; var e = Ctl.Kill(x.Pid); if (e != null) errs.Add(x.Name + ": " + e); }
            Log("end tree " + p.Name + ": " + tree.Count + " processes, " + errs.Count + " refused");
            return errs.Count == 0 ? null : string.Join("\n", errs.Distinct().Take(4));
        }
        public string SetPriority(ProcInfo p, Prio to)
        {
            var why = WhyNot(p); if (why != null) return why;
            if (!Same(p)) return p.Name + " has already closed.";
            var e = Ctl.SetPriority(p.Pid, to); Log("priority " + p.Name + " -> " + to + ": " + (e ?? "ok"));
            if (e == null) Keeper.Lowered.RemoveAll(x => x.Pid == p.Pid);   // you chose it yourself: it stays
            return e;
        }
        public string SetAffinity(ProcInfo p, long mask)
        {
            var why = WhyNot(p); if (why != null) return why;
            if (!Same(p)) return p.Name + " has already closed.";
            var e = Ctl.SetAffinity(p.Pid, mask); Log("affinity " + p.Name + ": " + (e ?? "ok")); return e;
        }
        // the game panel: Below normal / Idle while the game runs (put back when it closes)
        public string LowerGroup(AppGroup g, Prio to)
        {
            if (Game == null) return "No game is running.";
            var why = WhyNot(g); if (why != null) return why;
            var errs = new List<string>(); int n = 0;
            foreach (var m in g.Members.Where(x => x.Pid != SelfPid)) { var e = Keeper.Lower(m, to); if (e == null) n++; else errs.Add(e); }
            Log("lower " + g.Title + " -> " + to + ": " + n + " ok, " + errs.Count + " refused");
            return n == 0 && errs.Count > 0 ? errs[0] : null;
        }
        // Efficiency mode (Windows 11 Task Manager's: EcoQoS + Idle priority) - one process, or every process of an app
        public string SetEfficiency(ProcInfo p, bool on)
        {
            var why = WhyNot(p); if (why != null) return why;
            if (!Same(p)) return p.Name + " has already closed.";
            var e = Eco.Set(p, on); Log("efficiency " + p.Name + " " + (on ? "on" : "off") + ": " + (e ?? "ok"));
            if (e == null) Keeper.Lowered.RemoveAll(x => x.Pid == p.Pid);
            return e;
        }
        public string SetEfficiency(AppGroup g, bool on)
        {
            var why = WhyNot(g); if (why != null) return why;
            var errs = new List<string>(); int n = 0;
            foreach (var m in g.Members.Where(x => x.Pid != SelfPid)) { if (!Same(m)) continue; var e = Eco.Set(m, on); if (e == null) { n++; Keeper.Lowered.RemoveAll(x => x.Pid == m.Pid); } else errs.Add(e); }
            Log("efficiency " + g.Title + " " + (on ? "on" : "off") + ": " + n + " ok, " + errs.Count + " refused");
            return n == 0 && errs.Count > 0 ? errs[0] : null;
        }
        public bool EcoOn(AppGroup g) { return g.Members.Any(m => Eco.On.Any(x => x.Pid == m.Pid && x.Created == m.CreateTime)); }
        public bool EcoOn(ProcInfo p) { return Eco.On.Any(x => x.Pid == p.Pid && x.Created == p.CreateTime); }

        // ---------------- services
        public List<ServiceInfo> ServiceList; public bool ServicesReading; public event Action ServicesChanged; public string ServiceBusy;
        public void RefreshServices()
        {
            if (ServicesReading) return; ServicesReading = true;
            Action work = () =>
            {
                List<ServiceInfo> l; try { l = Services.List(); } catch { l = new List<ServiceInfo>(); }
                Action fin = () => { ServiceList = l; ServicesReading = false; var h = ServicesChanged; if (h != null) h(); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "services", Priority = ThreadPriority.BelowNormal }.Start();
        }
        public string WhyNotService(ServiceInfo s)
        {
            if (s.Microsoft && !S.ControlMicrosoftServices) return "It's one of Windows' own services - Hallow Tasks leaves those alone (Settings can change that).";
            string exe = ""; try { exe = System.IO.Path.GetFileName((s.Program ?? "").Trim('"').Split('"')[0].Split(new[] { ".exe " }, StringSplitOptions.None)[0]); } catch { }
            if (Known.IsAntiCheat(exe) || Known.IsAntiCheat(s.Name)) return "That's anti-cheat's service - leave it alone, games react when it's touched.";
            return null;
        }
        // op = start / stop / restart. Without the rights: the elevated helper (UAC) - never in tests
        public void ControlService(ServiceInfo s, string op, Action<string> done)
        {
            var why = WhyNotService(s); if (why != null) { done(why); return; }
            if (ServiceBusy != null) { done("Still working on " + ServiceBusy + "."); return; }
            ServiceBusy = s.Display ?? s.Name; Log("service " + op + " " + s.Name);
            Action work = () =>
            {
                var e = ControlServiceNow(s, op);
                Action fin = () => { ServiceBusy = null; Log("service " + op + " " + s.Name + ": " + (e ?? "ok")); done(e); RefreshServices(); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "service " + op }.Start();
        }

        public string ControlServiceNow(ServiceInfo s, string op)
        {
            var why = WhyNotService(s); if (why != null) return why;
            string e;
            try { e = op == "start" ? Services.Start(s.Name) : op == "stop" ? Services.Stop(s.Name) : Services.Restart(s.Name); } catch (Exception x) { e = x.Message; }
            if (e == "needs admin")
            {
                if (TaskSettings.TestDir != null || SnapMode) e = "needs admin (the test copy never asks Windows for it)";
                else e = WindowsServices.Elevated(op, s.Name);
            }
            return e;
        }

        // ---------------- precise network (the elevated ETW helper; UAC every time you switch it on, stops when Tasks closes)
        public void SetPreciseNetwork(bool on, Action<string> done)
        {
            if (!on) { Net.Stop(); Log("precise network off"); done(null); return; }
            if (TaskSettings.TestDir != null || SnapMode) { if (TaskSettings.Fake || SnapMode) { Net.FakeOn(); done(null); } else done("The test copy never asks Windows for admin."); return; }
            Log("precise network: asking Windows (UAC)");
            Net.Start(e => ui.BeginInvoke(new Action(() => { Log("precise network: " + (e ?? "on")); done(e); })));
        }

        public void RestoreGroup(string name)
        {
            foreach (var e in Keeper.Lowered.Where(x => x.Name == name).ToList()) Keeper.Restore(e.Pid);
        }
        public string OpenLocation(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Windows doesn't say where that program is (it may be protected).";
            if (TaskSettings.TestDir != null || SnapMode) { Log("open location " + path); return null; }
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); return null; } catch (Exception e) { return e.Message; }
        }
        public static string SearchUrl(ProcInfo p) { return "https://www.bing.com/search?q=" + Uri.EscapeDataString("\"" + p.Name + "\"" + (string.IsNullOrEmpty(p.Description) ? "" : " " + p.Description) + " process"); }
        public void SearchOnline(ProcInfo p)
        {
            var url = SearchUrl(p); Log("search online " + p.Name);
            if (TaskSettings.TestDir != null || SnapMode) return;
            try { Process.Start(url); } catch { }
        }
        public static string Details(AppGroup g, ProcInfo p)
        {
            var sb = new StringBuilder();
            var x = p ?? (g == null ? null : g.Main);
            if (g != null && p == null) sb.AppendLine(g.Title + " (" + g.Members.Count + " process" + (g.Members.Count == 1 ? "" : "es") + ")");
            if (x != null)
            {
                sb.AppendLine("Name: " + x.Name); sb.AppendLine("PID: " + x.Pid + (x.ParentPid > 0 ? " (parent " + x.ParentPid + ")" : ""));
                if (x.Description != null) sb.AppendLine("Description: " + x.Description);
                sb.AppendLine("Publisher: " + (x.Signer ?? x.Company ?? "unknown") + (x.Signed == true ? " (signed)" : x.Signed == false ? " (not signed)" : ""));
                sb.AppendLine("Path: " + (x.Path ?? "(Windows doesn't say)"));
                try { sb.AppendLine("Started: " + DateTime.FromFileTime(x.CreateTime).ToString("yyyy-MM-dd HH:mm:ss")); } catch { }
            }
            var cpu = p != null ? p.Cpu : g.Cpu; var ram = p != null ? p.Ram : g.Ram; var gpu = p != null ? p.Gpu : g.Gpu;
            sb.AppendLine("CPU " + Fmt.Pct(cpu) + ", RAM " + Fmt.Bytes(ram) + ", GPU " + Fmt.Pct(gpu));
            return sb.ToString().TrimEnd();
        }
        public void CopyDetails(AppGroup g, ProcInfo p) { try { if (!SnapMode) Clipboard.SetText(Details(g, p)); } catch { } }

        // Scan with Defender: through Hallow Guard when it offers a file scan over its pipe, otherwise Defender's own MpCmdRun
        // (a custom scan of that one file - the same engine Guard uses). The result comes back in the window.
        public bool Scanning;
        public void Scan(string path, Action<string> done)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { done("Windows doesn't say where that program is, so there's nothing to scan."); return; }
            if (Scanning) { done("A scan is already running."); return; }
            Scanning = true; Log("scan " + path);
            new Thread(() =>
            {
                string msg = null;
                var g = Family.Send("Guard", "scan file " + path, 3000);
                if (g != null && (g == "ok" || g.StartsWith("ok "))) msg = "Hallow Guard is scanning it - the result shows in Hallow Guard.";
                if (msg == null) msg = DefenderScan.Run(path);
                ui.BeginInvoke(new Action(() => { Scanning = false; done(msg); }));
            }) { IsBackground = true, Name = "scan" }.Start();
        }

        // ---------------- startup apps
        public List<StartupItem> StartupItems; public List<DisabledEntry> Disabled = new List<DisabledEntry>(); public bool StartupReading; HashSet<string> slow;
        public event Action StartupChanged;
        public void RefreshStartup()
        {
            if (StartupReading) return; StartupReading = true;
            var procs = Last == null ? null : Last.Procs;
            Action work = () =>
            {
                List<StartupItem> l; List<DisabledEntry> d;
                try { l = startupSrc.Read(); } catch { l = new List<StartupItem>(); }
                try { if (slow == null) slow = startupSrc.SlowExes(); } catch { slow = new HashSet<string>(); }
                if (procs == null) { try { lock (sampleLock) procs = sampler.Take().Procs; } catch { procs = new List<ProcInfo>(); } }
                StartupRate.Apply(l, procs, slow, DateTime.Now, p => p.CpuTime / 1e7);
                d = Store.List();
                if (SnapMode) d = new List<DisabledEntry> { new DisabledEntry { Id = "OneDrive", Name = "OneDrive", Data = @"""C:\Users\Alex\AppData\Local\Microsoft\OneDrive\OneDrive.exe"" /background", When = DateTime.Now.AddDays(-2).ToString("o") } };
                Action fin = () => { StartupItems = l.OrderBy(x => StartupImpact.Rank(x.Impact)).ThenBy(x => x.Name).ToList(); Disabled = d; StartupReading = false; var h = StartupChanged; if (h != null) h(); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "startup", Priority = ThreadPriority.BelowNormal }.Start();
        }
        public string DisableStartup(StartupItem i)
        {
            if (!i.Mine) return "Only your own startup entries can be turned off here - this one is for all users. Use Windows' Startup apps page.";
            if (SnapMode || TaskSettings.Fake) { Log("disable startup " + i.Name); return null; }
            var e = Store.Disable(i.Name); Log("disable startup " + i.Name + ": " + (e ?? "ok")); RefreshStartup(); return e;
        }
        public string RestoreStartup(DisabledEntry d)
        {
            var e = Store.Restore(d.Id); Log("restore startup " + d.Name + ": " + (e ?? "ok")); RefreshStartup(); return e;
        }
        public void OpenWindowsStartup() { if (TaskSettings.TestDir != null || SnapMode) return; try { Process.Start("ms-settings:startupapps"); } catch { } }

        // ---------------- settings
        const string RunName = "HallowTasks";
        string RunFile { get { return Path.Combine(TaskSettings.Dir, "test-autostart.txt"); } }
        public bool StartsWithWindows()
        {
            if (TaskSettings.TestDir != null || SnapMode) return File.Exists(RunFile);
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupStore.UserRun)) return k != null && k.GetValue(RunName) != null; } catch { return false; }
        }
        public void SetStartWithWindows(bool on)
        {
            if (SnapMode) return;
            if (TaskSettings.TestDir != null) { if (on) File.WriteAllText(RunFile, "\"HallowTasks.exe\" --tray"); else if (File.Exists(RunFile)) File.Delete(RunFile); return; }
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(StartupStore.UserRun)) { if (on) k.SetValue(RunName, "\"" + Assembly.GetExecutingAssembly().Location + "\" --tray"); else k.DeleteValue(RunName, false); } } catch { }
        }
        // Ctrl+Alt+T (Ctrl+Shift+Esc belongs to Windows' Task Manager and can't be taken over safely)
        public string SetHotkey(bool on, bool save = true)
        {
            string err = null;
            if (hotkey != null) { hotkey.Dispose(); hotkey = null; }
            if (on && !TaskSettings.Headless && !SnapMode)
            {
                hotkey = new Hotkey(() => ShowMain(null));
                if (!hotkey.Ok) { hotkey.Dispose(); hotkey = null; err = "Ctrl+Alt+T is already used by another program, so it couldn't be set."; on = false; }
            }
            if (save) { S.Hotkey = on; S.Save(); }
            Log("hotkey " + (on ? "on" : "off") + (err != null ? " (" + err + ")" : ""));
            return err;
        }
        public bool HotkeyOn { get { return hotkey != null; } }
        public void SetExtraGames(string text)
        {
            S.ExtraGames = (text ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
            S.Save();
        }

        // ---------------- tray, the Hallow icon (Hub hosts it), notifications, accent, updates, crash notice
        void SetupTray()
        {
            tray = new WF.NotifyIcon { Text = "Hallow Tasks", Visible = true };
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            var menu = new WF.ContextMenuStrip();
            var open = new WF.ToolStripMenuItem("Open Hallow Tasks"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold); open.Click += (s, e) => ShowMain("processes"); menu.Items.Add(open);
            foreach (var it in MenuItems().Cast<Dictionary<string, object>>())
            {
                var id = (string)it["id"]; if (id == "-") { menu.Items.Add(new WF.ToolStripSeparator()); continue; }
                var mi = new WF.ToolStripMenuItem((string)it["label"]); mi.Click += (s, e) => Invoke(id); menu.Items.Add(mi);
                if (id == "hotkey") menu.Opening += (s, e) => mi.Checked = HotkeyOn;
            }
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowMain(null); };
        }
        public void CheckHost()
        {
            if (tray == null) return;
            if (!S.UseHallowIcon) { SetHosted(false, false); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool on = false, lists = false;
                try { var r = Family.Send("Hub", "tray-host", 1500); if (r != null && r.StartsWith("ok {")) HubHosting(r.Substring(3), out on, out lists); } catch { }
                ui.BeginInvoke(new Action(() => SetHosted(on && S.UseHallowIcon, lists)));
            });
        }
        // Hub's "tray-host" answer: on = Hub's Hallow icon is showing; lists = Hub's menu has Hallow Tasks ("apps" has "Tasks", or
        // "generic":true - Hub 2.13+ lists any app answering "menu"). Tasks only hides its own icon when Hub lists it.
        public static void HubHosting(string json, out bool on, out bool lists)
        {
            on = false; lists = false;
            try
            {
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); object v;
                on = d != null && d.TryGetValue("on", out v) && v is bool && (bool)v;
                if (!on) return;
                if (d.TryGetValue("generic", out v) && v is bool && (bool)v) { lists = true; return; }
                if (d.TryGetValue("apps", out v) && v is System.Collections.IEnumerable && !(v is string))
                    foreach (var a in (System.Collections.IEnumerable)v) if (a is string && string.Equals((string)a, "Tasks", StringComparison.OrdinalIgnoreCase)) lists = true;
            }
            catch { on = false; lists = false; }
        }
        void SetHosted(bool on, bool lists) { hubHosts = on; hubLists = on && lists; if (tray != null && !balloonOwn) tray.Visible = !hubLists; }
        public int Notifications;
        public void Notify(string title, string text)
        {
            Notifications++; Log("notify: " + title);
            if (tray == null || TaskSettings.TestDir != null) return;
            if (!hubHosts) { tray.ShowBalloonTip(5000, title, text, WF.ToolTipIcon.None); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (Family.Error(Family.Send("Hub", "notify Tasks\t" + title + "\t" + text, 2000), "no answer") == null) return;
                ui.BeginInvoke(new Action(() => { balloonOwn = true; tray.Visible = true; tray.ShowBalloonTip(5000, title, text, WF.ToolTipIcon.None); var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) }; t.Tick += (s, e) => { t.Stop(); balloonOwn = false; SetHosted(hubHosts, hubLists); }; t.Start(); }));
            });
        }
        // the accent and light / dark: reloaded only when one of them changed (theme.json, Settings > Look, or Windows' mode)
        public void ApplyAccent()
        {
            if (SnapMode) return;
            var a = Accent.Current(S.UseHallowAccent); var light = Look.Current(S);
            if (a == Program.AccentHex && light == Program.Light) return;
            Program.LoadTheme(Application.Current, S); Log("look: " + (Program.Light ? "light" : "dark") + ", accent " + Program.AccentHex);
            if (main != null) { main.Rebuild(); Ui.TitleBar(main); }
        }
        public void SetLook(string look) { S.Look = look == "light" || look == "dark" ? look : "hallow"; S.Save(); ApplyAccent(); }
        GhRelease pending; public GhRelease PendingUpdate { get { return pending; } }
        public void CheckForUpdate(bool manual)
        {
            if (manual) Say("Checking GitHub for a newer Hallow Tasks...");
            new Thread(() =>
            {
                var r = Updater.Latest();
                ui.BeginInvoke(new Action(() =>
                {
                    S.LastUpdateCheck = DateTime.Now.ToString("o"); S.Save();
                    if (r == null) { if (manual) Say("Couldn't reach GitHub - try again later."); return; }
                    if (!Updater.IsNewer(r)) { if (manual) Say("You have the newest version (v" + Program.Version + ")."); return; }
                    pending = r; if (main != null) main.ShowUpdate("Update to v" + r.Version.ToString(r.Version.Build > 0 ? 3 : 2));
                    Say("Hallow Tasks v" + r.Version.ToString(2) + " is out - click Update at the top.");
                }));
            }) { IsBackground = true }.Start();
        }
        public void InstallUpdate()
        {
            if (pending == null) return;
            if (!Dialogs.Confirm(main, "Install Hallow Tasks v" + pending.Version.ToString(2) + "?", "It downloads from GitHub, checks the file and puts the new version in place. Your settings stay." + (Keeper.Lowered.Count > 0 ? " Apps you lowered go back to normal first." : ""), "Update", false)) return;
            var r = pending; Say("Updating...");
            if (Keeper.Lowered.Count > 0) Keeper.RestoreAll();
            new Thread(() => { var err = Updater.Install(r); ui.BeginInvoke(new Action(() => Say(err ?? "Installing the update..."))); }) { IsBackground = true }.Start();
        }
        void ShowCrashNotice()
        {
            var f = Crash.Last(); if (f == null) return;
            var name = Path.GetFileName(f); if (name == S.LastCrashShown) return;
            S.LastCrashShown = name; S.Save();
            CrashNotice.Make(f).Show();
        }

        // ---------------- family link: status.json and the pipe
        // the top users right now: from the window's newest sample, or (window closed) two quick samples 0.7 s apart
        // v1.2: the newest sample when it's at most 5 s old (no new sample at all); one quick sample when the last one was taken
        // within 15 s (its CPU numbers cover the time since); otherwise the two samples as before
        List<AppGroup> FreshGroups(out Sample s)
        {
            s = Last; var g = Groups;
            if (s != null && g != null && s != firstTake && (DateTime.Now - s.At).TotalSeconds <= QuickMaxAge) return g;
            lock (sampleLock)
            {
                if ((DateTime.Now - lastTakeAt).TotalSeconds > 15) { Take(); Thread.Sleep(700); }
                s = Take();
            }
            return Grouping.Build(s.Procs, sampler.WindowsDir, ExtraGames);
        }

        // ---- v1.2: a cached sample for Remote / Hub that is never more than 5 s old. The pipe's "quick" answers from it at once
        // (a quick sample first only when it's older), and while Hub or Remote runs (or someone asked in the last 2 minutes)
        // status.json is rewritten with every sample (about every 5 s) instead of every 60 s. Tasks' own sampling stays the
        // tray's 5 s sample it already takes with History on.
        public const int QuickMaxAge = 5, LeaseSeconds = 120; public const double BusyPercent = 5;
        // apps (groups) using at least 5% of the CPU or the GPU; the idle process never counts
        public static int BusyCount(IEnumerable<AppGroup> groups) { return groups.Count(g => g.Key != "idle" && !(g.Members.Count == 1 && g.Members[0].Pid == 0) && (g.Cpu >= BusyPercent || g.Gpu >= BusyPercent)); }
        string quickJson; DateTime quickAt = DateTime.MinValue, quickFileAt = DateTime.MinValue; int leaseUntil = Environment.TickCount;
        bool Leased { get { return unchecked(leaseUntil - Environment.TickCount) > 0; } }
        bool consumers; int consumersAt = Environment.TickCount - 60000; bool consumersChecking;
        // is Hallow Hub or Hallow Remote running? (looked up at most every 30 s, off the UI thread)
        bool ConsumersRunning()
        {
            if (unchecked(Environment.TickCount - consumersAt) > 30000 && !consumersChecking)
            {
                consumersAt = Environment.TickCount; consumersChecking = true;
                ThreadPool.QueueUserWorkItem(_ => { try { consumers = Family.Running("Hub") || Family.Running("Remote"); } catch { } consumersChecking = false; });
            }
            return consumers;
        }
        // on the UI thread, after each sample: the cached reply (at most every 1.5 s) and status.json (at most every 4.5 s, only while wanted)
        void CacheQuick(Sample s, bool force)
        {
            if (SnapMode || quitting || s == null || s == firstTake) return;
            if (!force && (s.At - quickAt).TotalSeconds < 1.5 && quickJson != null) return;
            var json = Family.Json(StatusData(true, s, Groups)); quickJson = json; quickAt = s.At;
            if ((Leased || consumers) && (DateTime.Now - quickFileAt).TotalSeconds >= 4.5)
            {
                quickFileAt = DateTime.Now;
                ThreadPool.QueueUserWorkItem(_ => Family.WriteStatus(json));
            }
        }
        string Quick()
        {
            leaseUntil = Environment.TickCount + LeaseSeconds * 1000;
            var j = quickJson;
            if (j != null && (DateTime.Now - quickAt).TotalSeconds <= QuickMaxAge) return "ok " + j;
            Sample s; FreshGroups(out s);   // on the pipe's thread, never the UI's
            var r = OnUiValue(() => { if (Last == null || s.At > Last.At) Apply(s); else if (quickAt < s.At) CacheQuick(s, true); return quickJson ?? Family.Json(StatusData(true, s, Grouping.Build(s.Procs, sampler.WindowsDir, ExtraGames))); });
            return r == null ? "error no answer in time" : "ok " + r;
        }
        public Dictionary<string, object> StatusData(bool running, Sample s, List<AppGroup> groups)
        {
            var d = new Dictionary<string, object> { { "app", "Tasks" }, { "version", Program.Version }, { "updated", Family.Time(DateTime.Now) }, { "running", running } };
            if (s != null && groups != null)
            {
                var others = groups.Where(g => g.Key != "idle" && !(g.Members.Count == 1 && g.Members[0].Pid == 0)).ToList();
                d["cpu"] = Math.Round(s.Sys.Cpu, 1); d["ram"] = s.Sys.RamTotal > 0 ? (object)Math.Round(100.0 * s.Sys.RamUsed / s.Sys.RamTotal, 1) : null;
                d["gpu"] = s.Sys.GpuKnown ? (object)Math.Round(s.Sys.Gpu3D, 1) : null;
                d["topCpu"] = others.OrderByDescending(g => g.Cpu).Take(5).Select(g => new Dictionary<string, object> { { "name", g.Title }, { "cpu", Math.Round(g.Cpu, 1) } }).ToList();
                d["topGpu"] = others.Where(g => g.Gpu > 0).OrderByDescending(g => g.Gpu).Take(5).Select(g => new Dictionary<string, object> { { "name", g.Title }, { "gpu", Math.Round(g.Gpu, 1) } }).ToList();
                d["processes"] = s.Procs.Count; d["sampled"] = Family.Time(s.At);
                // v1.2: how many apps are busy right now (a count only, no names): at least 5% of the CPU or the GPU
                d["busyApps"] = BusyCount(others); d["busyPercent"] = BusyPercent;
            }
            var game = s != null && !Active ? GameDetect.Find(s.Procs, ExtraGames, HubPlaying) : Game;
            d["game"] = game == null ? null : GameDetect.Title(game, HubPlaying); d["lowered"] = Keeper.Lowered.Count; d["sampling"] = Active;
            d["hotkey"] = S.Hotkey; d["startsWithWindows"] = StartsWithWindows();
            d["look"] = Program.Light ? "light" : "dark"; d["history"] = Hist.Count; d["historyInTray"] = S.HistoryInTray; d["historyLog"] = S.HistoryLog; d["alerts"] = S.Alerts;
            d["efficiency"] = Eco.On.Count; d["preciseNetwork"] = Net.On;
            d["lastCrash"] = Crash.LastTime() == null ? null : Family.Time(Crash.LastTime().Value);
            if (TaskSettings.TestDir != null) { lock (log) d["log"] = log.ToArray(); d["notifications"] = Notifications; }
            return d;
        }
        public void WriteStatus(bool running = true) { if (!SnapMode) Family.WriteStatus(Family.Json(StatusData(running, Active ? Last : null, Active ? Groups : null))); }
        void WriteStatusFresh()
        {
            if (SnapMode || quitting) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Sample s; var g = FreshGroups(out s); var d = StatusData(true, s, g); Family.WriteStatus(Family.Json(d)); } catch { }
            });
        }
        string Command(string line)
        {
            int sp = line.IndexOf(' '); var cmd = sp < 0 ? line : line.Substring(0, sp); var arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            switch (cmd)
            {
                case "ping": return "ok " + Family.Json(new Dictionary<string, object> { { "app", "Tasks" }, { "version", Program.Version } });
                case "status":
                    {
                        Sample s; var g = FreshGroups(out s);   // on the pipe's thread (at most ~1 s), never the UI's
                        var d = OnUiValue(() => StatusData(true, s, g)); if (d == null) return "error no answer in time";
                        Family.WriteStatus(Family.Json(d)); return "ok " + Family.Json(d);
                    }
                case "quick": return Quick();   // v1.2: the cached sample (at most 5 s old) - same fields as status, answered at once
                case "open": return OnUi(() => { var p = arg.Length > 0 ? MainWindow.PageId(arg) : null; if (arg.Length > 0 && p == null) return "error no page called " + arg; ShowMain(p); return "ok"; });
                case "menu": return OnUi(() => "ok " + Family.Json(new Dictionary<string, object> { { "app", "Tasks" }, { "items", MenuItems() } }));
                case "invoke": return OnUi(() => Invoke(arg));
                case "quit": new Thread(() => { Thread.Sleep(300); ui.BeginInvoke(new Action(Quit)); }) { IsBackground = true }.Start(); return "ok";
            }
            if (TaskSettings.TestDir != null && cmd.StartsWith("test-")) return OnUi(() => TestCommand(cmd, arg));
            return "error unknown command";
        }
        string OnUi(Func<string> f)
        {
            string r = "error no answer in time";
            var op = ui.BeginInvoke(new Action(() => { try { r = f(); } catch (Exception e) { r = "error " + Family.OneLine(e.Message); } }));
            return op.Wait(TimeSpan.FromSeconds(5)) == DispatcherOperationStatus.Completed ? r : "error no answer in time";
        }
        T OnUiValue<T>(Func<T> f) where T : class
        {
            T r = null; var op = ui.BeginInvoke(new Action(() => { try { r = f(); } catch { } }));
            return op.Wait(TimeSpan.FromSeconds(5)) == DispatcherOperationStatus.Completed ? r : null;
        }
        public List<object> MenuItems()
        {
            return new List<object> {
                new Dictionary<string, object> { { "id", "game" }, { "label", "What's slowing my game" } },
                new Dictionary<string, object> { { "id", "performance" }, { "label", "Performance" } },
                new Dictionary<string, object> { { "id", "history" }, { "label", "History - what spiked my PC" } },
                new Dictionary<string, object> { { "id", "startup" }, { "label", "Startup apps" } },
                new Dictionary<string, object> { { "id", "services" }, { "label", "Services" } },
                new Dictionary<string, object> { { "id", "-" } },
                new Dictionary<string, object> { { "id", "hotkey" }, { "label", "Ctrl+Alt+T opens Hallow Tasks" }, { "checked", S.Hotkey } } };
        }
        string Invoke(string id)
        {
            switch (id)
            {
                case "game": ShowMain("game"); return "ok";
                case "performance": ShowMain("performance"); return "ok";
                case "startup": ShowMain("startup"); return "ok";
                case "history": ShowMain("history"); return "ok";
                case "services": ShowMain("services"); return "ok";
                case "processes": ShowMain("processes"); return "ok";
                case "hotkey": { var e = SetHotkey(!S.Hotkey); if (main != null) main.Refresh(); return e == null ? "ok" : "error " + e; }
            }
            return "error unknown item";
        }
        // developer tests only (HALLOW_TASKS_TESTDIR): what a person would click, without windows. With HALLOW_TASKS_FAKE=1 the
        // processes are made up and the process control only remembers calls - nothing real is ended or changed.
        string TestCommand(string cmd, string arg)
        {
            switch (cmd)
            {
                case "test-sample": SampleNow(); return "ok " + Family.Json(new Dictionary<string, object> { { "groups", Groups.Count }, { "game", Game == null ? null : Game.Name }, { "ms", Math.Round(SampleMs, 1) } });
                case "test-active": SetActive(arg == "on"); return "ok";
                case "test-autostart": SetStartWithWindows(arg == "on"); return "ok " + StartsWithWindows();
                case "test-games": SetExtraGames(arg); return "ok " + S.ExtraGames.Count;
                case "test-look": ApplyAccent(); return "ok " + (Program.Light ? "light" : "dark");
                case "test-setlook": SetLook(arg); return "ok " + (Program.Light ? "light" : "dark");
                case "test-history": return "ok " + Family.Json(new Dictionary<string, object> { { "points", Hist.Count }, { "spikes", HistoryStore.FindSpikes(Hist.Points(DateTime.MinValue)).Select(x => x.Title).ToList() }, { "log", HistLog != null } });
                case "test-historylog": SetHistoryLog(arg == "on"); if (HistLog != null) { SampleNow(); HistLog.Flush(); } return "ok " + (HistLog != null);
            }
            if (!TaskSettings.Fake) return "error unknown test command";
            var fc = (FakeControl)Ctl;
            switch (cmd)
            {
                case "test-game": SnapGame(arg == "on"); SampleNow(); return "ok " + (Game == null ? "none" : Game.Name);
                case "test-rank": return "ok " + Family.Json(Watch.Rank(Groups, Game, 20).Select(r => r.Group.Title + (r.Tag != null ? " [" + r.Tag + "]" : "")).ToList());
                case "test-lower":
                    {
                        var p = arg.Split(' '); var g = Groups.FirstOrDefault(x => x.Key == p[0] || x.Key.StartsWith(p[0] + "|")); if (g == null) return "error no group " + p[0];
                        var e = LowerGroup(g, p.Length > 1 && p[1] == "idle" ? Prio.Idle : Prio.BelowNormal); return e == null ? "ok " + Keeper.Lowered.Count : "error " + e;
                    }
                case "test-end":
                    {
                        var g = Groups.FirstOrDefault(x => x.Key == arg || x.Key.StartsWith(arg + "|")); if (g == null) return "error no group " + arg;
                        var e = EndGroup(g); return e == null ? "ok" : "error " + e;
                    }
                case "test-calls": return "ok " + Family.Json(fc.Calls);
                case "test-prio": { Prio pr; return fc.Prios.TryGetValue(int.Parse(arg), out pr) ? "ok " + pr : "ok Normal"; }
                case "test-eco":
                    {
                        var p = arg.Split(' '); var g = Groups.FirstOrDefault(x => x.Key == p[0] || x.Key.StartsWith(p[0] + "|")); if (g == null) return "error no group " + p[0];
                        var e = SetEfficiency(g, p.Length < 2 || p[1] != "off"); return e == null ? "ok " + fc.Eco.Count : "error " + e;
                    }
                case "test-alerts":
                    {
                        var p = arg.Split(' '); S.Alerts = true; S.AlertCpu = int.Parse(p[0]); S.AlertGpu = int.Parse(p[1]); S.AlertSeconds = int.Parse(p[2]);   // no clamping: the test uses 1 s
                        return "ok";
                    }
                case "test-notes": lock (Notes) return "ok " + Family.Json(Notes.Select(n => n[1]).ToList());
                case "test-services": { var l = Services.List(); return "ok " + Family.Json(l.Select(x => x.Name + "|" + x.Status + "|" + (x.Microsoft ? "ms" : "3p") + "|" + (x.CanControl ? "ctl" : "admin")).ToList()); }
                case "test-svc":
                    {
                        int sp2 = arg.IndexOf(' '); var op = arg.Substring(0, sp2); var name = arg.Substring(sp2 + 1);
                        var s = Services.List().FirstOrDefault(x => x.Name == name); if (s == null) return "error no service";
                        var r = ControlServiceNow(s, op);
                        return r == null ? "ok " + Family.Json(((FakeServices)Services).Calls) : "error " + r;
                    }
                case "test-net":
                    {
                        Net.FakeOn(); Net.Feed(arg); SampleNow(); var g = Groups.FirstOrDefault(x => x.Key == "chrome");
                        return "ok " + (g == null ? -2 : g.NetBytes);
                    }
            }
            return "error unknown test command";
        }
    }

    // a small ring of numbers for the graphs (oldest first)
    public class Ring
    {
        readonly double[] v; int start, count;
        public Ring(int size) { v = new double[size]; }
        public void Add(double x) { if (double.IsNaN(x) || double.IsInfinity(x)) x = 0; if (count < v.Length) v[(start + count++) % v.Length] = x; else { v[start] = x; start = (start + 1) % v.Length; } }
        public int Count { get { return count; } }
        public int Size { get { return v.Length; } }
        public double this[int i] { get { return v[(start + i) % v.Length]; } }
        public double LastValue { get { return count == 0 ? 0 : this[count - 1]; } }
        public double Max() { double m = 0; for (int i = 0; i < count; i++) m = Math.Max(m, this[i]); return m; }
    }

    // Hallow Hub's status.json: which games it says are being played (only while Hub runs)
    public static class HubStatus
    {
        public static List<string> Playing(string hubDir)
        {
            var r = new List<string>();
            try
            {
                var f = Path.Combine(hubDir, "status.json"); if (!File.Exists(f)) return r;
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f)); object v;
                if (d == null || !d.TryGetValue("running", out v) || !(v is bool) || !(bool)v) return r;
                if (d.TryGetValue("playing", out v) && v is System.Collections.IEnumerable && !(v is string))
                    foreach (var x in (System.Collections.IEnumerable)v)
                    {
                        if (x is string) r.Add((string)x);
                        else { var o = x as Dictionary<string, object>; object n; if (o != null && o.TryGetValue("name", out n) && n is string) r.Add((string)n); }
                    }
            }
            catch { }
            return r;
        }
    }

    // Defender's own command-line scanner (the engine Hallow Guard uses): one file, a custom scan
    public static class DefenderScan
    {
        public static string Find()
        {
            if (TaskSettings.TestDir != null) { var t = Environment.GetEnvironmentVariable("HALLOW_TASKS_MPCMDRUN"); return string.IsNullOrEmpty(t) ? null : t; }
            try
            {
                var plat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Platform");
                if (Directory.Exists(plat))
                {
                    var best = Directory.GetDirectories(plat).Select(d => { Version v; return new { d, v = Version.TryParse(Path.GetFileName(d).Split('-')[0], out v) ? v : null }; })
                        .Where(x => x.v != null && File.Exists(Path.Combine(x.d, "MpCmdRun.exe"))).OrderByDescending(x => x.v).FirstOrDefault();
                    if (best != null) return Path.Combine(best.d, "MpCmdRun.exe");
                }
            }
            catch { }
            var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
            return File.Exists(pf) ? pf : null;
        }
        public static string Args(string file)
        {
            if (string.IsNullOrEmpty(file) || file.IndexOf('"') >= 0) throw new ArgumentException("bad path");
            return "-Scan -ScanType 3 -File \"" + file + "\" -DisableRemediation";
        }
        // exit 0 = nothing found, 2 = threats found
        public static string Verdict(int exit, string output)
        {
            var names = System.Text.RegularExpressions.Regex.Matches(output ?? "", @"Threat\s*:\s*(.+)").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value.Trim()).Distinct().ToList();
            if (exit == 2 || names.Count > 0) return "Microsoft Defender found something: " + (names.Count > 0 ? string.Join(", ", names) : "a threat") + ". Open Windows Security (or Hallow Guard) to deal with it.";
            if (exit == 0) return "Microsoft Defender found nothing in it.";
            return "Microsoft Defender couldn't scan it (code " + exit + ").";
        }
        public static string Run(string file)
        {
            var exe = Find(); if (exe == null) return "Microsoft Defender's scanner wasn't found on this PC.";
            try
            {
                var psi = new ProcessStartInfo(exe, Args(file)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    var outTask = p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(180000)) { try { p.Kill(); } catch { } return "The scan took too long and was stopped."; }
                    return Verdict(p.ExitCode, outTask.Result);
                }
            }
            catch (Exception e) { return "Couldn't start the scan: " + e.Message; }
        }
    }

    // Ctrl+Alt+T: a system-wide hotkey on a message-only window (RegisterHotKey - fails politely when another program has it)
    public class Hotkey : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, int mods, int vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        readonly System.Windows.Interop.HwndSource src; public readonly bool Ok;
        public Hotkey(Action pressed)
        {
            src = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("HallowTasksHotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
            src.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) => { if (msg == 0x0312 && w.ToInt32() == 0x7A5) { handled = true; pressed(); } return IntPtr.Zero; });
            Ok = RegisterHotKey(src.Handle, 0x7A5, 0x0001 /*ALT*/ | 0x0002 /*CTRL*/ | 0x4000 /*NOREPEAT*/, 0x54 /*T*/);
        }
        public void Dispose() { try { UnregisterHotKey(src.Handle, 0x7A5); } catch { } try { src.Dispose(); } catch { } }
    }
}
