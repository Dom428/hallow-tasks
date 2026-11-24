// Hallow Tasks 1.5 tests: closing is instant. Close game is one confirm, then Close now ends the game and its own helpers right away
// (no WM_CLOSE, no 10 s wait) - never anti-cheat, launchers, Windows or Hallow; Windows refusing (anti-cheat) shows at once; Settings'
// "Ask it to close first" still waits; "Don't ask again" skips the confirm; a Not responding app is ended right away; Game boost's
// Close is immediate too. Made-up processes with a fake process layer first, then stand-ins this test starts itself in its own temp
// folder (never a real game, anti-cheat, launcher or app).
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
        static void V15() { V15Settings(); V15Steps(); V15Boost(); V15Real(); V15HungReal(); V15BoostReal(); }

        // ---------------- the two settings: defaults, upgrading from 1.4, saving
        static void V15Settings()
        {
            var d = new TaskSettings();
            Ok(!d.CloseAskFirst && !d.CloseNoConfirm, "defaults: end a game right away, and confirm first");
            var file = Path.Combine(TaskSettings.Dir, "settings.json"); string before = File.Exists(file) ? File.ReadAllText(file) : null;
            try
            {
                Directory.CreateDirectory(TaskSettings.Dir);
                File.WriteAllText(file, "{\"Look\":\"dark\",\"HungNotes\":true,\"Sort\":\"impact\"}");   // a 1.4 settings file (no 1.5 fields)
                var old = TaskSettings.Load();
                Ok(old.Look == "dark" && !old.CloseAskFirst && !old.CloseNoConfirm, "upgrading from 1.4: closing is instant, the confirm stays on");
                old.CloseAskFirst = true; old.CloseNoConfirm = true; old.Save();
                var back = TaskSettings.Load();
                Ok(back.CloseAskFirst && back.CloseNoConfirm, "\"Ask it to close first\" and \"Don't ask again\" are saved");
                back.CloseNoConfirm = false; back.Save();
                Ok(!TaskSettings.Load().CloseNoConfirm, "Don't ask again is undone in Settings (Confirm before closing back on)");
            }
            finally { try { if (before == null) File.Delete(file); else File.WriteAllText(file, before); } catch { } }
        }

        // ---------------- the steps with a fake process layer
        static void V15Steps()
        {
            var all = V14Procs(); var game = all.First(p => p.Pid == 5020); var log = new List<string>(); var t0 = new DateTime(2026, 11, 23, 20, 0, 0);
            Func<FakeGameCloser, GameCloseRun> make = f => new GameCloseRun(game, "Delta Force", f, () => all, @"C:\Windows", 9, s => log.Add(s));
            var never = new[] { 5044, 5045, 5048, 5050, 5051, 5060, 5061, 5099, 4500, 4100, 3620, 6200, 6201 };
            Func<FakeGameCloser, bool> neverOthers = f => !f.Calls.Any(c => never.Any(n => c.EndsWith(" " + n)));

            // instant (the default)
            var f1 = new FakeGameCloser(); var r1 = make(f1);
            Ok(r1.Instant && !r1.AskFirst && r1.Now == GameCloseRun.State.Ready && f1.Calls.Count == 0, "instant by default; nothing happens before you click Close now");
            var sw = Stopwatch.StartNew(); var st = r1.EndNow(); sw.Stop();
            Ok(st == GameCloseRun.State.Forced && r1.Ended == 4 && r1.EndedRightAway, "Close now: the game and its 3 own helpers ended at once (" + r1.Ended + ")");
            Ok(!f1.Calls.Any(c => c.StartsWith("close")) && f1.Calls.First() == "kill 5020", "no WM_CLOSE and no waiting: the game is ended first");
            Ok(neverOthers(f1), "never touched: anti-cheat (and what it started), Steam, launchers, Notepad it opened, Hallow, a reused pid, other apps");
            Ok(log.Last() == "close game DeltaForceClient-Win64-Shipping.exe: ended right away (4 processes)", "logged: " + log.Last());
            Ok(r1.DoneText == "Delta Force closed." && sw.ElapsedMilliseconds < 1000, "the status line says it closed (" + sw.ElapsedMilliseconds + " ms)");
            Ok(r1.EndNow() == GameCloseRun.State.Forced && f1.Calls.Count(c => c.StartsWith("kill")) == 4, "a second click does nothing more");

            // access denied: shown at once
            var f2 = new FakeGameCloser(); f2.Denied.Add(5020); var r2 = make(f2);
            sw = Stopwatch.StartNew(); st = r2.EndNow(); sw.Stop();
            Ok(st == GameCloseRun.State.Denied && r2.Error == 5 && sw.ElapsedMilliseconds < 1000, "access denied (anti-cheat): shown at once, no waiting (" + sw.ElapsedMilliseconds + " ms)");
            Ok(f2.Calls.Count == 1 && f2.Calls[0] == "kill 5020", "when Windows refuses the game, nothing else is touched (no helpers, no WM_CLOSE)");
            Ok(r2.DeniedText().Contains("ACE") && r2.DeniedOptions().Length == 3 && r2.DeniedOptions()[0].Contains("Steam"), "the plain explanation and what you can do: " + r2.DeniedText());
            Ok(log.Last().EndsWith("Windows denied it (protected - anti-cheat or admin)"), "logged: " + log.Last());
            Ok(r2.EndNow() == GameCloseRun.State.Denied && f2.Calls.Count == 1, "no second try at a protected game behind your back");
            f2.Gone.Add(5020); Ok(r2.Tick(t0) == GameCloseRun.State.Closed && log.Last().EndsWith("closed by itself after Windows refused"), "the message closes by itself when the game does");

            // refused: anti-cheat, a launcher (even if called directly)
            foreach (var bad in new[] { all.First(p => p.Pid == 5044), all.First(p => p.Pid == 4500) })
            {
                var fb = new FakeGameCloser(); var rb = new GameCloseRun(bad, bad.Name, fb, () => all, @"C:\Windows", 9, s => log.Add(s));
                Ok(rb.EndNow() == GameCloseRun.State.Failed && fb.Calls.Count == 0, "Close now refuses " + bad.Name + " even if called directly - nothing sent");
            }
            var f0 = new FakeGameCloser(); f0.Gone.Add(5020); var r0 = make(f0);
            Ok(r0.EndNow() == GameCloseRun.State.Closed && f0.Calls.All(c => !c.StartsWith("kill")) && log.Last().EndsWith("it had already closed"), "already closed: nothing ended");

            // Settings > "Ask it to close first": still the two steps with the 10 s wait
            var f3 = new FakeGameCloser(); foreach (var n in new[] { 5020, 5030, 5031, 5032 }) f3.IgnoresClose.Add(n); var r3 = make(f3); r3.AskFirst = true;
            Ok(!r3.Instant && r3.WaitSeconds == 10, "ask first: not instant, waits up to 10 s");
            r3.Ask(t0);
            Ok(r3.Now == GameCloseRun.State.Asking && f3.Calls.Contains("close 5020") && !f3.Calls.Any(c => c.StartsWith("kill")), "ask first: asks nicely (WM_CLOSE), nothing ended");
            Ok(r3.Tick(t0.AddSeconds(9)) == GameCloseRun.State.Asking && r3.SecondsLeft(t0.AddSeconds(4)) == 6, "ask first: still waiting at 9 s (6 s left after 4 s)");
            Ok(r3.Tick(t0.AddSeconds(10)) == GameCloseRun.State.StillRunning && !f3.Calls.Any(c => c.StartsWith("kill")), "ask first: after 10 s Force close is offered - nothing ended by itself");
            Ok(r3.Force() == GameCloseRun.State.Forced && !r3.EndedRightAway && log.Last().EndsWith("force closed (4 processes)") && r3.DoneText == "Delta Force was force closed.", "ask first: Force close (your click) - logged: " + log.Last());

            // "Don't ask again": the click itself ends it; the dialog shows only when Windows refuses (or to count down when asking first)
            var f4 = new FakeGameCloser(); var r4 = make(f4); bool turnedOff = false; r4.NeverAsk = () => turnedOff = true;
            Ok(r4.Begin(false) && r4.Now == GameCloseRun.State.Ready && f4.Calls.Count == 0, "confirm on: the click only opens the confirm - nothing ended yet");
            Ok(!r4.Begin(true) && r4.Now == GameCloseRun.State.Forced && r4.Ended == 4 && !turnedOff, "Don't ask again: the click ends it right away, no dialog");
            var f5 = new FakeGameCloser(); f5.Denied.Add(5020); var r5 = make(f5);
            Ok(r5.Begin(true) && r5.Now == GameCloseRun.State.Denied, "Don't ask again + anti-cheat: the explanation still shows at once");
            var f6 = new FakeGameCloser(); f6.IgnoresClose.Add(5020); var r6 = make(f6); r6.AskFirst = true;
            Ok(r6.Begin(true) && r6.Now == GameCloseRun.State.Asking && !f6.Calls.Any(c => c.StartsWith("kill")), "Don't ask again + ask first: asks nicely and shows the countdown");
            var s7 = new TaskSettings(); var r7 = make(new FakeGameCloser()); r7.NeverAsk = () => s7.CloseNoConfirm = true; r7.NeverAsk();
            Ok(s7.CloseNoConfirm, "ticking Don't ask again turns the confirm off in Settings");

            // a Not responding app: ended right away after its confirm, even with "ask first"
            var notes = all.First(p => p.Pid == 6200); var f8 = new FakeGameCloser(); f8.IgnoresClose.Add(6200);
            var r8 = new GameCloseRun(notes, "Notes", f8, () => all, @"C:\Windows", 9, s => log.Add(s), true, game, null) { AskFirst = true };
            Ok(r8.Instant && r8.EndNow() == GameCloseRun.State.Forced && r8.Ended == 2 && !f8.Calls.Any(c => c.StartsWith("close")), "hung app: ended right away (the app and its own helper), no WM_CLOSE");
            Ok(f8.Calls.First() == "kill 6200" && log.Last() == "close app Notes.exe: ended right away (2 processes)", "hung app: the app first - logged: " + log.Last());
            var r9 = new GameCloseRun(game, "Delta Force", new FakeGameCloser(), () => all, @"C:\Windows", 9, s => log.Add(s), true, game, null);
            Ok(r9.EndNow() == GameCloseRun.State.Failed, "the hung-app close never ends a game (games use Close game)");
        }

        // ---------------- Game boost's Close: immediate unless "ask first"
        static void V15Boost()
        {
            var all = V14Procs(); var game = all.First(p => p.Pid == 5020); const string W = @"C:\Windows";
            var groups = Grouping.Build(all, W, null); Func<int, AppGroup> G = n => groups.First(g => g.Members.Any(m => m.Pid == n));
            var fc = new FakeControl(); foreach (var p in all) fc.Created[p.Pid] = p.CreateTime;
            var io = new FakeGameCloser(); var log = new List<string>(); var t0 = new DateTime(2026, 11, 23, 21, 0, 0);
            var eng = new BoostEngine(fc, new EcoKeeper(fc), io, new FakeLauncher(), pid => null, s => log.Add(s));
            io.IgnoresClose.Add(6200);
            Ok(eng.CloseNow(G(6200), true, t0, game, null, W, 9) == null, "boost Close: ended right away");
            var c = eng.Closed.First(x => x.Members.Any(m => m.Pid == 6200));
            Ok(c.State == "forced" && c.Instant && io.Calls.Contains("kill 6200") && !io.Calls.Any(x => x.StartsWith("close")), "boost Close: no WM_CLOSE, no 10 s wait (" + string.Join(", ", io.Calls) + ")");
            Ok(log.Last().StartsWith("boost ") && log.Last().Contains("ended right away"), "logged: " + log.Last());
            Ok(eng.CloseNow(G(4500), false, t0, game, null, W, 9) != null && !io.Calls.Any(x => x.EndsWith(" 4500")), "boost Close refuses a launcher (Steam) - nothing sent");
            var io2 = new FakeGameCloser(); io2.Denied.Add(4100); var eng2 = new BoostEngine(fc, new EcoKeeper(fc), io2, new FakeLauncher(), pid => null, null);
            var msg = eng2.CloseNow(G(4100), false, t0, game, null, W, 9);
            Ok(msg != null && msg.StartsWith("Windows won't let") && eng2.Closed[0].State == "denied", "boost Close, Windows refusing: said at once - " + msg);
            var never = new[] { 5020, 5044, 5048, 3620, 4500, 5061 };
            Ok(!io.Calls.Concat(io2.Calls).Any(x => never.Any(n => x.EndsWith(" " + n))), "boost Close never touches the game, anti-cheat, Windows, Hallow or launchers");
            // reopen after still works
            var la = new FakeLauncher(); var eng3 = new BoostEngine(fc, new EcoKeeper(fc), new FakeGameCloser(), la, pid => null, null);
            eng3.CloseNow(G(6200), true, t0, game, null, W, 9); eng3.Restore(true);
            Ok(la.Started.Count == 1 && la.Started[0].EndsWith("Notes.exe"), "reopen after: an app ended right away starts again after the game");
            // ask first: the old way
            var io4 = new FakeGameCloser(); io4.IgnoresClose.Add(6200); var eng4 = new BoostEngine(fc, new EcoKeeper(fc), io4, new FakeLauncher(), pid => null, null);
            eng4.Close(G(6200), false, t0, 9); eng4.Tick(t0.AddSeconds(9));
            Ok(eng4.Closed[0].State == "asking" && io4.Calls.Contains("close 6200") && !io4.Calls.Any(x => x.StartsWith("kill")), "ask first: boost still asks nicely and waits");
            eng4.Tick(t0.AddSeconds(10)); Ok(eng4.Closed[0].State == "still", "ask first: Force close offered after 10 s");
            // a saved profile applied by itself only asks nicely
            var io5 = new FakeGameCloser(); var eng5 = new BoostEngine(fc, new EcoKeeper(fc), io5, new FakeLauncher(), pid => null, null);
            var applied = eng5.Apply(new List<BoostItem> { new BoostItem { Exe = Boost.Exe(G(6200)), Action = "close" } }, groups, game, null, null, W, 9, t0);
            Ok(applied == 1 && io5.Calls.Contains("close 6200") && !io5.Calls.Any(x => x.StartsWith("kill")),"a Boost profile applied when a game starts never ends anything by itself");
        }

        // ---------------- stand-ins: a game that ignores WM_CLOSE, its helper, an anti-cheat- and a launcher-named child
        static void V15Real()
        {
            if (!File.Exists(StandInExe) || !File.Exists(DummyExe)) { Ok(false, "StandIn.exe and Dummy.exe are built"); return; }
            var t = Path.Combine(Dir, "standins15"); var bin = Path.Combine(t, @"Quick Game\Binaries\Win64"); var other = Path.Combine(t, "Other");
            Directory.CreateDirectory(bin); Directory.CreateDirectory(other);
            var gameExe = Path.Combine(bin, "TestQuickGame.exe"); var helperExe = Path.Combine(bin, "QuickGameHelper.exe");
            var acExe = Path.Combine(other, "xigncode_standin.exe"); var launcherExe = Path.Combine(other, "upc.exe");
            File.Copy(StandInExe, gameExe, true); File.Copy(DummyExe, helperExe, true); File.Copy(DummyExe, acExe, true); File.Copy(DummyExe, launcherExe, true);
            var extra = new[] { "TestQuickGame" };
            var io = new WindowsGameCloser(); var ctl = new WindowsControl(); var self = Process.GetCurrentProcess().Id; var log = new List<string>();
            try
            {
                // 1: the default - ended right away
                var g1 = Process.Start(new ProcessStartInfo(gameExe, "ignore 90 \"" + helperExe + "\" \"" + acExe + "\" \"" + launcherExe + "\"") { UseShellExecute = false }); WaitWindow(io, g1); Thread.Sleep(800);
                var all = new WindowsSampler().Take().Procs; var p1 = all.FirstOrDefault(p => p.Pid == g1.Id);
                var kids = p1 == null ? new List<ProcInfo>() : all.Where(p => p.ParentPid == g1.Id && p.CreateTime >= p1.CreateTime).ToList();
                var helper = kids.FirstOrDefault(p => p.Name.Equals("QuickGameHelper.exe", StringComparison.OrdinalIgnoreCase));
                var ac = kids.FirstOrDefault(p => p.Name.Equals("xigncode_standin.exe", StringComparison.OrdinalIgnoreCase));
                var la = kids.FirstOrDefault(p => p.Name.Equals("upc.exe", StringComparison.OrdinalIgnoreCase));
                Ok(p1 != null && helper != null && ac != null && la != null && p1.Pid > 4, "stand-ins started: the game, its helper, an anti-cheat-named and a launcher-named child");
                if (p1 == null || helper == null || ac == null || la == null) return;
                Ok(GameClose.Offerable(p1, null, extra, self), "the stand-in game is offered Close game");
                var r1 = new GameCloseRun(p1, "Quick game", io, () => new WindowsSampler().Take().Procs, @"C:\Windows", self, s => log.Add(s));
                var sw = Stopwatch.StartNew(); var st = r1.EndNow(); var exited = g1.WaitForExit(2000); sw.Stop();
                Ok(st == GameCloseRun.State.Forced && r1.Ended == 2 && exited, "Close now: TerminateProcess ended the stand-in game and its helper at once (" + sw.ElapsedMilliseconds + " ms, " + r1.Ended + " processes)");
                Ok(sw.ElapsedMilliseconds < 4000, "no 10 s wait (" + sw.ElapsedMilliseconds + " ms)");
                Thread.Sleep(300);
                Ok(ctl.CreateTime(helper.Pid) != helper.CreateTime, "its helper is gone");
                Ok(io.Alive(ac.Pid, ac.CreateTime) && io.Alive(la.Pid, la.CreateTime), "the anti-cheat-named and launcher-named stand-ins are still running - never touched");
                Ok(log.LastOrDefault() == "close game TestQuickGame.exe: ended right away (2 processes)", "logged: " + log.LastOrDefault());

                // 2: Settings > "Ask it to close first" still waits
                var g2 = Process.Start(new ProcessStartInfo(gameExe, "ignore 60") { UseShellExecute = false }); WaitWindow(io, g2);
                var p2 = new WindowsSampler().Take().Procs.FirstOrDefault(p => p.Pid == g2.Id);
                Ok(p2 != null, "a second stand-in game started"); if (p2 == null) return;
                var r2 = new GameCloseRun(p2, "Quick game", io, () => new WindowsSampler().Take().Procs, @"C:\Windows", self, s => log.Add(s)) { AskFirst = true, WaitSeconds = 3 };   // 3 s here (10 s in the app)
                Ok(r2.Begin(true) && r2.Now == GameCloseRun.State.Asking, "ask first: it's asked nicely and the countdown shows");
                Thread.Sleep(1500); r2.Tick(DateTime.Now);
                Ok(r2.Now == GameCloseRun.State.Asking && !g2.HasExited, "ask first: still waiting after 1.5 s - nothing ended");
                var until = DateTime.Now.AddSeconds(8);
                while (r2.Tick(DateTime.Now) == GameCloseRun.State.Asking && DateTime.Now < until) Thread.Sleep(200);
                Ok(r2.Now == GameCloseRun.State.StillRunning && !g2.HasExited, "ask first: still running after the wait - Force close offered, nothing ended by itself");
                Ok(r2.Force() == GameCloseRun.State.Forced && g2.WaitForExit(3000), "ask first: Force close (a click) ended it");
            }
            finally { KillUnder(t); }
        }

        // ---------------- a stand-in that really stops answering: ended right away
        static void V15HungReal()
        {
            if (!File.Exists(StandInExe)) { Ok(false, "StandIn.exe is built"); return; }
            var t = Path.Combine(Dir, "hung15"); Directory.CreateDirectory(t);
            var hungExe = Path.Combine(t, "TestStuckApp.exe"); File.Copy(StandInExe, hungExe, true);
            var self = Process.GetCurrentProcess().Id; var log = new List<string>();
            try
            {
                var h = Process.Start(new ProcessStartInfo(hungExe, "hang 120") { UseShellExecute = false });
                ProcInfo ph = null; var until = DateTime.Now.AddSeconds(20);
                while (DateTime.Now < until) { Thread.Sleep(700); ph = new WindowsSampler().Take().Procs.FirstOrDefault(p => p.Pid == h.Id); if (ph != null && ph.Hung) break; }
                Ok(ph != null && ph.Hung, "the stand-in shows as Not responding");
                if (ph == null || !ph.Hung) return;
                var run = new GameCloseRun(ph, "Stuck app", new WindowsGameCloser(), () => new WindowsSampler().Take().Procs, @"C:\Windows", self, s => log.Add(s), true, null, null);
                var sw = Stopwatch.StartNew(); var st = run.EndNow(); var exited = h.WaitForExit(2000); sw.Stop();
                Ok(run.Instant && st == GameCloseRun.State.Forced && exited && sw.ElapsedMilliseconds < 4000, "Close (not responding): ended right away after the confirm (" + sw.ElapsedMilliseconds + " ms)");
                Ok(log.LastOrDefault() == "close app TestStuckApp.exe: ended right away (1 process)", "logged: " + log.LastOrDefault());
            }
            finally { KillUnder(t); }
        }

        // ---------------- Game boost's Close with a stand-in that ignores WM_CLOSE
        static void V15BoostReal()
        {
            if (!File.Exists(StandInExe) || !File.Exists(DummyExe)) { Ok(false, "StandIn.exe and Dummy.exe are built"); return; }
            var t = Path.Combine(Dir, "boost15"); Directory.CreateDirectory(Path.Combine(t, "a")); Directory.CreateDirectory(Path.Combine(t, "b"));
            var stubExe = Path.Combine(t, @"a\BoostQuickStub.exe"); var dumExe = Path.Combine(t, @"b\BoostQuickDummy.exe");
            File.Copy(StandInExe, stubExe, true); File.Copy(DummyExe, dumExe, true);
            var self = Process.GetCurrentProcess().Id; var ctl = new WindowsControl(); var io = new WindowsGameCloser();
            try
            {
                var stub = Process.Start(new ProcessStartInfo(stubExe, "ignore 60") { UseShellExecute = false });
                var dum = Process.Start(new ProcessStartInfo(dumExe, "60") { UseShellExecute = false, CreateNoWindow = true });
                try { stub.WaitForInputIdle(5000); } catch { } Thread.Sleep(500);
                var procs = new WindowsSampler().Take().Procs; var groups = Grouping.Build(procs, @"C:\Windows", null);
                var gs = groups.FirstOrDefault(g => g.Members.Any(m => m.Pid == stub.Id));
                Ok(gs != null && Boost.WhyNot(gs, null, null, null, @"C:\Windows", self) == null, "boost: the stand-in can be closed");
                if (gs == null) return;
                var eng = new BoostEngine(ctl, new EcoKeeper(ctl), io, new WindowsLauncher(), ProcCmd.Read, null);
                var sw = Stopwatch.StartNew(); var e = eng.CloseNow(gs, false, DateTime.Now, null, null, @"C:\Windows", self); var exited = stub.WaitForExit(2000); sw.Stop();
                Ok(e == null && eng.Closed[0].State == "forced" && exited && sw.ElapsedMilliseconds < 4000, "boost Close: the stand-in that ignores WM_CLOSE ended right away (" + sw.ElapsedMilliseconds + " ms)");
                Ok(!dum.HasExited, "the other stand-in is untouched");
            }
            finally { KillUnder(t); }
        }
    }
}
