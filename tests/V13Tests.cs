// Hallow Tasks 1.3 tests: "Close game...". Made-up processes for who it's offered for and what Force close may end (never
// anti-cheat, launchers, Windows or something the game started that isn't part of it); the steps with a fake process layer (closed
// nicely, forced, Windows refusing = access denied, the log); then stand-ins this test starts itself in its own temp folder - a
// windowed stand-in "game" that closes on WM_CLOSE, one that ignores it (Force close), and an anti-cheat-named and a launcher-named
// stand-in it starts that must never be offered or touched. Never a real game, anti-cheat or launcher.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace HallowTasks.Tests
{
    public static partial class Run
    {
        static void V13() { V13Lists(); V13Steps(); V13Real(); }

        const string DfRoot = @"D:\SteamLibrary\steamapps\common\Delta Force\Game\DeltaForce";
        static List<ProcInfo> V13Procs()
        {
            return new List<ProcInfo> {
                P(1032, 960, "services.exe", null, session: 0), P(3620, 3500, "explorer.exe", @"C:\Windows\explorer.exe", window: true),
                P(4500, 3620, "steam.exe", @"C:\Program Files (x86)\Steam\steam.exe", window: true),
                P(5020, 4500, "DeltaForceClient-Win64-Shipping.exe", DfRoot + @"\Binaries\Win64\DeltaForceClient-Win64-Shipping.exe", 30, 7000, 88, true),
                P(5030, 5020, "CrashReportClient.exe", @"D:\SteamLibrary\steamapps\common\Delta Force\Engine\Binaries\Win64\CrashReportClient.exe"),   // a known helper outside its folder
                P(5031, 5020, "DFHelper.exe", DfRoot + @"\Binaries\Win64\DFHelper.exe"),                       // in its folder
                P(5032, 5031, "DFSubHelper.exe", DfRoot + @"\Content\DFSubHelper.exe"),                         // started by a helper, in its folder
                P(5044, 5020, "SGuard64.exe", @"C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe"),     // anti-cheat it started
                P(5045, 5044, "UnderAntiCheat.exe", DfRoot + @"\UnderAntiCheat.exe"),                          // started by anti-cheat: skipped with it
                P(5050, 5020, "steamwebhelper.exe", @"C:\Program Files (x86)\Steam\bin\cef\cef.win64\steamwebhelper.exe"),
                P(5051, 5020, "upc.exe", DfRoot + @"\upc.exe"),                                                // a launcher, even inside its folder
                P(5060, 5020, "notepad.exe", @"C:\Windows\System32\notepad.exe", window: true),               // not part of the game
                P(5061, 5020, "HallowClips.exe", @"C:\Users\A\AppData\Local\Programs\Hallow Clips\HallowClips.exe"),
                P(5099, 5020, "DFHelper.exe", DfRoot + @"\Binaries\Win64\DFHelper.exe", created: T0 - 5),   // pid reuse: older than the game
                P(5048, 1032, "SGuardSvc64.exe", @"C:\Program Files\AntiCheatExpert\SGuard\x64\SGuardSvc64.exe", session: 0),
                P(4100, 3620, "Discord.exe", @"C:\Users\A\AppData\Local\Discord\app-1.0.9\Discord.exe", window: true) };
        }

        static void V13Lists()
        {
            var all = V13Procs(); Func<int, ProcInfo> pid = n => all.First(p => p.Pid == n); var game = pid(5020);
            Ok(GameClose.Offerable(game, null, null, 1), "close game: offered for Delta Force (tagged Game)");
            Ok(GameClose.Offerable(P(101, 0, "Discovery-d.exe", @"D:\G\Discovery-d.exe"), null, null, 9) && GameClose.Offerable(P(77, 0, "MyGame.exe", @"D:\G\MyGame.exe"), null, new[] { "mygame" }, 9), "close game: offered for the other known game and Settings' extra games");
            var hub = P(78, 0, "SomeHubGame.exe", @"D:\G\SomeHubGame.exe");
            Ok(GameClose.Offerable(hub, hub, null, 9), "close game: offered for the game found through Hub's playing list");
            var every = new[] { "SGuard64", "steam", "steamwebhelper", "upc", "Battle.net", "EpicGamesLauncher", "explorer", "HallowHub", "Discord", "EasyAntiCheat", "BEService", "vgc" };
            foreach (var n in new[] { "SGuard64.exe", "SGuardSvc64.exe", "EasyAntiCheat.exe", "BEService.exe", "vgc.exe", "ACE-Tray.exe" })
                Ok(!GameClose.Offerable(P(80, 0, n, @"D:\G\" + n), null, every, 9), "close game: never offered for anti-cheat, even listed as a game: " + n);
            foreach (var n in new[] { "steam.exe", "steamwebhelper.exe", "Battle.net.exe", "EpicGamesLauncher.exe", "upc.exe" })
                Ok(!GameClose.Offerable(P(81, 0, n, @"D:\G\" + n), null, every, 9) && GameClose.IsLauncher(n), "close game: never offered for a launcher, even listed as a game: " + n);
            Ok(!GameClose.Offerable(P(82, 0, "explorer.exe", @"C:\Windows\explorer.exe"), null, every, 9) && !GameClose.Offerable(P(83, 0, "HallowHub.exe", null), null, every, 9), "close game: never Windows or Hallow");
            Ok(!GameClose.Offerable(pid(4100), null, null, 9) && !GameClose.Offerable(game, null, null, 5020), "close game: not for Discord, not for Tasks itself");
            Ok(!GameClose.IsLauncher("DeltaForceClient-Win64-Shipping.exe") && !GameClose.IsLauncher("Discord.exe"), "the game and Discord aren't launchers");
            // rows: a group or one process
            var groups = Grouping.Build(all, @"C:\Windows", null);
            var gg = groups.FirstOrDefault(g => g.Members.Any(m => m.Pid == 5020));
            var ac = groups.FirstOrDefault(g => g.Members.Any(m => m.Pid == 5044));
            var steam = groups.FirstOrDefault(g => g.Members.Any(m => m.Pid == 4500));
            var picked = GameClose.Pick(gg, null, null, null, 9);
            Ok(picked != null && picked.Pid == 5020, "close game: the game's row picks the game process");
            Ok(ac != null && GameClose.Pick(ac, null, null, null, 9) == null && GameClose.Pick(ac, pid(5044), game, null, 9) == null, "close game: no menu item on anti-cheat's row");
            Ok(steam != null && GameClose.Pick(steam, null, game, null, 9) == null, "close game: no menu item on Steam's row");
            Ok(GameClose.Pick(gg, pid(5031), game, null, 9) == null, "close game: no menu item on a helper's own row (only the game)");
            // the install folder
            Ok(GameClose.InstallRoot(game.Path, @"C:\Windows") == DfRoot, "install folder: the Unreal game's folder (above Binaries\\Win64): " + GameClose.InstallRoot(game.Path, @"C:\Windows"));
            Ok(GameClose.InstallRoot(@"D:\SteamLibrary\steamapps\common\Foo\Foo.exe", @"C:\Windows") == @"D:\SteamLibrary\steamapps\common\Foo", "install folder: the exe's folder");
            Ok(GameClose.InstallRoot(@"D:\Foo.exe", @"C:\Windows") == null && GameClose.InstallRoot(@"C:\Program Files\Foo.exe", @"C:\Windows") == null && GameClose.InstallRoot(@"D:\SteamLibrary\steamapps\common\Foo.exe", @"C:\Windows") == null && GameClose.InstallRoot(null, @"C:\Windows") == null,
                "install folder: never a drive, Program Files or Steam's common folder");
            // what Force close may end
            var ts = GameClose.Targets(game, all, @"C:\Windows", 9).Select(p => p.Pid).ToList();
            Ok(ts.Count == 4 && ts[0] == 5020 && ts.Contains(5030) && ts.Contains(5031) && ts.Contains(5032), "force close: the game first, then its own helpers (folder + known helper): " + string.Join(", ", ts));
            Ok(!ts.Contains(5044) && !ts.Contains(5045) && !ts.Contains(5048), "force close: never anti-cheat, nor what anti-cheat started");
            Ok(!ts.Contains(5050) && !ts.Contains(5051) && !ts.Contains(4500), "force close: never Steam or a launcher");
            Ok(!ts.Contains(5060) && !ts.Contains(5061) && !ts.Contains(5099) && !ts.Contains(4100), "force close: not Notepad it opened, not Hallow, not a reused pid, not Discord");
            Ok(GameClose.AntiCheatName(all) == "ACE" && GameClose.AntiCheatName(new[] { P(1, 0, "EasyAntiCheat.exe", null) }) == "Easy Anti-Cheat" && GameClose.AntiCheatName(new[] { game }) == null, "anti-cheat named in plain words");
            // the other actions stay off for games and anti-cheat
            var why = Safety.WhyNot(game, game, null, @"C:\Windows", 9);
            Ok(why != null && why.Contains("Close game"), "End task / priority / affinity / Efficiency stay off for the game, pointing at Close game: " + why);
            Ok(Safety.WhyNot(pid(5044), game, null, @"C:\Windows", 9) != null, "and stay off for anti-cheat");
        }

        static void V13Steps()
        {
            var all = V13Procs(); var game = all.First(p => p.Pid == 5020); var t0 = new DateTime(2026, 11, 22, 20, 0, 0);
            var log = new List<string>();
            Func<FakeGameCloser, List<ProcInfo>, GameCloseRun> make = (f, procs) => new GameCloseRun(game, "Delta Force", f, () => procs, @"C:\Windows", 9, s => log.Add(s));
            Func<FakeGameCloser, bool> neverOthers = f => !f.Calls.Any(c => new[] { 5044, 5045, 5048, 5050, 5051, 5060, 5061, 5099, 4500, 4100, 3620 }.Any(n => c.EndsWith(" " + n)));
            Ok(GameClose.WaitSeconds == 10, "step 1 waits up to 10 s");
            // closes when asked
            var f1 = new FakeGameCloser(); var r1 = make(f1, all);
            Ok(r1.Now == GameCloseRun.State.Ready && f1.Calls.Count == 0, "nothing happens before you click Close game in the dialog");
            r1.Ask(t0);
            Ok(f1.Calls.Contains("close 5020") && !f1.Calls.Any(c => c.StartsWith("kill")), "step 1: asks nicely (WM_CLOSE), nothing ended");
            Ok(r1.Tick(t0.AddSeconds(1)) == GameCloseRun.State.Closed && log.Last() == "close game DeltaForceClient-Win64-Shipping.exe: closed nicely", "closed nicely - logged: " + log.LastOrDefault());
            Ok(neverOthers(f1), "step 1 never asks anti-cheat, launchers or other apps");
            // ignores WM_CLOSE: still running after 10 s, Force close
            var f2 = new FakeGameCloser(); foreach (var n in new[] { 5020, 5030, 5031, 5032 }) f2.IgnoresClose.Add(n); var r2 = make(f2, all);
            r2.Ask(t0);
            Ok(r2.Tick(t0.AddSeconds(4)) == GameCloseRun.State.Asking && r2.SecondsLeft(t0.AddSeconds(4)) == 6, "countdown: 6 s left after 4 s");
            var f3 = new FakeGameCloser(); foreach (var n in new[] { 5020, 5030, 5031, 5032 }) f3.IgnoresClose.Add(n); var r3 = make(f3, all);
            r3.Ask(t0); r3.Tick(t0.AddSeconds(9.5));
            Ok(r3.Now == GameCloseRun.State.Asking, "still waiting at 9.5 s");
            Ok(r3.Tick(t0.AddSeconds(10)) == GameCloseRun.State.StillRunning && !f3.Calls.Any(c => c.StartsWith("kill")), "after 10 s: still running, Force close offered - nothing ended by itself");
            Ok(r3.Force() == GameCloseRun.State.Forced && r3.Ended == 4, "force close: ended the game and its 3 helpers (" + r3.Ended + ")");
            var kills = f3.Calls.Where(c => c.StartsWith("kill")).ToList();
            Ok(kills.Count == 4 && kills[0] == "kill 5020", "force close: the game first: " + string.Join(", ", kills));
            Ok(neverOthers(f3), "force close: anti-cheat, Steam, the launcher, Notepad, Hallow and Discord never touched");
            Ok(log.Last() == "close game DeltaForceClient-Win64-Shipping.exe: force closed (4 processes)", "forced - logged: " + log.Last());
            // Windows refuses (access denied: anti-cheat protects the game)
            var f4 = new FakeGameCloser(); f4.IgnoresClose.Add(5020); f4.Denied.Add(5020); var r4 = make(f4, all);
            r4.Ask(t0); r4.Tick(t0.AddSeconds(11));
            Ok(r4.Force() == GameCloseRun.State.Denied && r4.Error == 5, "access denied: shown as Windows refusing");
            Ok(f4.Calls.Count(c => c.StartsWith("kill")) == 1 && neverOthers(f4), "access denied: nothing else is touched (no helpers, nothing on anti-cheat)");
            Ok(r4.DeniedText() == "Delta Force's anti-cheat (ACE) protects the game, so Windows won't let any app end it.", "access denied, in plain words: " + r4.DeniedText());
            var opts = string.Join(" | ", r4.DeniedOptions());
            Ok(opts.Contains("Steam") && opts.Contains("Stop button") && opts.Contains("ACE often finishes its shutdown") && opts.Contains("Sign out") && opts.Contains("restart"), "access denied: what you can do: " + opts);
            Ok(log.Last() == "close game DeltaForceClient-Win64-Shipping.exe: Windows denied it (protected - anti-cheat or admin)", "denied - logged: " + log.Last());
            Ok(r4.Force() == GameCloseRun.State.Denied && f4.Calls.Count(c => c.StartsWith("kill")) == 1, "no second try at a protected game behind your back");
            f4.Gone.Add(5020);
            Ok(r4.Tick(t0.AddSeconds(60)) == GameCloseRun.State.Closed && log.Last().EndsWith("closed by itself after Windows refused"), "when it finishes closing by itself the dialog says so: " + log.Last());
            var noAc = all.Where(p => !Known.IsAntiCheat(p.Name)).ToList(); var f5 = new FakeGameCloser(); f5.IgnoresClose.Add(5020); f5.Denied.Add(5020); var r5 = make(f5, noAc);
            r5.Ask(t0); r5.Tick(t0.AddSeconds(11)); r5.Force();
            Ok(r5.Now == GameCloseRun.State.Denied && r5.DeniedText().StartsWith("Windows protects Delta Force"), "access denied without anti-cheat: " + r5.DeniedText());
            // closed meanwhile; cancelled; never anti-cheat even if asked to
            var f6 = new FakeGameCloser(); f6.Gone.Add(5020); var r6 = make(f6, all); r6.Ask(t0);
            Ok(r6.Now == GameCloseRun.State.Closed && f6.Calls.Count == 0, "already closed: nothing sent");
            var f7 = new FakeGameCloser(); f7.IgnoresClose.Add(5020); var r7 = make(f7, all); r7.Ask(t0); r7.Cancelled();
            Ok(log.Last().EndsWith("still running - not forced") && !f7.Calls.Any(c => c.StartsWith("kill")), "dialog closed while waiting: not forced");
            var sg = all.First(p => p.Pid == 5044); var f8 = new FakeGameCloser(); f8.IgnoresClose.Add(5044);
            var r8 = new GameCloseRun(sg, "SGuard", f8, () => all, @"C:\Windows", 9, s => log.Add(s)); r8.Ask(t0);
            Ok(r8.Now == GameCloseRun.State.Failed && r8.Force() == GameCloseRun.State.Failed && f8.Calls.Count == 0, "Close game refuses anything that isn't a game (anti-cheat), even if called directly - not even WM_CLOSE");
            Ok(log.All(l => l.StartsWith("close game ") && !l.Contains("\\") && !l.Contains("5020") && !l.Contains("Steam")), "the log: the exe name and what happened, no paths or other data");
        }

        static string StandInExe { get { return Path.Combine(Path.GetDirectoryName(typeof(Run).Assembly.Location), "StandIn.exe"); } }
        static void V13Real()
        {
            if (!File.Exists(StandInExe) || !File.Exists(DummyExe)) { Ok(false, "StandIn.exe and Dummy.exe are built"); return; }
            var t = Path.Combine(Dir, "standins"); var bin = Path.Combine(t, @"Stand In Game\Binaries\Win64"); var other = Path.Combine(t, "Other");
            Directory.CreateDirectory(bin); Directory.CreateDirectory(other);
            var gameExe = Path.Combine(bin, "TestStandInGame.exe"); var helperExe = Path.Combine(bin, "StandInGameHelper.exe");
            var acExe = Path.Combine(other, "xigncode_standin.exe"); var launcherExe = Path.Combine(other, "upc.exe");
            File.Copy(StandInExe, gameExe, true); File.Copy(DummyExe, helperExe, true); File.Copy(DummyExe, acExe, true); File.Copy(DummyExe, launcherExe, true);
            var extra = new[] { "TestStandInGame", "xigncode_standin", "upc" };   // even listed as games, anti-cheat and launchers are never offered
            var io = new WindowsGameCloser(); var ctl = new WindowsControl(); var self = Process.GetCurrentProcess().Id; var log = new List<string>();
            Func<Process, ProcInfo> info = pr => { var s = new WindowsSampler().Take().Procs; return s.FirstOrDefault(p => p.Pid == pr.Id); };
            try
            {
                // 1: a stand-in that closes when asked
                var g1 = Process.Start(new ProcessStartInfo(gameExe, "close 60") { UseShellExecute = false }); WaitWindow(io, g1);
                var p1 = info(g1);
                Ok(p1 != null && GameClose.Offerable(p1, null, extra, self), "stand-in game: offered (tagged Game through Settings' list)");
                var r1 = new GameCloseRun(p1, "Stand-in game", io, () => new WindowsSampler().Take().Procs, @"C:\Windows", self, s => log.Add(s));
                r1.Ask(DateTime.Now); var until = DateTime.Now.AddSeconds(12);
                while (r1.Tick(DateTime.Now) == GameCloseRun.State.Asking && DateTime.Now < until) Thread.Sleep(200);
                Ok(r1.Now == GameCloseRun.State.Closed && g1.WaitForExit(2000), "stand-in game: closed nicely by WM_CLOSE (" + r1.TookSeconds.ToString("0.0") + " s)");
                Ok(log.LastOrDefault() == "close game TestStandInGame.exe: closed nicely", "logged: " + log.LastOrDefault());

                // 2: one that ignores WM_CLOSE, with a helper in its folder and an anti-cheat- and a launcher-named stand-in it started
                var g2 = Process.Start(new ProcessStartInfo(gameExe, "ignore 90 \"" + helperExe + "\" \"" + acExe + "\" \"" + launcherExe + "\"") { UseShellExecute = false }); WaitWindow(io, g2); Thread.Sleep(800);
                var all = new WindowsSampler().Take().Procs; var p2 = all.FirstOrDefault(p => p.Pid == g2.Id);
                var kids = p2 == null ? new List<ProcInfo>() : all.Where(p => p.ParentPid == g2.Id && p.CreateTime >= p2.CreateTime).ToList();
                var helper = kids.FirstOrDefault(p => p.Name.Equals("StandInGameHelper.exe", StringComparison.OrdinalIgnoreCase));
                var ac = kids.FirstOrDefault(p => p.Name.Equals("xigncode_standin.exe", StringComparison.OrdinalIgnoreCase));
                var la = kids.FirstOrDefault(p => p.Name.Equals("upc.exe", StringComparison.OrdinalIgnoreCase));
                Ok(p2 != null && helper != null && ac != null && la != null, "stand-ins started: the game, its helper, an anti-cheat-named and a launcher-named child (" + kids.Count + " children)");
                if (p2 == null || helper == null || ac == null || la == null) return;
                Ok(!GameClose.Offerable(ac, null, extra, self) && Known.IsAntiCheat(ac.Name), "the anti-cheat-named stand-in is never offered");
                Ok(!GameClose.Offerable(la, null, extra, self) && GameClose.IsLauncher(la.Name), "the launcher-named stand-in is excluded");
                var ts = GameClose.Targets(p2, all, @"C:\Windows", self);
                Ok(ts.Count == 2 && ts[0].Pid == g2.Id && ts[1].Pid == helper.Pid, "force close would end the stand-in and its helper only: " + string.Join(", ", ts.Select(x => x.Name)));
                Ok(io.AskToClose(g2.Id) >= 1, "WM_CLOSE reaches the stand-in's window");
                var r2 = new GameCloseRun(p2, "Stand-in game", io, () => new WindowsSampler().Take().Procs, @"C:\Windows", self, s => log.Add(s)) { WaitSeconds = 3 };   // 3 s here to keep the suite short (10 s in the app)
                r2.Ask(DateTime.Now); until = DateTime.Now.AddSeconds(8);
                while (r2.Tick(DateTime.Now) == GameCloseRun.State.Asking && DateTime.Now < until) Thread.Sleep(200);
                Ok(r2.Now == GameCloseRun.State.StillRunning && !g2.HasExited, "the stand-in that ignores WM_CLOSE is still running after the wait - nothing ended by itself");
                Ok(r2.Force() == GameCloseRun.State.Forced && r2.Ended == 2 && g2.WaitForExit(3000), "force close: TerminateProcess ended the stand-in and its helper (" + r2.Ended + ")");
                Ok(ctl.CreateTime(helper.Pid) != helper.CreateTime, "the helper in its folder is gone");
                Ok(io.Alive(ac.Pid, ac.CreateTime) && io.Alive(la.Pid, la.CreateTime), "the anti-cheat-named and launcher-named stand-ins are still running - never touched");
                Ok(log.LastOrDefault() == "close game TestStandInGame.exe: force closed (2 processes)", "logged: " + log.LastOrDefault());
                Ok(io.Terminate(g2.Id, p2.CreateTime, out _ignored) == KillResult.Gone, "a closed game reads as gone, no error");
            }
            finally
            {
                // only this test's own stand-ins (by their path in its temp folder)
                foreach (var pr in Process.GetProcesses())
                {
                    try { var f = pr.MainModule.FileName; if (f.StartsWith(t + "\\", StringComparison.OrdinalIgnoreCase)) { pr.Kill(); pr.WaitForExit(3000); } } catch { }
                }
            }
        }
        static int _ignored;
        static void WaitWindow(WindowsGameCloser io, Process p)
        {
            try { p.WaitForInputIdle(5000); } catch { }
            Thread.Sleep(300);
        }
    }
}
