// Hallow Tasks 1.4 tests: "Not responding" apps (who Close is offered for, the 15 s note held while you play, the safe flow in app
// mode with a fake process layer, then a stand-in that really stops reading its messages), Startup apps switched Task Manager's way
// (StartupApproved) in a FAKE registry root and temp folders only - the real startup entries are read before and after and must be
// unchanged - and Game boost (what's listed, every exclusion, lower / Efficiency / close / Force close / restore / reopen with fakes,
// then with stand-ins this test starts itself). Never a real app, game, anti-cheat or startup entry.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace HallowTasks.Tests
{
    public static partial class Run
    {
        static void V14() { V14Hung(); V14Startup(); V14Boost(); V14Pct(); V14HungReal(); V14BoostReal(); }

        static List<ProcInfo> V14Procs()
        {
            var l = V13Procs();
            var notes = P(6200, 3620, "Notes.exe", @"C:\Users\A\AppData\Local\Notes\Notes.exe", 0, 90, 0, true); notes.Hung = true; l.Add(notes);
            var helper = P(6201, 6200, "NotesHelper.exe", @"C:\Users\A\AppData\Local\Notes\NotesHelper.exe"); l.Add(helper);
            return l;
        }

        // ---------------- Not responding
        static void V14Hung()
        {
            var all = V14Procs(); Func<int, ProcInfo> pid = n => all.First(p => p.Pid == n); var game = pid(5020); var notes = pid(6200);
            const string W = @"C:\Windows";
            Ok(HungClose.WhyNot(notes, game, null, W, 9) == null, "not responding: Close is offered for an ordinary app");
            foreach (var p in new[] { game, pid(5044), pid(5048), pid(4500), pid(5050), pid(5051), pid(3620), pid(5061), pid(1032) })
            {
                var x = p.Clone(); x.Hung = true;
                Ok(HungClose.WhyNot(x, game, null, W, 9) != null, "not responding: never offered for " + p.Name + " - " + HungClose.WhyNot(x, game, null, W, 9));
            }
            var g2 = P(77, 0, "MyGame.exe", @"D:\G\MyGame.exe", window: true); g2.Hung = true;
            Ok(HungClose.WhyNot(g2, null, new[] { "MyGame" }, W, 9).Contains("Close game"), "a hung game points at Close game (Settings' extra games too)");
            Ok(HungClose.WhyNot(notes, game, null, W, 6200) != null && HungClose.WhyNot(P(4, 0, "System", null, session: 0), null, null, W, 9) != null, "never Tasks itself or pid 4");
            var groups = Grouping.Build(all, W, null);
            var ng = groups.First(g => g.Members.Any(m => m.Pid == 6200));
            Ok(ng.Hung && HungClose.Pick(ng, null, game, null, W, 9) == notes, "the app's row picks the hung process");
            var gameHung = all.Select(p => p.Clone()).ToList(); gameHung.First(p => p.Pid == 5020).Hung = true;
            var gg = Grouping.Build(gameHung, W, null).First(g => g.Members.Any(m => m.Pid == 5020));
            Ok(HungClose.Pick(gg, null, game, null, W, 9) == null && GameClose.Pick(gg, null, game, null, 9) != null, "a hung game's row: no Close (not responding) - Close game instead");
            Ok(HungClose.Pick(groups.First(g => g.Members.Any(m => m.Pid == 4100)), null, game, null, W, 9) == null, "a responding app: no Close (not responding)");

            // the 15 s note, once, held while a game runs
            var t0 = new DateTime(2026, 11, 22, 21, 0, 0); var hw = new HungWatch();
            Ok(hw.Due(all, null, null, W, 9, t0).Count == 0 && hw.Due(all, null, null, W, 9, t0.AddSeconds(14)).Count == 0, "note: nothing before 15 s");
            var d = hw.Due(all, null, null, W, 9, t0.AddSeconds(15));
            Ok(d.Count == 1 && d[0].Pid == 6200 && hw.Count == 1, "note: after 15 s, one note for the hung app");
            Ok(hw.Due(all, null, null, W, 9, t0.AddSeconds(30)).Count == 0, "note: only once");
            var hw2 = new HungWatch(); hw2.Due(all, game, null, W, 9, t0);
            Ok(hw2.Due(all, game, null, W, 9, t0.AddSeconds(40)).Count == 0, "note: held while a game runs");
            Ok(hw2.Due(all, null, null, W, 9, t0.AddSeconds(41)).Count == 1, "note: given after the game if it's still not responding");
            var fine = all.Select(p => p.Clone()).ToList(); fine.First(p => p.Pid == 6200).Hung = false; hw.Due(fine, null, null, W, 9, t0.AddSeconds(31));
            Ok(hw.Due(all, null, null, W, 9, t0.AddSeconds(40)).Count == 0 && hw.Due(all, null, null, W, 9, t0.AddSeconds(56)).Count == 1, "note: if it recovers and hangs again, the 15 s start over");
            var acHung = all.Select(p => p.Clone()).ToList(); acHung.First(p => p.Pid == 5044).Hung = true; acHung.First(p => p.Pid == 6200).Hung = false;
            var hw3 = new HungWatch(); hw3.Due(acHung, null, null, W, 9, t0);
            Ok(hw3.Due(acHung, null, null, W, 9, t0.AddSeconds(20)).Count == 0, "note: never about anti-cheat");

            // the safe flow in app mode (fake process layer)
            var log = new List<string>();
            Func<FakeGameCloser, ProcInfo, GameCloseRun> make = (f, target) => new GameCloseRun(target, Known.Friendly(target), f, () => all, W, 9, s => log.Add(s), true, game, null);
            Func<FakeGameCloser, bool> onlyNotes = f => f.Calls.All(c => c.EndsWith(" 6200") || c.EndsWith(" 6201"));
            var f1 = new FakeGameCloser(); var r1 = make(f1, notes);
            Ok(r1.App && r1.WaitSeconds == 5 && r1.Now == GameCloseRun.State.Ready && f1.Calls.Count == 0, "app close: waits 5 s; nothing happens before you click Close app");
            r1.Ask(t0);
            Ok(f1.Calls.Contains("close 6200") && !f1.Calls.Any(c => c.StartsWith("kill")) && r1.Tick(t0.AddSeconds(1)) == GameCloseRun.State.Closed, "app close: asks nicely (WM_CLOSE) and it closes");
            Ok(log.Last() == "close app Notes.exe: closed nicely", "logged: " + log.Last());
            var f2 = new FakeGameCloser(); f2.IgnoresClose.Add(6200); f2.IgnoresClose.Add(6201); var r2 = make(f2, notes); r2.Ask(t0);
            Ok(r2.Tick(t0.AddSeconds(4)) == GameCloseRun.State.Asking && r2.Tick(t0.AddSeconds(5)) == GameCloseRun.State.StillRunning && !f2.Calls.Any(c => c.StartsWith("kill")), "app close: still running after 5 s - Force close offered, nothing ended by itself");
            Ok(r2.Force() == GameCloseRun.State.Forced && r2.Ended == 2 && f2.Calls.Count(c => c.StartsWith("kill")) == 2 && f2.Calls.First(c => c.StartsWith("kill")) == "kill 6200", "app close: Force close ends the app first, then its own helper");
            Ok(onlyNotes(f1) && onlyNotes(f2), "app close: nothing else is ever asked or ended");
            Ok(log.Last() == "close app Notes.exe: force closed (2 processes)", "logged: " + log.Last());
            foreach (var bad in new[] { game, pid(5044), pid(4500), pid(3620), pid(5061) })
            {
                var x = bad.Clone(); x.Hung = true; var f3 = new FakeGameCloser(); var r3 = make(f3, x); r3.Ask(t0);
                Ok(r3.Now == GameCloseRun.State.Failed && r3.Force() == GameCloseRun.State.Failed && f3.Calls.Count == 0, "app close refuses " + bad.Name + " even if called directly - not even WM_CLOSE");
            }
            var f4 = new FakeGameCloser(); f4.IgnoresClose.Add(6200); f4.Denied.Add(6200); var r4 = make(f4, notes); r4.Ask(t0); r4.Tick(t0.AddSeconds(6));
            Ok(r4.Force() == GameCloseRun.State.Denied && r4.DeniedText().StartsWith("Windows protects Notes") && r4.DeniedOptions().Length == 3, "app close: Windows refusing is explained: " + r4.DeniedText());
        }

        // ---------------- Startup: StartupApproved, fake root only
        static string RealStartupSnapshot()
        {
            var sb = new StringBuilder();
            foreach (var path in new[] { StartupStore.UserRun, StartupPaths.ApprovedPath + "Run", StartupPaths.ApprovedPath + "Run32", StartupPaths.ApprovedPath + "StartupFolder" })
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(path))
                        if (k != null) foreach (var n in k.GetValueNames().OrderBy(x => x)) { var v = k.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames); sb.Append(path + "|" + n + "=" + (v is byte[] ? BitConverter.ToString((byte[])v) : Convert.ToString(v)) + "\n"); }
                }
                catch { }
            try { var f = Environment.GetFolderPath(Environment.SpecialFolder.Startup); if (Directory.Exists(f)) foreach (var x in Directory.GetFiles(f).OrderBy(x => x)) sb.Append(x + "|" + new FileInfo(x).Length + "\n"); } catch { }
            return sb.ToString();
        }
        static void V14Startup()
        {
            var before = RealStartupSnapshot();
            var root = @"Software\HallowTasks\Test-v14-" + Process.GetCurrentProcess().Id; var folders = Path.Combine(Dir, "startup14");
            var P2 = StartupPaths.ForTest(root, folders);
            Ok(P2.UserRun.StartsWith(root) && P2.MachineRun.StartsWith(root) && P2.UserApproved.StartsWith(root) && P2.MachineApproved.StartsWith(root) && P2.UserFolder.StartsWith(folders) && P2.CommonFolder.StartsWith(folders) && !P2.Tasks,
                "startup test paths: a fake registry root and temp folders only (no real HKLM, no Task Scheduler)");
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(P2.UserRun)) { k.SetValue("FakeChat", "\"C:\\Fake\\chat.exe\" --tray"); k.SetValue("FakeExp", "%LOCALAPPDATA%\\x.exe", RegistryValueKind.ExpandString); }
                using (var k = Registry.CurrentUser.CreateSubKey(P2.MachineRun)) k.SetValue("FakeAudio", "C:\\Fake\\audio.exe -background");
                Directory.CreateDirectory(P2.UserFolder); Directory.CreateDirectory(P2.CommonFolder);
                File.WriteAllText(Path.Combine(P2.UserFolder, "Fake Tool.cmd"), "@echo off"); File.WriteAllText(Path.Combine(P2.CommonFolder, "Fake Shared.cmd"), "@echo off"); File.WriteAllText(Path.Combine(P2.UserFolder, "desktop.ini"), "");
                var ws = new WindowsStartup(P2); var items = ws.Read();
                Func<string, StartupItem> it = n => ws.Read().FirstOrDefault(x => x.Name == n);
                Ok(items.Count == 5, "startup: 2 of your Run entries, 1 for all users, your Startup folder file, the all-users folder file - not desktop.ini (" + items.Count + ")" + (items.Count == 5 ? "" : ": " + string.Join(", ", items.Select(x => x.Name))));
                var chat = items.FirstOrDefault(x => x.Name == "FakeChat"); var tool = items.FirstOrDefault(x => x.Name == "Fake Tool"); var audio = items.FirstOrDefault(x => x.Name == "FakeAudio"); var shared = items.FirstOrDefault(x => x.Name == "Fake Shared");
                Ok(chat != null && chat.Mine && chat.Kind == "run" && chat.Approved == "Run" && tool != null && tool.Mine && tool.Kind == "folder" && tool.Approved == "StartupFolder" && tool.ApprovedName == "Fake Tool.cmd", "startup: yours can be switched (Run key and Startup folder)");
                Ok(audio != null && !audio.Mine && shared != null && !shared.Mine && items.All(x => x.Enabled), "startup: all-users entries are shown, not switchable; everything on (no StartupApproved value = on)");
                // Disable: StartupApproved 03 + when; the entry itself stays
                var t1 = DateTime.UtcNow;
                Ok(ws.SetOn(chat, false) == null, "disable your Run entry: done");
                byte[] v; using (var k = Registry.CurrentUser.OpenSubKey(P2.UserApproved + "Run")) v = k == null ? null : k.GetValue("FakeChat") as byte[];
                Ok(v != null && v.Length == 12 && v[0] == 3 && v[1] == 0 && Math.Abs((DateTime.FromFileTimeUtc(BitConverter.ToInt64(v, 4)) - t1).TotalMinutes) < 2, "it's Task Manager's StartupApproved value: 03 00 00 00 + the time (" + (v == null ? "none" : BitConverter.ToString(v)) + ")");
                using (var k = Registry.CurrentUser.OpenSubKey(P2.UserRun)) Ok((string)k.GetValue("FakeChat") == "\"C:\\Fake\\chat.exe\" --tray" && k.GetValueKind("FakeExp") == RegistryValueKind.ExpandString, "the Run entry itself is untouched - nothing deleted or moved");
                var c2 = it("FakeChat"); Ok(c2 != null && !c2.Enabled && c2.OffSince != null && Math.Abs((c2.OffSince.Value - DateTime.Now).TotalMinutes) < 2, "read back: Off since now");
                Ok(ws.SetOn(c2, true) == null, "enable it again");
                using (var k = Registry.CurrentUser.OpenSubKey(P2.UserApproved + "Run")) v = k.GetValue("FakeChat") as byte[];
                Ok(v != null && v.Length == 12 && v[0] == 2 && v.Skip(1).All(b => b == 0), "enabled: 02 and zeros, like Task Manager (" + BitConverter.ToString(v) + ")");
                Ok(it("FakeChat").Enabled && it("FakeChat").OffSince == null, "read back: On");
                Ok(ws.SetOn(tool, false) == null && !it("Fake Tool").Enabled && File.Exists(Path.Combine(P2.UserFolder, "Fake Tool.cmd")), "your Startup folder file: switched off, the file stays");
                using (var k = Registry.CurrentUser.OpenSubKey(P2.UserApproved + "StartupFolder")) Ok(k != null && (k.GetValue("Fake Tool.cmd") as byte[])[0] == 3, "under StartupApproved\\StartupFolder by its file name");
                ws.SetOn(it("Fake Tool"), true);
                Ok(ws.SetOn(audio, false) != null && ws.SetOn(shared, false) != null, "all-users entries: refused (Windows' page, admin)");
                using (var k = Registry.CurrentUser.OpenSubKey(P2.MachineApproved.TrimEnd('\\'))) Ok(k == null, "nothing written for all-users entries");
                Ok(ws.SetOn(new StartupItem { Name = "x", Kind = "task", Mine = true, Approved = "Run", ApprovedName = "x" }, false) != null, "scheduled tasks: never switched here");
                // StartupApproved values Windows writes
                DateTime? since;
                Ok(Approval.IsOn(null, out since) && Approval.IsOn(new byte[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, out since) && !Approval.IsOn(new byte[] { 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, out since) && since == null, "StartupApproved: no value / even = on, odd = off");
                // the boot event log (made-up event XML) and impact with Windows' measured time
                string exe; int ms;
                Ok(BootEvent.Parse("<Event><EventData><Data Name='Name'>Discord.exe</Data><Data Name='FriendlyName'>Discord</Data><Data Name='TotalTime'>1900</Data><Data Name='DegradationTime'>1200</Data></EventData></Event>", out exe, out ms) && exe == "discord" && ms == 1900, "boot log: exe and measured time from the event (" + exe + ", " + ms + " ms)");
                Ok(!BootEvent.Parse("<Event/>", out exe, out ms), "boot log: an event without a name is skipped");
                var fake = new FakeStartup(); var fi = fake.Read(); StartupRate.Apply(fi, Sample1(), fake.BootTimes(), DateTime.FromFileTime(T0).AddHours(10), p => p.CpuTime / 1e7);
                var dsc = fi.First(x => x.Name == "Discord");
                Ok(dsc.BootMs == 1900 && dsc.Impact == "High" && dsc.Why.Contains("1.9 s at startup"), "impact: Windows' measured boot time counts and is shown: " + dsc.Why);
                Ok(fi.First(x => x.Name == "Spotify").BootMs == -1 && StartupImpact.Secs(4200) == "4.2 s" && StartupImpact.Secs(800) == "800 ms", "no measurement = -; seconds written plainly");
                Ok(fake.SetOn(fi.First(x => x.Name == "SecurityHealth"), false) != null && fake.SetOn(fi.First(x => x.Name == "Spotify"), false) == null && !fake.Read().First(x => x.Name == "Spotify").Enabled, "the --snap list switches only its own entries");
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(root, false); } catch { }
                try { Directory.Delete(folders, true); } catch { }
            }
            using (var k = Registry.CurrentUser.OpenSubKey(root)) Ok(k == null && !Directory.Exists(folders), "the fake root and folders are cleaned up");
            Ok(RealStartupSnapshot() == before, "your real startup entries and StartupApproved values are exactly as before (read-only check)");
        }

        // ---------------- Game boost (fakes)
        static List<ProcInfo> V14BoostProcs()
        {
            var l = V13Procs();
            l.Add(P(4300, 3620, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 9, 900, 4, true)); l.Add(P(4310, 4300, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 6, 600, 3));
            l.Add(P(4700, 3620, "obs64.exe", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", 4, 380, 7, true));
            l.Add(P(4800, 3620, "Spotify.exe", @"C:\Users\A\AppData\Roaming\Spotify\Spotify.exe", 2, 200, 0, true));
            l.Add(P(6200, 3620, "Notes.exe", @"C:\Users\A\AppData\Local\Notes\Notes.exe", 3, 300, 0, true));
            l.Add(P(1720, 1032, "svchost.exe", @"C:\Windows\System32\svchost.exe", 5, 60, session: 0));
            l.Add(P(7100, 1720, "SearchIndexer.exe", @"C:\Windows\System32\SearchIndexer.exe", 4, 50, session: 0));
            l.First(p => p.Pid == 4100).Cpu = 5; l.First(p => p.Pid == 4100).Ram = 300L << 20;
            l.First(p => p.Pid == 4500).Cpu = 2; l.First(p => p.Pid == 4500).Ram = 300L << 20;
            l.First(p => p.Pid == 5061).Cpu = 8; l.First(p => p.Pid == 5061).Ram = 400L << 20;   // Hallow Clips: busy, never listed
            l.First(p => p.Pid == 5044).Cpu = 3;
            return l;
        }
        static void V14Boost()
        {
            var all = V14BoostProcs(); const string W = @"C:\Windows"; var game = all.First(p => p.Pid == 5020); var skip = new[] { "spotify" };
            var groups = Grouping.Build(all, W, null);
            Func<int, AppGroup> G2 = n => groups.First(g => g.Members.Any(m => m.Pid == n));
            var rows = Boost.List(groups, game, null, skip, W, 9, 20); var keys = rows.Select(r => r.Group.Key).ToList();
            Ok(keys.Contains("chrome") && keys.Contains("discord") && keys.Contains("obs") && keys.Contains("steam") && rows.Any(r => r.Group.Members.Any(m => m.Pid == 6200)), "boost lists what's using the PC: " + string.Join(", ", rows.Select(r => r.Group.Title)));
            Ok(rows[0].Group.Key == "chrome", "biggest first (Chrome)");
            foreach (var n in new[] { 5020, 5044, 5048, 3620, 1720, 7100, 5061, 4800, 1032 })
                Ok(!rows.Any(r => r.Group.Members.Any(m => m.Pid == n)) && Boost.WhyNot(G2(n), game, null, skip, W, 9) != null, "boost never lists " + all.First(p => p.Pid == n).Name + ": " + Boost.WhyNot(G2(n), game, null, skip, W, 9));
            Ok(Boost.WhyNot(G2(4100), game, null, skip, W, 4100) != null, "boost never lists Tasks itself");
            Ok(Boost.WhyNot(G2(4100), null, new[] { "Discord" }, skip, W, 9) != null, "boost never lists Settings' extra games");
            var steam = rows.First(r => r.Group.Key == "steam");
            Ok(!steam.CanClose && steam.Note != null && rows.Where(r => r.Group.Key != "steam").All(r => r.CanClose), "launchers: lower only, never closed");

            var fc = new FakeControl(); foreach (var p in all) fc.Created[p.Pid] = p.CreateTime;
            var io = new FakeGameCloser(); var la = new FakeLauncher(); var log = new List<string>();
            var eng = new BoostEngine(fc, new EcoKeeper(fc), io, la, pid => pid == 4300 ? "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --started-by-test" : null, s => log.Add(s));
            var t0 = new DateTime(2026, 11, 22, 21, 0, 0);
            Ok(!eng.Active && fc.Calls.Count == 0 && io.Calls.Count == 0, "boost: nothing happens until you click");
            Ok(eng.Lower(G2(4100), Prio.BelowNormal, 9) == null && fc.Prios[4100] == Prio.BelowNormal, "Below normal: Discord lowered");
            Ok(eng.Efficiency(G2(4700), 9) == null && fc.Eco.Contains(4700), "Efficiency mode: OBS");
            Ok(eng.Close(G2(4300), true, t0, 9) == null && io.Calls.Contains("close 4300") && io.Calls.Contains("close 4310") && !io.Calls.Any(c => c.StartsWith("kill")), "Close: Chrome asked nicely (WM_CLOSE), nothing ended");
            Ok(eng.Close(G2(4500), false, t0, 9) != null && !io.Calls.Any(c => c.EndsWith(" 4500")), "Close refuses a launcher (Steam) - nothing sent");
            io.IgnoresClose.Add(6200); Ok(eng.Close(G2(6200), false, t0, 9) == null, "Close: Notes asked nicely");
            Ok(eng.Force(G2(6200).Key, game, null, W, 9) != null && !io.Calls.Any(c => c.StartsWith("kill")), "Force close before the wait is over: refused");
            eng.Tick(t0.AddSeconds(5));
            Ok(eng.Closed.First(c => c.Key == "chrome").State == "closed" && eng.Closed.First(c => c.Members[0].Pid == 6200).State == "asking", "Chrome closed; Notes still being waited for");
            eng.Tick(t0.AddSeconds(10));
            var notesC = eng.Closed.First(c => c.Members[0].Pid == 6200);
            Ok(notesC.State == "still" && !io.Calls.Any(c => c.StartsWith("kill")), "after 10 s Notes is still running: Force close offered, nothing ended by itself");
            Ok(eng.Force(notesC.Key, game, null, W, 9) == null && notesC.State == "forced" && io.Calls.Count(c => c.StartsWith("kill")) == 1 && io.Calls.Contains("kill 6200"), "Force close (your click): only Notes ended");
            var never = new[] { 5020, 5044, 5048, 3620, 1720, 7100, 5061, 4500, 4800 };
            Ok(!io.Calls.Concat(fc.Calls).Any(c => never.Any(n => c.EndsWith(" " + n) || c.Contains(" " + n + " "))), "never touched: the game, anti-cheat, Windows, Hallow, Steam, the opted-out app");
            var profile = eng.Done.ToList();
            Ok(profile.Count == 4 && profile.Any(d => d.Exe == "chrome" && d.Action == "close" && d.Reopen) && profile.Any(d => d.Exe == "discord" && d.Action == "below") && profile.Any(d => d.Exe == "obs64" && d.Action == "eco"), "what you did can be saved as a Boost profile: " + string.Join(", ", profile.Select(d => d.Exe + "=" + d.Action)));
            // after the game
            var msg = eng.Restore(true);
            Ok(fc.Prios[4100] == Prio.Normal && !fc.Eco.Contains(4700), "after the game: Discord's priority and OBS's Efficiency are back");
            Ok(la.Started.Count == 1 && la.Started[0] == "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe --started-by-test", "after the game: only the app you ticked is reopened, the same way it was started: " + string.Join(" | ", la.Started));
            Ok(!eng.Active && msg.Contains("1 app reopened"), "restored: " + msg);
            // a saved profile when a game starts: asks nicely only
            var io2 = new FakeGameCloser(); foreach (var p in all) io2.IgnoresClose.Add(p.Pid); var fc2 = new FakeControl(); foreach (var p in all) fc2.Created[p.Pid] = p.CreateTime;
            var eng2 = new BoostEngine(fc2, new EcoKeeper(fc2), io2, new FakeLauncher(), null, null);
            var n2 = eng2.Apply(profile, groups, game, null, skip, W, 9, t0); eng2.Tick(t0.AddSeconds(60));
            Ok(n2 == 4 && fc2.Prios[4100] == Prio.BelowNormal && io2.Calls.Contains("close 4300") && !io2.Calls.Any(c => c.StartsWith("kill")), "Boost profile applied when a game starts: lowered and asked nicely - never Force close (" + n2 + ")");
            var eng3 = new BoostEngine(fc2, new EcoKeeper(fc2), new FakeGameCloser(), new FakeLauncher(), null, null);
            Ok(eng3.Apply(new List<BoostItem> { new BoostItem { Exe = "discord", Action = "close" } }, groups, game, null, new[] { "discord" }, W, 9, t0) == 0, "the profile skips apps you opted out since");
            Ok(eng3.Apply(new List<BoostItem> { new BoostItem { Exe = "sguard64", Action = "close" }, new BoostItem { Exe = "deltaforceclient-win64-shipping", Action = "idle" }, new BoostItem { Exe = "explorer", Action = "close" } }, groups, game, null, null, W, 9, t0) == 0, "a profile can never boost the game, anti-cheat or Windows");
            Ok(log.All(l => l.StartsWith("boost ") && !l.Contains("\\")), "the log: app names and what happened, no paths");
        }

        // ---------------- "45%"
        static void V14Pct()
        {
            Ok(Fmt.Pct(45) == "45%" && Fmt.Pct(4.5) == "4.5%" && Fmt.Pct(0) == "0%", "percentages: 45%, 4.5%, 0% - no space");
            var src = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Run).Assembly.Location), "..", "src"));
            if (!Directory.Exists(src)) { Ok(false, "the source folder is next to tests"); return; }
            // every string literal: no "45 %" (a digit, a space, %) and none that starts with " %" or is a lone " %" after a number
            var bad = new List<string>(); var lit = new Regex("\"(?:[^\"\\\\\\r\\n]|\\\\.)*\""); var spaced = new Regex("\\d\\s+%|^\"\\s+%");
            foreach (var f in Directory.GetFiles(src, "*.cs"))
            {
                int ln = 0;
                foreach (var line in File.ReadAllLines(f))
                {
                    ln++; var code = line.TrimStart(); if (code.StartsWith("//")) continue;
                    var cut = code.IndexOf("//", StringComparison.Ordinal); if (cut > 0 && code.LastIndexOf('"', cut) < 0) code = code.Substring(0, cut);
                    foreach (Match m in lit.Matches(code)) if (spaced.IsMatch(m.Value)) bad.Add(Path.GetFileName(f) + ":" + ln + " " + m.Value);
                }
            }
            Ok(spaced.IsMatch("\"CPU 45 %\"") && spaced.IsMatch("\" % or GPU\"") && !spaced.IsMatch("\"CPU 45%\"") && !spaced.IsMatch("\"% Idle Time\""), "the check itself catches \"45 %\" and lets \"45%\" through");
            Ok(bad.Count == 0, "no user-visible \"45 %\" in the app's text" + (bad.Count == 0 ? "" : ": " + string.Join(" | ", bad.Take(5))));
        }

        // ---------------- a stand-in that really stops responding
        static void V14HungReal()
        {
            if (!File.Exists(StandInExe)) { Ok(false, "StandIn.exe is built"); return; }
            var t = Path.Combine(Dir, "hung14"); Directory.CreateDirectory(t);
            var hungExe = Path.Combine(t, "TestHungApp.exe"); var fineExe = Path.Combine(t, "TestFineApp.exe");
            File.Copy(StandInExe, hungExe, true); File.Copy(StandInExe, fineExe, true);
            var self = Process.GetCurrentProcess().Id; var log = new List<string>();
            try
            {
                var h = Process.Start(new ProcessStartInfo(hungExe, "hang 120") { UseShellExecute = false });
                var f = Process.Start(new ProcessStartInfo(fineExe, "close 60") { UseShellExecute = false });
                ProcInfo ph = null, pf = null; var until = DateTime.Now.AddSeconds(20); double slowest = 0; List<ProcInfo> procs = null;
                while (DateTime.Now < until)
                {
                    Thread.Sleep(700);
                    var sw = Stopwatch.StartNew(); procs = new WindowsSampler().Take().Procs; slowest = Math.Max(slowest, sw.Elapsed.TotalSeconds);
                    ph = procs.FirstOrDefault(p => p.Pid == h.Id); pf = procs.FirstOrDefault(p => p.Pid == f.Id);
                    if (ph != null && ph.Hung) break;
                }
                Ok(ph != null && ph.Hung && ph.HasWindow, "a stand-in that stops reading its messages shows as Not responding (IsHungAppWindow)");
                Ok(pf != null && pf.HasWindow && !pf.Hung, "a stand-in that keeps answering doesn't");
                Ok(slowest < 5, "a hung window never blocks the sampler (slowest sample " + slowest.ToString("0.00") + " s)");
                if (ph == null || !ph.Hung) return;
                var groups = Grouping.Build(procs, @"C:\Windows", null);
                var hg = groups.First(g => g.Members.Any(m => m.Pid == h.Id));
                Ok(hg.Hung && HungClose.Pick(hg, null, null, null, @"C:\Windows", self) != null && HungClose.Pick(groups.First(g => g.Members.Any(m => m.Pid == f.Id)), null, null, null, @"C:\Windows", self) == null, "Close (not responding) is offered for the hung stand-in only");
                var io = new WindowsGameCloser();
                var run = new GameCloseRun(ph, "Test hung app", io, () => new WindowsSampler().Take().Procs, @"C:\Windows", self, s => log.Add(s), true, null, null) { WaitSeconds = 2 };
                run.Ask(DateTime.Now); var u2 = DateTime.Now.AddSeconds(6);
                while (run.Tick(DateTime.Now) == GameCloseRun.State.Asking && DateTime.Now < u2) Thread.Sleep(200);
                Ok(run.Now == GameCloseRun.State.StillRunning && !h.HasExited, "asked nicely: the hung stand-in can't answer - still running, nothing ended by itself");
                Ok(run.Force() == GameCloseRun.State.Forced && h.WaitForExit(3000), "Force close (a click) ended it");
                Ok(!f.HasExited, "the responding stand-in is untouched");
                Ok(log.LastOrDefault() == "close app TestHungApp.exe: force closed (1 process)", "logged: " + log.LastOrDefault());
            }
            finally { KillUnder(t); }
        }
        static void KillUnder(string folder)
        {
            foreach (var pr in Process.GetProcesses())
                try { using (pr) { var f = pr.MainModule.FileName; if (f.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)) { pr.Kill(); pr.WaitForExit(3000); } } } catch { }
        }

        // ---------------- Game boost with stand-ins: lower, close nicely, Force close, restore, reopen
        static void V14BoostReal()
        {
            if (!File.Exists(StandInExe) || !File.Exists(DummyExe)) { Ok(false, "StandIn.exe and Dummy.exe are built"); return; }
            var t = Path.Combine(Dir, "boost14"); Directory.CreateDirectory(Path.Combine(t, "a")); Directory.CreateDirectory(Path.Combine(t, "b")); Directory.CreateDirectory(Path.Combine(t, "c"));
            var niceExe = Path.Combine(t, @"a\BoostStandIn.exe"); var stubExe = Path.Combine(t, @"b\BoostStubborn.exe"); var dumExe = Path.Combine(t, @"c\BoostDummy.exe");
            File.Copy(StandInExe, niceExe, true); File.Copy(StandInExe, stubExe, true); File.Copy(DummyExe, dumExe, true);
            var self = Process.GetCurrentProcess().Id; var ctl = new WindowsControl(); var io = new WindowsGameCloser();
            try
            {
                var nice = Process.Start(new ProcessStartInfo(niceExe, "close 60") { UseShellExecute = false });
                var stub = Process.Start(new ProcessStartInfo(stubExe, "ignore 60") { UseShellExecute = false });
                var dum = Process.Start(new ProcessStartInfo(dumExe, "60") { UseShellExecute = false, CreateNoWindow = true });
                try { nice.WaitForInputIdle(5000); stub.WaitForInputIdle(5000); } catch { } Thread.Sleep(500);
                var procs = new WindowsSampler().Take().Procs; var groups = Grouping.Build(procs, @"C:\Windows", null);
                Func<int, AppGroup> G3 = n => groups.FirstOrDefault(g => g.Members.Any(m => m.Pid == n));
                var gn = G3(nice.Id); var gs = G3(stub.Id); var gd = G3(dum.Id);
                Ok(gn != null && gs != null && gd != null && new[] { gn, gs, gd }.All(g => Boost.WhyNot(g, null, null, null, @"C:\Windows", self) == null), "boost: the stand-ins can be boosted");
                if (gn == null || gs == null || gd == null) return;
                var cl = ProcCmd.Read(nice.Id);
                Ok(cl != null && cl.Contains("BoostStandIn.exe") && ProcCmd.Args(cl) == "close 60", "its command line is read (to reopen it the same way): " + ProcCmd.Args(cl));
                var eng = new BoostEngine(ctl, new EcoKeeper(ctl), io, new WindowsLauncher(), ProcCmd.Read, null) { WaitSeconds = 3 };
                var before = ctl.GetPriority(dum.Id);   // the suite runs at Below normal, and its children inherit that
                Ok(eng.Lower(gd, Prio.Idle, self) == null && ctl.GetPriority(dum.Id) == Prio.Idle, "Idle: the stand-in's priority really changed (was " + before + ")");
                Ok(eng.Close(gn, true, DateTime.Now, self) == null && eng.Close(gs, false, DateTime.Now, self) == null, "Close: both asked nicely");
                var until = DateTime.Now.AddSeconds(8);
                while (DateTime.Now < until && eng.Closed.Any(c => c.State == "asking")) { Thread.Sleep(250); eng.Tick(DateTime.Now); }
                var cn = eng.Closed.First(c => c.Key == gn.Key); var cs = eng.Closed.First(c => c.Key == gs.Key);
                Ok(cn.State == "closed" && nice.WaitForExit(2000), "the stand-in that answers closed nicely (WM_CLOSE)");
                Ok(cs.State == "still" && !stub.HasExited, "the stubborn one is still running after the wait - nothing ended by itself");
                Ok(eng.Force(cs.Key, null, null, @"C:\Windows", self) == null && cs.State == "forced" && stub.WaitForExit(3000), "Force close (a click) ended it");
                Ok(!dum.HasExited, "the lowered stand-in was never closed");
                var msg = eng.Restore(true);
                Ok(ctl.GetPriority(dum.Id) == before, "restore: the priority is back to what it was (" + ctl.GetPriority(dum.Id) + ")");
                Process again = null; var u2 = DateTime.Now.AddSeconds(8);
                while (again == null && DateTime.Now < u2)
                {
                    Thread.Sleep(300);
                    foreach (var pr in Process.GetProcessesByName("BoostStandIn")) { try { if (pr.Id != nice.Id && string.Equals(pr.MainModule.FileName, niceExe, StringComparison.OrdinalIgnoreCase)) again = pr; } catch { } }
                }
                Ok(again != null, "restore: the ticked stand-in was reopened (" + msg + ")");
                Ok(again != null && ProcCmd.Args(ProcCmd.Read(again.Id)) == "close 60", "reopened the same way it was started (close 60)");
                Ok(!Process.GetProcessesByName("BoostStubborn").Any(pr => { try { return string.Equals(pr.MainModule.FileName, stubExe, StringComparison.OrdinalIgnoreCase); } catch { return false; } }), "the unticked one was not reopened");
            }
            finally { KillUnder(t); }
        }
    }
}
