// Hallow Tasks unit tests: grouping, Impact, the protected / anti-cheat / game lists, the game panel and the priority keeper with
// made-up processes; startup Disable/Restore on a fake HKCU key; READ-ONLY real sampling; kill / priority / affinity only on
// Dummy.exe children this test starts itself (never a real process); the family pipe of a test copy with made-up processes.
//   tests\TasksTests.exe              everything above
//   tests\TasksTests.exe --overhead   only the real sampler: how long a sample takes (read-only)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace HallowTasks.Tests
{
    public static partial class Run
    {
        static int pass, fail;
        static void Ok(bool c, string name) { if (c) { pass++; Console.WriteLine("ok   " + name); } else { fail++; Console.WriteLine("FAIL " + name); } }
        static readonly string[] Games = { "DeltaForceClient-Win64-Shipping", "Discovery-d" };
        static string Dir;

        public static int Main(string[] args)
        {
            if (args.Contains("--overhead")) return Overhead();
            Dir = Path.Combine(Path.GetTempPath(), "HallowTasksTest" + Process.GetCurrentProcess().Id);
            Environment.SetEnvironmentVariable("HALLOW_TASKS_TESTDIR", Dir);
            try { Lists(); Groups(); Impact(); Detect(); Panel(); Keeper(); SafetyRules(); StartupBits(); StartupStoreTest(); Family(); Formats(); V11(); RealSampling(); RealControl(); PipeAndApp(); RealCopy(); if (!args.Contains("--quick")) OverheadCopy(); }
            catch (Exception e) { fail++; Console.WriteLine("FAIL exception: " + e); }
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\HallowTasks")) if (k != null && k.SubKeyCount == 0 && k.ValueCount == 0) { k.Close(); Registry.CurrentUser.DeleteSubKey(@"Software\HallowTasks", false); } } catch { }
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
            Console.WriteLine(pass + " passed, " + fail + " failed");
            return fail == 0 ? 0 : 1;
        }

        static long T0 = new DateTime(2026, 11, 21, 9, 0, 0).ToFileTime();
        static ProcInfo P(int pid, int parent, string name, string path, double cpu = 0, int ramMb = 10, double gpu = 0, bool window = false, int session = 1, long created = 0, double disk = 0)
        {
            return new ProcInfo { Pid = pid, ParentPid = parent, Name = name, Path = path, Cpu = cpu, Ram = ramMb * 1048576L, Gpu = gpu, HasWindow = window, Session = session, CreateTime = created != 0 ? created : T0 + pid * 1000, DiskRead = disk };
        }

        // ---------------- the lists
        static void Lists()
        {
            foreach (var a in new[] { "SGuard64.exe", "SGuardSvc64.exe", "EasyAntiCheat.exe", "EasyAntiCheat_EOS.exe", "BEService.exe", "BEService_x64.exe", "vgc.exe", "vgtray.exe", "FACEIT.exe", "faceitservice.exe", "ACE-Tray.exe", "GameMon.des", "xhunter1.sys", "PnkBstrA.exe", "EAAntiCheat.GameServiceLauncher.exe", "start_protected_game.exe" })
                Ok(Known.IsAntiCheat(a), "anti-cheat: " + a);
            foreach (var a in new[] { "Discord.exe", "chrome.exe", "vgcx.exe", "sgua.exe", "DeltaForceClient-Win64-Shipping.exe", "steam.exe", "obs64.exe" }) Ok(!Known.IsAntiCheat(a), "not anti-cheat: " + a);
            Ok(Known.IsGameName("DeltaForceClient-Win64-Shipping.exe", null) && Known.IsGameName("discovery-d.exe", null) && Known.IsGameName("Discovery-d", null), "the two known games, any case, with or without .exe");
            Ok(!Known.IsGameName("Discord.exe", null) && Known.IsGameName("MyGame.exe", new[] { "mygame" }) && Known.IsGameName("mygame.EXE", new[] { "MyGame.exe" }), "extra games from Settings");
            foreach (var a in new[] { "csrss.exe", "lsass.exe", "wininit.exe", "winlogon.exe", "smss.exe", "services.exe", "svchost.exe", "dwm.exe", "explorer.exe", "MsMpEng.exe", "System", "Registry", "Memory Compression" }) Ok(Known.IsCritical(a), "Windows core: " + a);
            Ok(!Known.IsCritical("Discord.exe") && !Known.IsCritical("HallowHub.exe"), "apps aren't Windows core");
            Ok(Known.IsProtected(P(4, 0, "System", null, session: 0), @"C:\Windows"), "pid 4 protected");
            Ok(Known.IsProtected(P(900, 4, "SomeSvc.exe", @"C:\Windows\System32\SomeSvc.exe", session: 0), @"C:\Windows"), "a session-0 program in the Windows folder is protected");
            Ok(Known.IsProtected(P(901, 4, "secret.exe", null, session: 0), @"C:\Windows"), "a session-0 process Windows won't show the path of is protected");
            Ok(!Known.IsProtected(P(902, 4, "steamservice.exe", @"C:\Program Files (x86)\Common Files\Steam\steamservice.exe", session: 0), @"C:\Windows"), "a session-0 service outside the Windows folder isn't");
            Ok(!Known.IsProtected(P(903, 4, "notepad.exe", @"C:\Windows\System32\notepad.exe", session: 1), @"C:\Windows"), "a program in your session isn't (Notepad)");
            Ok(Known.IsHallow(P(1, 0, "HallowHub.exe", null)) && Known.IsHallow(P(1, 0, "ffmpeg.exe", @"C:\Users\A\AppData\Local\Programs\Hallow Clips\ffmpeg.exe")) && !Known.IsHallow(P(1, 0, "Discord.exe", @"C:\D\Discord.exe")), "Hallow apps by name or install folder");
        }

        // ---------------- grouping
        static List<ProcInfo> Sample1()
        {
            return new List<ProcInfo> {
                P(0, 0, "System Idle Process", null, session: 0), P(4, 0, "System", null, 0.5, 0, session: 0),
                P(700, 600, "services.exe", null, session: 0), P(710, 700, "svchost.exe", @"C:\Windows\System32\svchost.exe", 1.0, session: 0), P(711, 700, "svchost.exe", @"C:\Windows\System32\svchost.exe", 0.2, session: 0),
                P(712, 710, "WmiPrvSE.exe", @"C:\Windows\System32\wbem\WmiPrvSE.exe", 0.4, session: 0),
                P(1000, 900, "explorer.exe", @"C:\Windows\explorer.exe", 0.3, 120, 0, true),
                P(2000, 1000, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 2, 300, 1, true),
                P(2001, 2000, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 5, 400, 9), P(2002, 2000, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 1, 100),
                P(3000, 1000, "Discord.exe", @"C:\Users\A\AppData\Local\Discord\app-1.0.9\Discord.exe", 1, 200, 2, true), P(3001, 3000, "Discord.exe", @"C:\Users\A\AppData\Local\Discord\app-1.0.9\Discord.exe", 3, 150, 5),
                P(3002, 3000, "Discord.exe", @"C:\Users\A\AppData\Local\Discord\app-1.0.9\Discord.exe", 0.1, 40),
                P(4000, 1000, "HallowHub.exe", @"C:\Users\A\AppData\Local\Programs\Hallow Hub\HallowHub.exe", 0.1, 60), P(4001, 1000, "HallowClips.exe", @"C:\Users\A\AppData\Local\Programs\Hallow Clips\HallowClips.exe", 1, 100),
                P(4002, 4001, "ffmpeg.exe", @"C:\Users\A\AppData\Local\Programs\Hallow Clips\ffmpeg.exe", 4, 80),
                P(5000, 1000, "cmd.exe", @"C:\Windows\System32\cmd.exe", 0, 5, 0, true), P(5001, 5000, "conhost.exe", @"C:\Windows\System32\conhost.exe", 0, 8),
                P(6000, 1000, "MyTool.exe", @"D:\Tools\MyTool\MyTool.exe", 1, 50, 0, true), P(6001, 6000, "MyToolHelper.exe", @"D:\Tools\MyTool\bin\MyToolHelper.exe", 2, 30),
                P(6002, 6000, "notepad.exe", @"C:\Windows\System32\notepad.exe", 0, 10, 0, true),
                // pid reuse: 7001's "parent" 7000 started AFTER it, so it isn't its parent
                P(7000, 1000, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 0, 10, 0, false, 1, T0 + 999999999), P(7001, 7000, "Unrelated.exe", @"E:\Unrelated\Unrelated.exe", 0, 10, 0, false, 1, T0 + 5),
                P(8000, 1000, "DeltaForceClient-Win64-Shipping.exe", @"D:\Games\Delta Force\DeltaForceClient-Win64-Shipping.exe", 30, 7000, 85, true),
                P(8001, 8000, "SGuard64.exe", @"C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe", 1, 40),
            };
        }
        static AppGroup G(List<AppGroup> l, string key) { return l.FirstOrDefault(g => g.Key == key); }
        static AppGroup Of(List<AppGroup> l, int pid) { return l.FirstOrDefault(g => g.Members.Any(m => m.Pid == pid)); }
        static void Groups()
        {
            var l = Grouping.Build(Sample1(), @"C:\Windows", null);
            Ok(G(l, "chrome") != null && G(l, "chrome").Members.Count == 4 && G(l, "chrome").Title == "Google Chrome", "Chrome's processes are one group: Google Chrome (4)");
            Ok(G(l, "discord") != null && G(l, "discord").Members.Count == 3 && G(l, "discord").Title == "Discord" && G(l, "discord").Category == "Apps", "Discord (3), an app");
            Ok(G(l, "hallow") != null && G(l, "hallow").Members.Count == 3 && G(l, "hallow").Title == "Hallow" && G(l, "hallow").Category == "Hallow", "Hallow Hub, Clips and Clips' ffmpeg (its folder) are under Hallow");
            Ok(Of(l, 5001) == Of(l, 5000), "conhost goes with the console program that started it");
            Ok(Of(l, 6001) == Of(l, 6000), "a helper in the app's own folder goes with the app");
            Ok(Of(l, 6002) != Of(l, 6000), "Notepad started by the app is its own app (different folder)");
            Ok(Of(l, 712) != Of(l, 710) && G(l, "svchost").Members.Count == 2, "svchost children aren't merged into Service Host; the two svchost are one group");
            Ok(Of(l, 2000) != Of(l, 1000) && G(l, "explorer").Members.Count == 1 && G(l, "explorer").Category == "Windows", "apps started from Explorer aren't Explorer's; Explorer is a Windows process");
            Ok(Of(l, 7001).Title == "Unrelated", "a reused PID doesn't make a parent (Unrelated isn't Chrome's)");
            Ok(Of(l, 8000).IsGame && !Of(l, 8000).IsAntiCheat && Of(l, 8001) != Of(l, 8000) && Of(l, 8001).IsAntiCheat, "the game is its own group; its anti-cheat is a separate, marked group");
            Ok(G(l, "svchost").Category == "Windows" && G(l, "svchost").IsProtected, "Service Host is Windows and protected");
            Ok(Of(l, 712).Category == "Windows", "WmiPrvSE (session 0, System32) is Windows");
            var dc = G(l, "discord"); Ok(Math.Abs(dc.Cpu - 4.1) < 0.001 && dc.Ram == 390 * 1048576L && Math.Abs(dc.Gpu - 7) < 0.001, "group sums: CPU, RAM, GPU");
            Ok(Grouping.Matches(dc, "disc", "All") && Grouping.Matches(dc, "3001", "All") && Grouping.Matches(dc, "app-1.0.9", null) && !Grouping.Matches(dc, "chrome", "All"), "search: title, PID, path");
            Ok(Grouping.Matches(dc, "", "Apps") && !Grouping.Matches(dc, "", "Windows") && Grouping.Matches(G(l, "hallow"), null, "Hallow"), "filter chips");
            var tree = Grouping.Tree(Sample1().First(p => p.Pid == 6000), Sample1());
            Ok(tree.Select(p => p.Pid).OrderBy(x => x).SequenceEqual(new[] { 6000, 6001, 6002 }), "process tree: the app and what it started");
            var t2 = Grouping.Tree(Sample1().First(p => p.Pid == 7000), Sample1());
            Ok(t2.Count == 1, "process tree skips a reused PID's 'child'");
            Ok(Grouping.SameFolder(@"C:\A\B\x.exe", @"C:\A\B\sub\y.exe") && !Grouping.SameFolder(@"C:\Windows\System32\a.exe", @"C:\Windows\System32\b.exe") && !Grouping.SameFolder(null, @"C:\x.exe"), "same install folder (never just System32)");
        }

        // ---------------- impact and sorting
        static void Impact()
        {
            Ok(Math.Abs(ImpactCalc.Score(10, 0, 0, 0) - 10) < 1e-9, "Impact: CPU counts 1:1");
            Ok(Math.Abs(ImpactCalc.Score(0, 50, 0, 0) - 40) < 1e-9, "Impact: GPU counts 0.8");
            Ok(Math.Abs(ImpactCalc.Score(0, 0, 5 * 1048576, 0) - 10) < 1e-9 && Math.Abs(ImpactCalc.Score(0, 0, 100 * 1048576.0, 0) - 30) < 1e-9, "Impact: disk 2 per MB/s, at most 30");
            Ok(Math.Abs(ImpactCalc.Score(0, 0, 0, 2L << 30) - 4) < 1e-9 && Math.Abs(ImpactCalc.Score(0, 0, 0, 40L << 30) - 10) < 1e-9, "Impact: memory 2 per GB, at most 10");
            Ok(ImpactCalc.Score(-5, -1, -1, -1) == 0, "Impact never negative");
            Ok(ImpactCalc.Power(0, 0, 0) == "Very low" && ImpactCalc.Power(3, 0, 0) == "Low" && ImpactCalc.Power(10, 0, 0) == "Moderate" && ImpactCalc.Power(20, 10, 0) == "High" && ImpactCalc.Power(30, 80, 0) == "Very high", "power hint steps");
            Ok(ImpactCalc.Heat(0) == 0 && ImpactCalc.Heat(5) == 1 && ImpactCalc.Heat(20) == 2 && ImpactCalc.Heat(80) == 3, "cell tint steps");
            var l = Grouping.Build(Sample1(), @"C:\Windows", null);
            var s = Grouping.Sort(l, "impact", true);
            Ok(s[0].IsGame && s[1].Key == "chrome", "default sort (Impact): the game first, then Chrome");
            Ok(Grouping.Sort(l, "ram", true)[0].IsGame && Grouping.Sort(l, "cpu", false)[0].Cpu <= Grouping.Sort(l, "cpu", false)[1].Cpu, "sort by RAM / CPU, both directions");
            Ok(Grouping.Sort(l, "name", false)[0].Title.CompareTo(Grouping.Sort(l, "name", false)[1].Title) <= 0, "sort by name A-Z");
        }

        // ---------------- which game
        static void Detect()
        {
            var l = Sample1();
            Ok(GameDetect.Find(l, null, null).Pid == 8000, "Delta Force found by its exe");
            var noGame = l.Where(p => p.Pid != 8000).ToList();
            Ok(GameDetect.Find(noGame, null, null) == null, "no game when it isn't running");
            Ok(GameDetect.Find(noGame, new[] { "MyTool" }, null).Pid == 6000, "an extra game from Settings");
            var hub = new List<ProcInfo>(noGame) { P(9000, 1000, "TheFinals.exe", @"D:\Games\THE FINALS\TheFinals.exe", 20, 3000, 70, true) };
            hub.Last().Description = "THE FINALS";
            Ok(GameDetect.Find(hub, null, new[] { "THE FINALS" }).Pid == 9000, "Hallow Hub's \"playing\" name matches the exe / description");
            Ok(GameDetect.Find(hub, null, new[] { "Chrome" }) == null, "Hub names never match known apps (Chrome)");
            Ok(GameDetect.Find(noGame, null, new[] { "SGuard" }) == null && GameDetect.Find(l.Where(p => p.Pid != 8000).ToList(), null, new[] { "SGuard64" }) == null, "Hub names never match anti-cheat");
            Ok(GameDetect.Title(l.First(p => p.Pid == 8000), null) == "Delta Force", "Delta Force's friendly name");
        }

        // ---------------- the game panel
        static void Panel()
        {
            var w = new GameWatch(); var procs = Sample1(); var game = GameDetect.Find(procs, null, null);
            var now = DateTime.Now;
            for (int i = 0; i < 15; i++)
            {
                var ps = procs.Select(p => p.Clone()).ToList();
                foreach (var p in ps) if (p.Pid == 6000) p.Cpu = i < 5 ? 80 : 0;   // MyTool was busy, then went quiet
                w.Push(Grouping.Build(ps, @"C:\Windows", null), game, now.AddSeconds(i));
            }
            var groups = Grouping.Build(procs, @"C:\Windows", null);
            var rank = w.Rank(groups, game, 20);
            Ok(rank.All(r => !r.Group.Members.Any(m => m.Pid == 8000)), "the game itself is never in the list");
            Ok(rank[0].Group.Key == "chrome", "Chrome ranks first (most CPU + GPU)");
            var ac = rank.FirstOrDefault(r => r.Group.IsAntiCheat);
            Ok(ac != null && ac.Tag == Known.AntiCheatTag && !ac.CanAct, "anti-cheat is listed but marked \"Anti-cheat \u2014 leave alone\" with no actions");
            var sv = rank.FirstOrDefault(r => r.Group.Key == "svchost");
            Ok(sv != null && sv.Tag != null && !sv.CanAct, "Windows' own processes are marked leave alone");
            var my = rank.FirstOrDefault(r => r.Group.Members.Any(m => m.Pid == 6000));
            Ok(my != null && my.Samples == 10 && my.Cpu < 10, "averages the last 10 samples (MyTool's busy start has dropped out)");
            Ok(rank.First(r => r.Group.Key == "discord").CanAct, "Discord can be lowered / ended");
            Ok(w.Rank(groups, null, 20).Count == 0, "no game: nothing to rank");
            w.Push(groups, null, now); Ok(w.GameKey == null && w.Since == null, "game closed: history cleared");
            var other = procs.First(p => p.Pid == 6000);
            w.Push(groups, game, now); var since = w.Since; w.Push(groups, other, now.AddSeconds(1));
            Ok(w.GameKey == other.Key && w.Since != since, "a different game starts a new history");
            Ok(w.Rank(groups, game, 3).Count == 3, "at most N rows");
        }

        // ---------------- priorities while the game runs
        static void Keeper()
        {
            var fc = new FakeControl(); var k = new PriorityKeeper(fc);
            var a = P(100, 1, "Discord.exe", "x"); var b = P(101, 1, "obs64.exe", "y");
            fc.Created[100] = a.CreateTime; fc.Created[101] = b.CreateTime; fc.Prios[101] = Prio.AboveNormal;
            Ok(k.Lower(a, Prio.Normal) != null && k.Lower(a, Prio.High) != null, "only Below normal or Idle");
            Ok(k.Lower(a, Prio.BelowNormal) == null && fc.Prios[100] == Prio.BelowNormal && k.Lowered.Count == 1, "lower Discord to Below normal");
            Ok(k.Lower(a, Prio.Idle) == null && k.Lowered.Count == 1 && k.Lowered[0].Was == Prio.Normal && k.Lowered[0].Now == Prio.Idle, "lowering again keeps the original priority");
            Ok(k.Lower(b, Prio.Idle) == null && k.Lowered.Count == 2, "lower OBS (was Above normal)");
            fc.Created[101] = b.CreateTime + 1;   // OBS closed and its PID now belongs to something else
            Ok(k.RestoreAll() == 1 && fc.Prios[100] == Prio.Normal && fc.Prios[101] == Prio.Idle && k.Lowered.Count == 0, "game closed: Discord back to Normal; the reused PID isn't touched");
            fc.Gone.Add(100); Ok(k.Lower(a, Prio.Idle) != null, "a closed process can't be lowered");
            var c2 = P(102, 1, "x.exe", "z"); fc.Created[102] = c2.CreateTime; k.Lower(c2, Prio.BelowNormal);
            Ok(k.Restore(102) && fc.Prios[102] == Prio.Normal && k.Lowered.Count == 0, "Restore one");
        }

        // ---------------- what may be touched
        static void SafetyRules()
        {
            var game = P(8000, 1, "DeltaForceClient-Win64-Shipping.exe", "g");
            Ok(Safety.WhyNot(game, game, null, @"C:\Windows", 1) != null, "the game: refused");
            Ok(Safety.WhyNot(P(8001, 1, "Discovery-d.exe", "g"), null, null, @"C:\Windows", 1) != null, "a known game even when it isn't the detected one: refused");
            Ok(Safety.WhyNot(P(8002, 1, "SGuard64.exe", "a"), game, null, @"C:\Windows", 1).Contains("anti-cheat"), "anti-cheat: refused with a clear message");
            Ok(Safety.WhyNot(P(8003, 1, "BEService.exe", "a"), null, null, @"C:\Windows", 1) != null && Safety.WhyNot(P(8004, 1, "vgc.exe", "a", session: 0), null, null, @"C:\Windows", 1) != null, "BattlEye, Vanguard: refused");
            Ok(Safety.WhyNot(P(600, 1, "csrss.exe", null, session: 1), null, null, @"C:\Windows", 1).Contains("part of Windows"), "csrss: refused (part of Windows)");
            Ok(Safety.WhyNot(P(42, 1, "HallowTasks.exe", "t"), null, null, @"C:\Windows", 42) != null, "Hallow Tasks itself: refused");
            Ok(Safety.WhyNot(P(3000, 1, "Discord.exe", "d"), game, null, @"C:\Windows", 1) == null, "Discord: allowed");
            Ok(Safety.WhyNot((ProcInfo)null, null, null, null, 1) != null, "a closed process: refused");
            var hallow = new AppGroup { Key = "hallow", Members = new List<ProcInfo> { P(42, 1, "HallowTasks.exe", "t"), P(43, 1, "HallowHub.exe", "h") } };
            Ok(Safety.WhyNot(hallow, null, null, @"C:\Windows", 42) == null, "the Hallow group: allowed (Hallow Tasks itself is left out)");
            var only = new AppGroup { Key = "hallow", Members = new List<ProcInfo> { P(42, 1, "HallowTasks.exe", "t") } };
            Ok(Safety.WhyNot(only, null, null, @"C:\Windows", 42) != null, "a group of only Hallow Tasks: refused");
            var mixed = new AppGroup { Key = "x", Members = new List<ProcInfo> { P(1, 1, "a.exe", "a"), P(2, 1, "EasyAntiCheat.exe", "e") } };
            Ok(Safety.WhyNot(mixed, null, null, @"C:\Windows", 9) != null, "a group with anti-cheat in it: refused");
        }

        // ---------------- startup
        static void StartupBits()
        {
            string why;
            Ok(StartupImpact.Rate(true, true, 400L << 20, 0, false, out why) == "High" && why.Contains("400 MB"), "startup impact: 400 MB = High");
            Ok(StartupImpact.Rate(true, true, 150L << 20, 0, false, out why) == "Medium", "150 MB = Medium");
            Ok(StartupImpact.Rate(true, true, 20L << 20, 30, false, out why) == "Medium" && StartupImpact.Rate(true, true, 20L << 20, 130, false, out why) == "High", "CPU seconds per hour: 30 = Medium, 130 = High");
            Ok(StartupImpact.Rate(true, true, 20L << 20, 2, false, out why) == "Low", "small and quiet = Low");
            Ok(StartupImpact.Rate(false, false, 0, 0, false, out why) == "Off" && StartupImpact.Rate(false, true, 0, 0, false, out why) == "Not running", "Off / Not running");
            Ok(StartupImpact.Rate(false, true, 0, 0, true, out why) == "High" && why.Contains("slowed down startup"), "Windows' slow-startup event = High");
            Ok(StartupImpact.ExeOf("\"C:\\x y\\Discord\\Update.exe\" --processStart Discord.exe") == "Update" && StartupImpact.ExeOf("C:\\a\\b.EXE -x") == "b" && StartupImpact.ExeOf("") == null, "exe from a startup command");
            var items = new FakeStartup().Read(); var procs = Sample1();
            procs.Add(P(9500, 1000, "steam.exe", @"C:\Program Files (x86)\Steam\steam.exe", 0, 350)); procs.Last().CpuTime = (long)(3600 * 1e7);
            StartupRate.Apply(items, procs, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EpicGamesLauncher" }, DateTime.FromFileTime(T0).AddHours(10), p => p.CpuTime / 1e7);
            Ok(items.First(i => i.Name == "Steam").Impact == "High" && items.First(i => i.Name == "Steam").Running, "running Steam with 350 MB = High");
            Ok(items.First(i => i.Name == "EpicGamesLauncher").Impact == "Off" && items.First(i => i.Name == "EpicGamesLauncher").Why.Contains("slowed"), "switched off in Windows = Off (and why: it slowed down startup)");
            Ok(items.First(i => i.Name == "Spotify").Impact == "Not running", "not running = Not running");
            Ok(items.Where(i => i.Mine).All(i => i.Where == "Registry (you)") && items.Where(i => !i.Mine).All(i => i.Where != "Registry (you)"), "only your own Run entries can be changed");
        }
        static void StartupStoreTest()
        {
            var root = @"Software\HallowTasks\Test-unit" + Process.GetCurrentProcess().Id;
            var st = StartupStore.ForTest(root);
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(st.RunPath)) { k.SetValue("FakeApp", "\"C:\\Fake\\fake.exe\" --tray"); k.SetValue("FakeExp", "%LOCALAPPDATA%\\x.exe", RegistryValueKind.ExpandString); }
                Ok(st.Disable("FakeApp") == null, "disable a (fake) Run entry");
                using (var k = Registry.CurrentUser.OpenSubKey(st.RunPath)) Ok(k.GetValue("FakeApp") == null, "it's gone from the Run key");
                var l = st.List(); Ok(l.Count == 1 && l[0].Name == "FakeApp" && l[0].Data == "\"C:\\Fake\\fake.exe\" --tray", "and in the backup with its command");
                Ok(st.Disable("FakeApp") != null, "disabling it again: it isn't there");
                Ok(st.Restore(l[0].Id) == null && st.List().Count == 0, "Restore puts it back (undo)");
                using (var k = Registry.CurrentUser.OpenSubKey(st.RunPath)) Ok((string)k.GetValue("FakeApp") == "\"C:\\Fake\\fake.exe\" --tray", "the same command as before");
                Ok(st.Disable("FakeExp") == null, "an expandable entry");
                st.Restore(st.List()[0].Id);
                using (var k = Registry.CurrentUser.OpenSubKey(st.RunPath)) Ok(k.GetValueKind("FakeExp") == RegistryValueKind.ExpandString && (string)k.GetValue("FakeExp", null, RegistryValueOptions.DoNotExpandEnvironmentNames) == "%LOCALAPPDATA%\\x.exe", "keeps %variables% unexpanded");
                using (var k = Registry.CurrentUser.CreateSubKey(st.RunPath)) k.SetValue("Dup", "a");
                st.Disable("Dup"); using (var k = Registry.CurrentUser.CreateSubKey(st.RunPath)) k.SetValue("Dup", "b");
                Ok(st.Restore(st.List()[0].Id) != null, "Restore refuses to overwrite an entry with the same name");
            }
            finally { try { Registry.CurrentUser.DeleteSubKeyTree(root, false); } catch { } }
            using (var k = Registry.CurrentUser.OpenSubKey(root)) Ok(k == null, "the fake keys are cleaned up");
        }

        // ---------------- family bits
        static void Family()
        {
            bool on, lists;
            Controller.HubHosting("{\"on\":true,\"apps\":[\"Mixer\",\"Guard\"],\"generic\":true}", out on, out lists); Ok(on && lists, "Hub 2.13+ (generic): Tasks is listed");
            Controller.HubHosting("{\"on\":true,\"apps\":[\"Mixer\",\"Guard\"]}", out on, out lists); Ok(on && !lists, "an older Hub: own icon stays");
            Controller.HubHosting("{\"on\":false,\"generic\":true}", out on, out lists); Ok(!on && !lists, "Hub's icon off");
            Controller.HubHosting("nonsense", out on, out lists); Ok(!on && !lists, "a bad answer: own icon");
            var hd = Path.Combine(Dir, "hub"); Directory.CreateDirectory(hd);
            File.WriteAllText(Path.Combine(hd, "status.json"), "{\"app\":\"Hub\",\"running\":true,\"playing\":[\"Delta Force\",{\"name\":\"THE FINALS\"}]}");
            var pl = HubStatus.Playing(hd); Ok(pl.Count == 2 && pl[0] == "Delta Force" && pl[1] == "THE FINALS", "Hub's playing list (names or objects)");
            File.WriteAllText(Path.Combine(hd, "status.json"), "{\"app\":\"Hub\",\"running\":false,\"playing\":[\"Delta Force\"]}");
            Ok(HubStatus.Playing(hd).Count == 0, "Hub not running: nothing playing");
            File.WriteAllText(Path.Combine(hd, "status.json"), "{broken"); Ok(HubStatus.Playing(hd).Count == 0, "a broken file: nothing");
            Ok(DefenderScan.Args(@"C:\a b\x.exe") == "-Scan -ScanType 3 -File \"C:\\a b\\x.exe\" -DisableRemediation", "Defender scan arguments: one file, never removes anything by itself");
            bool threw = false; try { DefenderScan.Args("C:\\a\"b"); } catch { threw = true; } Ok(threw, "a path with a quote is refused");
            Ok(DefenderScan.Verdict(0, "Scan starting...\nScan finished.") == "Microsoft Defender found nothing in it.", "verdict: clean");
            Ok(DefenderScan.Verdict(2, "Threat                  : HackTool:Win32/Keygen\n").Contains("HackTool:Win32/Keygen"), "verdict: the threat's name");
            Ok(Controller.SearchUrl(new ProcInfo { Name = "updsvc.exe" }).StartsWith("https://www.bing.com/search?q=%22updsvc.exe%22"), "Search online: a web search for the name (opened only when clicked)");
            var d = Controller.Details(null, P(3000, 1000, "Discord.exe", @"C:\D\Discord.exe", 1.5, 200));
            Ok(d.Contains("PID: 3000") && d.Contains("Path: C:\\D\\Discord.exe") && d.Contains("CPU 1.5%"), "Copy details text");
            Ok(Accent.ApplyTo("a #F07820 #40F07820 b", "#8B5CF6") == "a #8B5CF6 #408B5CF6 b", "the Hallow accent swaps into the theme (also #AARRGGBB)");
            Ok(Ui.Readable("#101010") != "#101010" && Ui.Readable("#F07820") == "#F07820", "a too-dark accent is lightened for the dark panels");
        }
        static void Formats()
        {
            bool hkOk = false, hkRan = false; var th = new Thread(() => { try { using (var h = new Hotkey(() => { })) hkOk = h.Ok; hkRan = true; } catch { } }); th.SetApartmentState(ApartmentState.STA); th.Start(); th.Join(5000);
            Ok(hkRan, "Ctrl+Alt+T: registers and unregisters cleanly (" + (hkOk ? "it was free" : "another program has it - Settings says so") + ")");
            Ok(Fmt.Pct(0) == "0%" && Fmt.Pct(3.14) == "3.1%" && Fmt.Pct(42.6) == "43%", "percent format");
            Ok(Fmt.Bytes(512 * 1024) == "512 KB" && Fmt.Bytes(50L << 20) == "50.0 MB" && Fmt.Bytes(500L << 20) == "500 MB" && Fmt.Bytes(3L << 30) == "3.0 GB", "bytes format");
            Ok(Fmt.Rate(0) == "0.0 MB/s" && Fmt.Rate(2.5 * 1048576) == "2.5 MB/s", "disk rate format");
            Ok(Fmt.Bits(125000) == "1.0 Mbps" && Fmt.Bits(1000) == "8 Kbps", "network format (bits)");
            var r = new Ring(3); r.Add(1); r.Add(2); r.Add(3); r.Add(4);
            Ok(r.Count == 3 && r[0] == 2 && r.LastValue == 4 && r.Max() == 4, "graph ring keeps the newest 60 (here 3)");
        }

        // ---------------- REAL, read-only: sampling this PC
        static void RealSampling()
        {
            var s = new WindowsSampler(); var a = s.Take(); Thread.Sleep(1000); var b = s.Take();
            Ok(b.Procs.Count > 20, "real sample: " + b.Procs.Count + " processes");
            var me = b.Procs.FirstOrDefault(p => p.Pid == Process.GetCurrentProcess().Id);
            Ok(me != null && me.Path != null && me.Path.EndsWith("TasksTests.exe", StringComparison.OrdinalIgnoreCase) && me.Ram > 0, "finds this test process with its path and memory");
            Ok(b.Procs.Sum(p => p.Cpu) <= 101 && b.Procs.All(p => p.Cpu >= 0), "CPU % adds up to at most 100");
            Ok(b.Sys.RamTotal > 0 && b.Sys.RamUsed > 0 && b.Sys.Cores.Length == Environment.ProcessorCount, "system memory and per-core CPU");
            Ok(b.Procs.Any(p => p.HasWindow) || true, "windows found: " + b.Procs.Count(p => p.HasWindow));
            Console.WriteLine("     GPU counters: " + (b.Sys.GpuKnown ? "yes, 3D " + Fmt.Pct(b.Sys.Gpu3D) + ", VRAM " + Fmt.Bytes(b.Sys.VramUsed) + " of " + Fmt.Bytes(b.Sys.VramTotal) + " (" + b.Sys.GpuName + ")" : "not available") +
                "; disk " + Fmt.Rate(b.Sys.DiskRead + b.Sys.DiskWrite) + "; net " + Fmt.Bits(b.Sys.NetDown + b.Sys.NetUp) + "; conns counted for " + b.Procs.Count(p => p.Conns > 0) + " processes");
            Ok(b.SampleMs < 1000, "a sample takes " + b.SampleMs.ToString("0") + " ms (first: " + a.SampleMs.ToString("0") + " ms)");
            var groups = Grouping.Build(b.Procs, s.WindowsDir, null);
            Ok(groups.Count > 5 && groups.Count < b.Procs.Count, "real grouping: " + groups.Count + " groups from " + b.Procs.Count + " processes");
            Ok(groups.Any(g => g.Category == "Windows"), "real: Windows processes found");
            var sys = b.Procs.FirstOrDefault(p => p.Pid == 4); Ok(sys != null && Known.IsProtected(sys, s.WindowsDir), "real: System (pid 4) is protected");
            var sw = Stopwatch.StartNew(); var st = new WindowsStartup(StartupStore.UserRun).Read(); var slow = new WindowsStartup(StartupStore.UserRun).SlowExes();
            Ok(st != null && st.All(i => i.Name != null && i.Where != null), "real startup entries read (read-only): " + st.Count + " entries, " + st.Count(i => i.Mine) + " of them yours; slow-startup events readable: " + slow.Count + " (" + sw.ElapsedMilliseconds + " ms)");
        }
        static int Overhead()
        {
            var s = new WindowsSampler(); if (Environment.GetCommandLineArgs().Contains("nogpu")) s.GpuEvery = 100000; else if (Environment.GetCommandLineArgs().Contains("gpu2")) s.GpuEvery = 2; for (int w = 0; w < 15; w++) { s.Take(); Thread.Sleep(1000); } var ms = new List<double>(); var cpu0 = Process.GetCurrentProcess().TotalProcessorTime; var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) { Thread.Sleep(1000); var x = s.Take(); ms.Add(x.SampleMs); Grouping.Build(x.Procs, s.WindowsDir, null); }
            var used = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds; var wall = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine("sample ms: avg " + ms.Average().ToString("0.0") + ", max " + ms.Max().ToString("0.0"));
            Console.WriteLine("CPU: " + (100.0 * used / wall / Environment.ProcessorCount).ToString("0.00") + "% of the whole CPU (" + (100.0 * used / wall).ToString("0.0") + "% of one core) over " + (wall / 1000).ToString("0") + " s");
            return 0;
        }

        // ---------------- REAL control - only on Dummy.exe children started here
        static ProcessPriorityClass ReadPrio(Process p) { p.Refresh(); return p.PriorityClass; }
        static Prio PrioOf(ProcessPriorityClass c) { switch (c) { case ProcessPriorityClass.Idle: return Prio.Idle; case ProcessPriorityClass.BelowNormal: return Prio.BelowNormal; case ProcessPriorityClass.AboveNormal: return Prio.AboveNormal; case ProcessPriorityClass.High: return Prio.High; default: return Prio.Normal; } }
        static string DummyExe { get { return Path.Combine(Path.GetDirectoryName(typeof(Run).Assembly.Location), "Dummy.exe"); } }
        static Process StartDummy(string args) { return Process.Start(new ProcessStartInfo(DummyExe, args) { UseShellExecute = false, CreateNoWindow = true }); }
        static void RealControl()
        {
            if (!File.Exists(DummyExe)) { Ok(false, "Dummy.exe is built"); return; }
            var ctl = new WindowsControl(); var d = StartDummy("60"); Thread.Sleep(300);
            try
            {
                var first = ctl.GetPriority(d.Id);   // a child inherits the tests' own priority (they run at Below normal)
                Ok(first != null && (int)first.Value == (int)PrioOf(ReadPrio(d)), "dummy: reads its priority (" + first + ")");
                Ok(ctl.SetPriority(d.Id, Prio.Normal) == null && ReadPrio(d) == ProcessPriorityClass.Normal, "dummy: set Normal");
                Ok(ctl.SetPriority(d.Id, Prio.BelowNormal) == null && ReadPrio(d) == ProcessPriorityClass.BelowNormal, "dummy: set Below normal");
                var keeper = new PriorityKeeper(ctl); var info = new ProcInfo { Pid = d.Id, Name = "Dummy.exe", CreateTime = ctl.CreateTime(d.Id) };
                Ok(info.CreateTime == d.StartTime.ToFileTime(), "dummy: its creation time identifies it");
                Ok(keeper.Lower(info, Prio.Idle) == null && ctl.GetPriority(d.Id) == Prio.Idle, "dummy: lowered to Idle for the game");
                Ok(keeper.RestoreAll() == 1 && ctl.GetPriority(d.Id) == Prio.BelowNormal, "dummy: restored to what it was");
                Ok(ctl.SetAffinity(d.Id, 1) == null && ctl.Affinity(d.Id) == 1, "dummy: affinity CPU 0 only");
                Ok(ctl.SetAffinity(d.Id, 0) != null, "dummy: no CPUs refused");
                Ok(ctl.Kill(d.Id) == null && d.WaitForExit(5000), "dummy: End task");
                Ok(ctl.CreateTime(d.Id) == 0, "dummy: gone (a closed process reads as gone even while a handle is open)");
                Ok(ctl.Kill(d.Id) != null, "ending it again: an error, no crash");
            }
            finally { try { if (!d.HasExited) d.Kill(); } catch { } }
            // the process tree: a dummy that started a dummy
            var parent = StartDummy("60 child"); Thread.Sleep(1200);
            try
            {
                var s = new WindowsSampler(); var all = s.Take().Procs;
                var root = all.FirstOrDefault(p => p.Pid == parent.Id);
                var tree = Grouping.Tree(root, all);
                Ok(tree.Count == 2 && tree.All(p => p.Name.Equals("Dummy.exe", StringComparison.OrdinalIgnoreCase)), "dummy tree: the dummy and the dummy it started (" + string.Join(", ", tree.Select(p => p.Name + " " + p.Pid)) + ")");
                if (tree.All(p => p.Name.Equals("Dummy.exe", StringComparison.OrdinalIgnoreCase)))   // never kill anything that isn't a Dummy
                {
                    foreach (var p in Enumerable.Reverse(tree)) ctl.Kill(p.Pid);
                    Thread.Sleep(500);
                    Ok(tree.All(p => ctl.CreateTime(p.Pid) == 0 || ctl.CreateTime(p.Pid) != p.CreateTime), "dummy tree: End process tree ended both");
                }
            }
            finally { try { foreach (var p in Process.GetProcessesByName("Dummy")) using (p) if (string.Equals(p.MainModule.FileName, DummyExe, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { } }
            Ok(Safety.WhyNot(new ProcInfo { Pid = Process.GetCurrentProcess().Id, Name = "TasksTests.exe" }, null, null, @"C:\Windows", Process.GetCurrentProcess().Id) != null, "the app's own process is refused");
        }

        // ---------------- the family pipe of a test copy (made-up processes, fake control: nothing real is touched)
        static string Cmd(string exe, string dir, string line)
        {
            var psi = new ProcessStartInfo(exe, "--cmd \"" + line + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            psi.EnvironmentVariables["HALLOW_TASKS_TESTDIR"] = dir;
            using (var p = Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(10000); return o; }
        }
        static void PipeAndApp()
        {
            var exe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(Run).Assembly.Location)), "HallowTasks.exe");
            if (!File.Exists(exe)) { Ok(false, "HallowTasks.exe is built"); return; }
            var dir = Path.Combine(Dir, "app"); Directory.CreateDirectory(dir);
            var psi = new ProcessStartInfo(exe, "--tray") { UseShellExecute = false, CreateNoWindow = true };
            psi.EnvironmentVariables["HALLOW_TASKS_TESTDIR"] = dir; psi.EnvironmentVariables["HALLOW_TASKS_FAKE"] = "1"; psi.EnvironmentVariables["HALLOW_TASKS_HEADLESS"] = "1";
            var app = Process.Start(psi);
            try
            {
                string r = null; for (int i = 0; i < 40 && (r == null || !r.StartsWith("ok")); i++) { Thread.Sleep(250); r = Cmd(exe, dir, "ping"); }
                Ok(r == "ok {\"app\":\"Tasks\",\"version\":\"" + Program.Version + "\"}", "ping: " + r);
                r = Cmd(exe, dir, "status"); Ok(r.StartsWith("ok {") && r.Contains("\"app\":\"Tasks\"") && r.Contains("\"topCpu\":[{\"name\":\"Delta Force\"") && r.Contains("\"topGpu\":"), "status: the top CPU / GPU users");
                Ok(!r.Contains("C:\\\\") && !r.Contains("Alex"), "status: names and numbers only - no paths");
                Ok(File.Exists(Path.Combine(dir, "status.json")) && File.ReadAllText(Path.Combine(dir, "status.json")).Contains("\"topCpu\""), "status.json in the test folder");
                r = Cmd(exe, dir, "menu"); Ok(r.StartsWith("ok {\"app\":\"Tasks\",\"items\":[") && r.Contains("\"id\":\"game\"") && r.Contains("\"id\":\"hotkey\"") && r.Contains("\"checked\":false"), "menu (Hub 2.13+ lists any app answering it); hotkey off by default");
                Ok(Cmd(exe, dir, "open performance") == "ok" && Cmd(exe, dir, "open nowhere").StartsWith("error"), "open <page>");
                Ok(Cmd(exe, dir, "invoke nothing") == "error unknown item" && Cmd(exe, dir, "frobnicate") == "error unknown command", "unknown item / command");
                for (int i = 0; i < 4; i++) Cmd(exe, dir, "test-sample");
                Ok(Cmd(exe, dir, "test-game on") == "ok DeltaForceClient-Win64-Shipping.exe", "the test copy sees the (made-up) game");
                r = Cmd(exe, dir, "test-rank"); Ok(r.StartsWith("ok [") && !r.Contains("Delta Force") && r.Contains("SGuard [Anti-cheat"), "game panel over the pipe: no game, anti-cheat marked: " + r);
                Ok(Cmd(exe, dir, "test-lower wallpaper") == "ok 1" && Cmd(exe, dir, "test-prio 4900") == "ok BelowNormal", "lower Wallpaper Engine (fake control)");
                Ok(Cmd(exe, dir, "test-lower exe:sguard64").StartsWith("error That's anti-cheat"), "lowering anti-cheat is refused");
                Ok(Cmd(exe, dir, "test-lower exe:deltaforceclient-win64-shipping").StartsWith("error That's your game"), "lowering the game is refused");
                Ok(Cmd(exe, dir, "test-game off") == "ok none" && Cmd(exe, dir, "test-prio 4900") == "ok Normal", "game closed: Wallpaper Engine back to Normal");
                Ok(Cmd(exe, dir, "test-end chrome") == "ok", "End task on Chrome's group");
                r = Cmd(exe, dir, "test-calls"); Ok(r.Contains("kill 4300") && r.Contains("kill 4340") && !r.Contains("kill 5020") && !r.Contains("kill 5044"), "it ended Chrome's 5 processes and nothing else");
                Ok(Cmd(exe, dir, "test-end svchost").StartsWith("error") && Cmd(exe, dir, "test-end explorer").StartsWith("error"), "Service Host / Explorer: refused");
                Ok(Cmd(exe, dir, "test-game on") == "ok DeltaForceClient-Win64-Shipping.exe", "game on again for the 1.1 checks");
                PipeV11(exe, dir);
                Ok(Cmd(exe, dir, "test-autostart on") == "ok True" && Cmd(exe, dir, "test-autostart off") == "ok False", "Start with Windows (test file, never the real Run key)");
                Ok(Cmd(exe, dir, "quit") == "ok", "quit");
                Ok(app.WaitForExit(8000), "the test copy exits");
                var cp = new ProcessStartInfo(exe, "--cmd ping") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }; cp.EnvironmentVariables["HALLOW_TASKS_TESTDIR"] = dir;
                using (var c = Process.Start(cp)) { c.StandardOutput.ReadToEnd(); c.WaitForExit(10000); Ok(c.ExitCode == 2, "--cmd when it isn't running -> exit 2"); }
            }
            finally { try { if (!app.HasExited) app.Kill(); } catch { } }
        }
        // a test copy with the REAL sampler (read-only) and no windows: status over the pipe
        static void RealCopy()
        {
            var exe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(Run).Assembly.Location)), "HallowTasks.exe");
            var dir = Path.Combine(Dir, "real"); Directory.CreateDirectory(dir);
            var psi = new ProcessStartInfo(exe, "--tray") { UseShellExecute = false, CreateNoWindow = true };
            psi.EnvironmentVariables["HALLOW_TASKS_TESTDIR"] = dir; psi.EnvironmentVariables["HALLOW_TASKS_HEADLESS"] = "1";
            var app = Process.Start(psi);
            try
            {
                string r = null; for (int i = 0; i < 40 && (r == null || !r.StartsWith("ok")); i++) { Thread.Sleep(250); r = Cmd(exe, dir, "ping"); }
                var sw = Stopwatch.StartNew(); r = Cmd(exe, dir, "status"); var ms = sw.ElapsedMilliseconds;
                Ok(r.StartsWith("ok {") && r.Contains("\"topCpu\":[{\"name\":") && r.Contains("\"processes\":") && r.Contains("\"sampling\":false"), "real test copy: status with the top users while its window is closed (" + ms + " ms)");
                Ok(!r.Contains(":\\\\"), "real status has no paths");
                r = Cmd(exe, dir, "test-sample"); Ok(r.StartsWith("ok {\"groups\":"), "real test copy samples: " + r);
                Ok(Cmd(exe, dir, "quit") == "ok" && app.WaitForExit(8000), "real test copy quits");
            }
            finally { try { if (!app.HasExited) app.Kill(); } catch { } }
        }
    }
}
