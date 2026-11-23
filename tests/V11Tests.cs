// Hallow Tasks 1.1 tests: light / dark mode (resolution, the light palette's contrast), History (spikes, stutters, Overlay's live
// numbers, the 24-hour log), the quiet alerts, the process tree, Efficiency mode (fake control, then ONE dummy child the test
// started), Services (made-up services and a fake controller; the real list read-only - nothing is ever started or stopped),
// precise network (the protocol, a made-up event, and the helper started WITHOUT elevation - it must say "denied" or run read-only,
// never prompt), the test copy over its pipe, and the overhead of sampling.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace HallowTasks.Tests
{
    public static partial class Run
    {
        static void V11()
        {
            LookTests(); HistoryTests(); AlertTests(); TreeTests(); EcoTests(); ServiceTests(); NetTests();
        }

        // ---------------- light / dark
        static void LookTests()
        {
            Ok(Look.ModeOf(null) == "dark" && Look.ModeOf("{\"accent\":\"#F07820\"}") == "dark" && Look.ModeOf("not json") == "dark" && Look.ModeOf("{\"mode\":\"pink\"}") == "dark", "mode: missing / broken / unknown = dark");
            Ok(Look.ModeOf("{\"accent\":\"#2E86DE\",\"mode\":\"light\",\"other\":1}") == "light" && Look.ModeOf("{\"mode\":\"System\"}") == "system", "mode: light, system (unknown fields are fine)");
            Func<bool?> winLight = () => true, winDark = () => false, winUnknown = () => null;
            Ok(!Look.Resolve("dark", "hallow", winLight) && Look.Resolve("light", "hallow", winDark) && !Look.Resolve(null, null, winLight), "follow Hallow: dark / light; missing = dark");
            Ok(Look.Resolve("system", "hallow", winLight) && !Look.Resolve("system", "hallow", winDark) && !Look.Resolve("system", "hallow", winUnknown), "system: Windows' AppsUseLightTheme (a fake), unknown = dark");
            Ok(Look.Resolve("dark", "light", winDark) && !Look.Resolve("light", "dark", winLight) && !Look.Resolve("system", "dark", winLight), "the per-app Look wins over theme.json");
            Ok(Look.NeedsWindows("system", "hallow") && !Look.NeedsWindows("system", "light") && !Look.NeedsWindows("dark", "hallow"), "Windows' setting is only polled when it matters");
            Ok(true, "Cyber blue: not a Hallow Tasks theme (Guard only), nothing to ignore");
            var acc = Look.DarkAccent("#F07820");
            Ok(Look.Contrast(acc, "#FFFFFF") >= 4.5 && Look.Contrast(acc, "#EFECF5") >= 4.5 && Look.Contrast(acc, "#FFFFFF") < 5.6, "default orange darkened for text: " + acc + " (" + Look.Contrast(acc, "#FFFFFF").ToString("0.00") + ":1 on white, " + Look.Contrast(acc, "#EFECF5").ToString("0.00") + ":1 on the side bar; the spec says about #B85410)");
            Ok(Look.DarkAccent("#1E1A26") == "#1E1A26", "an already dark accent stays");
            Ok(Math.Abs(Look.Contrast("#000000", "#FFFFFF") - 21) < 0.01 && Look.Over("#80FFFFFF", "#000000") == "#808080", "contrast and see-through maths");
            foreach (var a in new[] { "#F07820", "#2E86DE", "#F5D000", "#00FF66", "#FF2E97", "#FFFFFF", "#7A5CC0" })
            {
                var bad = Look.LightPairs(a).Where(p => Look.Contrast((string)p[1], (string)p[2]) < (double)p[3]).Select(p => p[0] + " " + Look.Contrast((string)p[1], (string)p[2]).ToString("0.00")).ToList();
                Ok(bad.Count == 0, "light palette contrast with accent " + a + ": " + Look.LightPairs(a).Count + " pairs" + (bad.Count > 0 ? " - too low: " + string.Join("; ", bad) : " all >= 4.5:1 (3:1 for graph lines)"));
            }
            Ok(Look.Contrast("#1B7A44", "#FFFFFF") >= 4.5 && Look.Contrast("#8F5B00", "#FFFFFF") >= 4.5 && Look.Contrast("#C2333F", "#FFFFFF") >= 4.5 && Look.Contrast("#1E8E4F", "#FFFFFF") >= 3 && Look.Contrast("#A66A00", "#FFFFFF") >= 3, "the text-safe status colours (green #1B7A44, amber #8F5B00, red #C2333F); icon values at 3:1");
            string xaml; using (var s = typeof(Program).Assembly.GetManifestResourceStream("HallowTasks.Theme.xaml")) using (var r = new StreamReader(s)) xaml = r.ReadToEnd();
            var missing = Look.Light.Keys.Where(k => !xaml.Contains("x:Key=\"" + k + "\" Color=\"")).ToList();
            Ok(missing.Count == 0, "every light key exists in Theme.xaml" + (missing.Count > 0 ? ": missing " + string.Join(", ", missing) : ""));
            var lx = Look.ToLight(xaml);
            Ok(lx.Contains("x:Key=\"Bg\" Color=\"#F6F4FA\"") && lx.Contains("x:Key=\"Text\" Color=\"#1E1A26\"") && lx.Contains("x:Key=\"Green\" Color=\"#1B7A44\"") && !lx.Contains("#F59A50") && !lx.Contains("#14101B"), "ToLight swaps the values (and no dark background is left)");
            var both = Accent.ApplyTo(lx, Look.DarkAccent("#F07820"));
            Ok(!both.Contains("F07820") && both.Contains(acc.Substring(1)), "light + accent: every accent use is the text-safe one");
            // the dark title bar / mode in code: no hard-coded colours outside Theme.xaml and Look.cs
            var src = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(Run).Assembly.Location)), "src");
            if (Directory.Exists(src))
            {
                var hard = Directory.GetFiles(src, "*.cs").Where(f => !f.EndsWith("Look.cs") && !f.EndsWith("Accent.cs") && !f.EndsWith("Icons.g.cs") && !f.EndsWith("Ui.cs"))
                    .SelectMany(f => File.ReadAllLines(f).Select((l, i) => new { f, l, i })).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.l, "\"#[0-9A-Fa-f]{6,8}\"|Colors\\.|Color\\.FromRgb|Brushes\\.")).Select(x => Path.GetFileName(x.f) + ":" + (x.i + 1)).ToList();
                Ok(hard.Count == 0, "no hard-coded colours in the code (only theme keys)" + (hard.Count > 0 ? ": " + string.Join(", ", hard.Take(8)) : ""));
            }
        }

        // ---------------- History
        static Sample S(DateTime at, double cpu, double gpu, params ProcInfo[] procs) { var s = new Sample { At = at, Sys = new SysStats { Cpu = cpu, Gpu3D = gpu, GpuKnown = true, RamTotal = 16L << 30, RamUsed = 8L << 30 } }; s.Procs.AddRange(procs); return s; }
        static void HistoryTests()
        {
            var tn = DateTime.Now.AddMinutes(-30); var t0 = new DateTime(tn.Year, tn.Month, tn.Day, tn.Hour, tn.Minute, 0);   // 1.4: relative to now - the 24-hour log trims older lines (a fixed date failed a day later)
            var h = new HistoryStore();
            var game = P(8000, 1000, "DeltaForceClient-Win64-Shipping.exe", @"D:\G\DeltaForceClient-Win64-Shipping.exe", 30, 7000, 95, true);
            for (int i = 0; i < 120; i++)
            {
                var at = t0.AddSeconds(i);
                double dc = i >= 40 && i < 50 ? 35 : 2, cc = i >= 90 && i < 95 ? 18 : 1;
                var procs = new[] { P(3000, 1000, "Discord.exe", @"C:\D\Discord.exe", dc, 200, 1, true), P(2000, 1000, "chrome.exe", @"C:\C\chrome.exe", cc, 300, 0, true), game, P(5000, 1000, "obs64.exe", @"C:\O\obs64.exe", 1, 100, 0, false, 1, 0, i >= 60 && i < 63 ? 40 * 1048576 : 0) };
                var s = S(at, 20 + dc, 90, procs);
                h.Add(s, Grouping.Build(s.Procs, @"C:\Windows", null), i >= 20 ? game : null, i >= 20 ? "Delta Force" : null);
            }
            Ok(h.Count == 120, "History keeps every point (" + h.Count + ")");
            var sp = HistoryStore.FindSpikes(h.Points(DateTime.MinValue));
            Ok(sp.Count == 3 && sp[0].Title == "Discord" && Math.Abs(sp[0].Cpu - 35) < 0.01 && sp[0].From == t0.AddSeconds(40) && sp[0].To == t0.AddSeconds(49) && sp[0].InGame, "spikes: Discord 35% CPU for 10 s while you played, first: " + string.Join(", ", sp.Select(x => x.Title + " " + x.What)));
            Ok(sp.Any(x => x.Title == "Google Chrome" && x.Cpu >= 18) && sp.Any(x => x.Disk >= 40 * 1048576 - 1), "spikes: Chrome CPU, OBS disk");
            Ok(!sp.Any(x => x.Title == "Delta Force"), "the game is never a spike (it's meant to be busy)");
            // gaps split a spike; the 1-hour window drops old points
            var h2 = new HistoryStore(); var t1 = t0;
            foreach (var sec in new[] { 0, 1, 2, 40, 41 }) { var s = S(t1.AddSeconds(sec), 30, 0, P(3000, 1000, "Discord.exe", @"C:\D\Discord.exe", 30, 200)); h2.Add(s, Grouping.Build(s.Procs, @"C:\Windows", null), null, null); }
            Ok(HistoryStore.FindSpikes(h2.Points(DateTime.MinValue)).Count == 2, "a 38 s pause splits a spike in two");
            var s2 = S(t1.AddHours(1).AddSeconds(60), 1, 0); h2.Add(s2, new List<AppGroup>(), null, null);
            Ok(h2.Count == 1, "points older than an hour are dropped");
            // stutters: peak (no Overlay) - Discord's jump while the game runs was found with its cause
            var st = h.Stutters(DateTime.MinValue);
            Ok(st.Count >= 1 && st[0].Source == "peak" && st[0].What.StartsWith("Discord jumped to 35") && st[0].At == t0.AddSeconds(40), "stutter from a CPU peak: " + (st.Count > 0 ? st[0].What + " / " + st[0].Cause : "none"));
            // Overlay's live numbers: a sudden drop of the 1% low
            var h3 = new HistoryStore(); Stutter got = null;
            for (int i = 0; i < 12; i++) { var at = t0.AddSeconds(2 * i); var s = S(at, 40, 95, P(3000, 1000, "Discord.exe", @"C:\D\Discord.exe", i == 10 ? 30 : 2, 200), game); h3.Add(s, Grouping.Build(s.Procs, @"C:\Windows", null), game, "Delta Force"); var r = h3.OverlayLive(at, 360, i == 10 ? 70 : 230, -1); if (r != null) got = r; }
            Ok(got != null && got.Source == "overlay" && got.What.StartsWith("1% low fell to 70 fps (usually 230)") && got.Cause.StartsWith("Discord"), "stutter from Overlay: " + (got == null ? "none" : got.What + " / " + got.Cause));
            Ok(h3.Stutters(DateTime.MinValue).Count(x => x.Source == "peak") == 0, "with Overlay's numbers, CPU peaks aren't counted twice");
            var h4 = new HistoryStore(); Ok(h4.OverlayLive(t0, 300, 200, 5) == null && h4.OverlayLive(t0.AddSeconds(6), 300, 200, 7) != null && h4.OverlayLive(t0.AddSeconds(8), 300, 200, 7) == null, "a frameSpikes counter from Overlay (if it offers one): each increase is a stutter");
            var now = DateTime.Now; var nowS = now.ToString("s");
            var o = HistoryStore.ParseOverlay("{\"app\":\"Overlay\",\"live\":{\"game\":\"Delta Force\",\"fps\":379,\"fpsLow\":201.5,\"ping\":30,\"updated\":\"" + nowS + "\"}}", now);
            Ok(o != null && o[0] == 379 && Math.Abs(o[1] - 201.5) < 0.01 && o[2] == -1, "reads Overlay's status \"live\" (fps, fpsLow)");
            Ok(HistoryStore.ParseOverlay("{\"live\":{\"fps\":379,\"fpsLow\":201,\"updated\":\"" + now.AddMinutes(-2).ToString("s") + "\"}}", now) == null && HistoryStore.ParseOverlay("{\"app\":\"Overlay\",\"visible\":true}", now) == null && HistoryStore.ParseOverlay("garbage", now) == null, "stale / missing / broken live numbers are ignored");
            // the 24-hour log
            var lp = Path.Combine(Dir, "hist", "history-24h.jsonl"); Directory.CreateDirectory(Path.GetDirectoryName(lp));
            var log = new HistoryLog(lp);
            foreach (var p in h.Points(DateTime.MinValue)) log.Push(p);
            log.Flush();
            var lines = File.ReadAllLines(lp);
            Ok(lines.Length == 2, "the log: one line a minute (" + lines.Length + " lines for 2 minutes)");
            var back = HistoryLog.Parse(lines[0]);
            Ok(back != null && back.At == t0 && back.Apps.Any(a => a.Title == "Discord" && Math.Abs(a.Cpu - 35) < 0.01) && back.Game == "Delta Force" && back.Apps.Length <= 8, "a log line reads back: the minute's peaks and busiest apps");
            Ok(HistoryStore.FindSpikes(log.Read(DateTime.MinValue), 60, 130).Any(x => x.Title == "Discord"), "spikes from the log too");
            var old = HistoryLog.Line(DateTime.Now.AddHours(-30), h.Points(DateTime.MinValue).Take(3).ToList());
            var fresh = Enumerable.Range(0, 300).Select(i => HistoryLog.Line(DateTime.Now.AddMinutes(-300 + i), h.Points(DateTime.MinValue).Skip(40).Take(5).ToList())).ToList();
            File.WriteAllText(lp, old + "\n" + string.Join("\n", fresh) + "\n");
            var cap = new HistoryLog(lp) { Cap = 40000 }; cap.Trim();
            var after = File.ReadAllLines(lp);
            Ok(!after.Contains(old) && new FileInfo(lp).Length <= 40000 && after.Length > 10 && after.Last() == fresh.Last(), "trim: older than 24 h out, capped size (" + new FileInfo(lp).Length + " bytes, " + after.Length + " lines, newest kept)");
            Ok(fresh[0].Length < 700, "a log line is small (" + fresh[0].Length + " bytes; 1440 a day ~ " + (fresh[0].Length * 1440 / 1024) + " KB)");
        }

        // ---------------- alerts while you play
        static void AlertTests()
        {
            var w = new AlertWatch(); var t0 = new DateTime(2026, 11, 21, 20, 0, 0);
            var game = P(8000, 1000, "DeltaForceClient-Win64-Shipping.exe", @"D:\G\DeltaForceClient-Win64-Shipping.exe", 30, 7000, 95, true);
            Func<double, List<AppGroup>> g = cpu => Grouping.Build(new List<ProcInfo> { game, P(3000, 1000, "Discord.exe", @"C:\D\Discord.exe", cpu, 200), P(8001, 8000, "SGuard64.exe", @"C:\S\SGuard64.exe", 1, 40) }, @"C:\Windows", null);
            Ok(w.Check(g(50), null, t0, 25, 25, 20, 1).Count == 0, "no game: no alerts");
            var all = new List<string[]>();
            for (int i = 0; i <= 25; i += 5) all.AddRange(w.Check(g(40), game, t0.AddSeconds(i), 25, 25, 20, 1));
            Ok(all.Count == 1 && all[0][0] == "Discord is busy while you play" && all[0][1].StartsWith("40% CPU for over 20 s"), "Discord over 25% CPU for 20 s: one quiet note (" + (all.Count > 0 ? all[0][1] : "none") + ")");
            Ok(w.Check(g(40), game, t0.AddSeconds(60), 25, 25, 20, 1).Count == 0 && w.Check(g(40), game, t0.AddMinutes(11), 25, 25, 20, 1).Count == 1, "not again for 10 minutes");
            var w2 = new AlertWatch(); int n = 0;
            for (int i = 0; i < 30; i += 5) n += w2.Check(g(i % 10 == 0 ? 40 : 5), game, t0.AddSeconds(i), 25, 25, 20, 1).Count;
            Ok(n == 0, "short bursts under N seconds: nothing");
            Ok(new AlertWatch().Check(g(1), game, t0, 25, 25, 0, 1).Count == 0, "the game itself never alerts");
        }

        // ---------------- the process tree
        static void TreeTests()
        {
            var all = Sample1();
            var rows = Grouping.Forest(all, null, null, p => p.Cpu);
            Func<int, Grouping.TreeRow> R = pid => rows.FirstOrDefault(r => r.Proc.Pid == pid);
            Ok(R(0) == null && R(4) != null && R(4).Depth == 0, "tree: System Idle Process is left out, System is a root");
            Ok(R(2000).Depth == R(1000).Depth + 1 && R(2001).Depth == R(2000).Depth + 1 && R(1000).HasChildren, "tree: Explorer > Chrome > Chrome's helpers");
            Ok(R(7001) != null && R(7001).Depth == 0, "tree: a reused PID's 'child' is a root of its own");
            Ok(rows.FindIndex(r => r.Proc.Pid == 2001) > rows.FindIndex(r => r.Proc.Pid == 2000) && rows.Count == all.Count - 1, "tree: children right after their parent; every process once");
            var col = Grouping.Forest(all, new HashSet<string> { all.First(p => p.Pid == 2000).Key }, null, p => p.Cpu);
            Ok(!col.Any(r => r.Proc.Pid == 2001) && col.Any(r => r.Proc.Pid == 2000), "tree: a collapsed process hides its children");
            var f = Grouping.Forest(all, null, p => p.Name == "ffmpeg.exe", p => p.Cpu);
            Ok(f.Select(r => r.Proc.Pid).SequenceEqual(new[] { 1000, 4001, 4002 }), "tree search: the match and the processes above it (" + string.Join(" > ", f.Select(r => r.Proc.Name)) + ")");
        }

        // ---------------- Efficiency mode
        static void EcoTests()
        {
            var fc = new FakeControl(); var k = new EcoKeeper(fc); var d = P(3000, 1000, "Discord.exe", @"C:\D\Discord.exe");
            fc.Prios[3000] = Prio.AboveNormal;
            Ok(k.Set(d, true) == null && fc.Eco.Contains(3000) && fc.Prios[3000] == Prio.Idle && k.IsOn(d), "efficiency on: EcoQoS + Idle (fake control)");
            Ok(k.Set(d, false) == null && !fc.Eco.Contains(3000) && fc.Prios[3000] == Prio.AboveNormal && !k.IsOn(d), "efficiency off: its own priority back");
            fc.Gone.Add(3000); Ok(k.Set(d, true) != null, "a closed process: an error, no crash");
            Ok(Safety.WhyNot(P(8000, 1, "DeltaForceClient-Win64-Shipping.exe", null), null, null, @"C:\Windows", 1) != null && Safety.WhyNot(P(8001, 1, "SGuard64.exe", null), null, null, @"C:\Windows", 1) != null && Safety.WhyNot(P(700, 1, "svchost.exe", @"C:\Windows\System32\svchost.exe", session: 0), null, null, @"C:\Windows", 1) != null, "efficiency is refused for the game, anti-cheat and Windows (the same rules as End task)");
            // a REAL dummy child the test started itself
            if (!File.Exists(DummyExe)) { Ok(false, "Dummy.exe is built"); return; }
            var ctl = new WindowsControl(); var dm = StartDummy("60"); Thread.Sleep(300);
            try
            {
                var e = ctl.SetEfficiency(dm.Id, true); var on = ctl.GetEfficiency(dm.Id);
                Ok(e == null && on == true && ctl.GetPriority(dm.Id) == Prio.Idle, "dummy: Efficiency mode on - EcoQoS reads back on, priority Idle (no admin)");
                Ok(ctl.SetEfficiency(dm.Id, false) == null && ctl.GetEfficiency(dm.Id) == false, "dummy: Efficiency mode off");
            }
            finally { try { if (!dm.HasExited) dm.Kill(); } catch { } }
        }

        // ---------------- Services (made-up services and a fake controller; the real list read-only)
        static void ServiceTests()
        {
            Ok(ServiceRules.ValidName("Spooler") && ServiceRules.ValidName("Steam Client Service") && ServiceRules.ValidName("NVDisplay.ContainerLocalSystem") && ServiceRules.ValidName("OneSyncSvc_4a1b2") && ServiceRules.ValidName("MSSQL$SQLEXPRESS"), "service names");
            Ok(!ServiceRules.ValidName("") && !ServiceRules.ValidName("a\" & calc") && !ServiceRules.ValidName(@"..\x") && !ServiceRules.ValidName("a/b") && !ServiceRules.ValidName(new string('a', 300)), "odd names are refused (never put on a command line)");
            Ok(ServiceRules.HelperArgs("stop", "Steam Client Service") == "--svc stop \"Steam Client Service\"", "the elevated helper's arguments");
            bool threw = false; try { ServiceRules.HelperArgs("delete", "Spooler"); } catch (ArgumentException) { threw = true; } Ok(threw, "only start / stop / restart");
            Ok(WindowsServices.RunHelper(new[] { "--svc", "stop", "bad\"name" }, 1) == 2 && WindowsServices.RunHelper(new[] { "--svc", "format", "Spooler" }, 1) == 2 && WindowsServices.RunHelper(new[] { "--svc" }, 1) == 2, "the helper refuses bad requests before touching anything");
            Ok(ServiceRules.IsMicrosoft(@"C:\Windows\system32\svchost.exe -k netsvcs -p", null) && ServiceRules.IsMicrosoft(@"%SystemRoot%\System32\spoolsv.exe", null) && ServiceRules.IsMicrosoft("\"C:\\Program Files\\Common Files\\microsoft shared\\ClickToRun\\OfficeClickToRun.exe\"", null) && !ServiceRules.IsMicrosoft("\"C:\\Program Files (x86)\\Common Files\\Steam\\steamservice.exe\" /RunAsService", null), "Microsoft's services are recognised (leave alone), Steam's isn't");
            Ok(ServiceRules.StartWord(2, true, false) == "Automatic (delayed)" && ServiceRules.StartWord(3, false, true) == "Manual (trigger)" && ServiceRules.StartWord(4, false, false) == "Disabled", "startup types");
            var f = new FakeServices();
            Ok(f.Stop("Steam Client Service") == null && f.Start("Steam Client Service") == null && f.List().First(s => s.Name == "Steam Client Service").Running, "fake controller: start / stop");
            Ok(f.Stop("NVDisplay.ContainerLocalSystem") == "needs admin", "fake controller: needs admin");
            var sw = Stopwatch.StartNew(); var real = new WindowsServices().List(); var ms = sw.ElapsedMilliseconds;
            Ok(real.Count > 50 && real.Any(s => s.Running && s.Pid > 0) && real.Any(s => s.Microsoft) && real.All(s => s.Name != null), "real services, read-only: " + real.Count + " (" + real.Count(s => s.Running) + " running, " + real.Count(s => !s.Microsoft) + " not Microsoft, " + real.Count(s => s.CanControl) + " you may control) in " + ms + " ms");
            Ok(real.Count(s => s.Description != null) > real.Count / 2 && real.Any(s => s.StartType.StartsWith("Automatic")), "real services have descriptions and startup types");
        }

        // ---------------- precise network
        static void NetTests()
        {
            var counts = new Dictionary<int, long[]> { { 1234, new long[] { 500, 20000 } }, { 99, new long[] { 0, 0 } }, { 7, new long[] { 1, 2 } } };
            var line = NetHelper.Line(500, counts);
            Ok(line == "net 500 1234:500:20000 7:1:2", "helper line: " + line);
            var d = NetHelper.Parse(line);
            Ok(d != null && d.Count == 2 && d[1234][0] == 1000 && d[1234][1] == 40000, "parsed to bytes per second");
            Ok(NetHelper.Parse("hello") == null && NetHelper.Parse("net x 1:2:3") == null && NetHelper.Parse("net 1000 1:-5:3").Count == 0, "bad lines are ignored");
            Ok(NetHelper.ValidPipe(NetPrecise.NewPipeName()) && !NetHelper.ValidPipe("HallowTasksNet.x") && !NetHelper.ValidPipe(@"..\pipe\other") && !NetHelper.ValidPipe(null), "pipe names");
            // a made-up Kernel-Network event (EVENT_RECORD: id at 40, user data length at 86, user data at 96; data = pid, size)
            var rec = Marshal.AllocHGlobal(128); var data = Marshal.AllocHGlobal(16);
            try
            {
                for (int i = 0; i < 128; i++) Marshal.WriteByte(rec, i, 0);
                Marshal.WriteInt32(data, 0, 4321); Marshal.WriteInt32(data, 4, 1500); Marshal.WriteInt16(rec, 86, 16); Marshal.WriteIntPtr(rec, 96, data);
                var c = new Dictionary<int, long[]>();
                Marshal.WriteInt16(rec, 40, 10); NetHelper.Count(rec, c); Marshal.WriteInt16(rec, 40, 11); NetHelper.Count(rec, c); NetHelper.Count(rec, c); Marshal.WriteInt16(rec, 40, 58); NetHelper.Count(rec, c);
                Ok(c[4321][0] == 3000 && c[4321][1] == 3000, "events: TCP send + UDP send counted as sent, TCP receive as received");
            }
            finally { Marshal.FreeHGlobal(rec); Marshal.FreeHGlobal(data); }
            var np = new NetPrecise(); var procs = new List<ProcInfo> { P(1234, 1, "a.exe", null), P(5, 1, "b.exe", null) };
            np.Apply(procs); Ok(procs.All(p => p.NetSend < 0), "precise network off: nothing claimed");
            np.FakeOn(); np.Feed("net 1000 1234:100:900"); np.Apply(procs);
            Ok(procs[0].NetSend == 100 && procs[0].NetRecv == 900 && procs[1].NetSend == 0, "precise network on: bytes/s per process, 0 for the quiet ones");
            var grp = Grouping.Build(procs, @"C:\Windows", null); Ok(grp.First(x => x.Members.Any(m => m.Pid == 1234)).NetBytes == 1000, "group sum");
            // the helper itself, started WITHOUT elevation (no UAC): it must refuse a bad pipe, and with a good one answer "denied"
            // (or, if this user may trace, run read-only and stop as soon as the pipe closes)
            var exe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(Run).Assembly.Location)), "HallowTasks.exe");
            Func<string, Process> helper = a => { var psi = new ProcessStartInfo(exe, a) { UseShellExecute = false, CreateNoWindow = true }; psi.EnvironmentVariables["HALLOW_TASKS_TESTDIR"] = Path.Combine(Dir, "net"); return Process.Start(psi); };
            using (var bad = helper("--netetw ../evil " + Process.GetCurrentProcess().Id)) Ok(bad.WaitForExit(10000) && bad.ExitCode == 2, "helper: a bad pipe name is refused");
            var name = NetPrecise.NewPipeName(); var server = NetPrecise.MakeServer(name);
            var hp = helper("--netetw " + name + " " + Process.GetCurrentProcess().Id);
            var np2 = new NetPrecise(); var err = np2.Attach(server, 15000);
            Ok(err == null ? np2.On : err.Contains("needs admin"), "helper without admin: " + (err ?? "running read-only (this user may trace)"));
            np2.Stop();
            Ok(hp.WaitForExit(10000), "helper stops when the pipe closes");
            try { if (!hp.HasExited) hp.Kill(); } catch { }
        }

        // ---------------- the 1.1 commands of the test copy (made-up processes, fake control and services)
        static void PipeV11(string exe, string dir)
        {
            Ok(Cmd(exe, dir, "test-eco discord") == "ok 4", "Efficiency mode on Discord (4 processes, fake control)");
            Ok(Cmd(exe, dir, "test-eco exe:sguard64").StartsWith("error That's anti-cheat") && Cmd(exe, dir, "test-eco exe:deltaforceclient-win64-shipping").StartsWith("error That's your game") && Cmd(exe, dir, "test-eco svchost").StartsWith("error"), "Efficiency mode refused: anti-cheat, the game, Windows");
            Ok(Cmd(exe, dir, "test-eco discord off") == "ok 0", "Efficiency mode off");
            var r = Cmd(exe, dir, "test-history"); Ok(r.StartsWith("ok {\"points\":") && !r.StartsWith("ok {\"points\":0"), "History in the test copy: " + r);
            Ok(Cmd(exe, dir, "test-alerts 9 90 1") == "ok", "alerts on (9% CPU, 1 s)");
            Cmd(exe, dir, "test-sample"); Thread.Sleep(1300); Cmd(exe, dir, "test-sample");
            r = Cmd(exe, dir, "test-notes"); Ok(r.Contains("Google Chrome is busy while you play") && !r.Contains("Delta Force is busy"), "a quiet note for Chrome (never the game): " + r);
            r = Cmd(exe, dir, "status"); Ok(r.Contains("\"alerts\":true") && r.Contains("\"history\":") && r.Contains("\"preciseNetwork\":false") && r.Contains("\"look\":"), "status has the 1.1 fields");
            r = Cmd(exe, dir, "test-services"); Ok(r.Contains("Spooler|Running|ms|admin") && r.Contains("Steam Client Service|Stopped|3p|ctl"), "made-up services: " + r.Length + " chars");
            Ok(Cmd(exe, dir, "test-svc start Steam Client Service").StartsWith("ok [\"start Steam Client Service\"]"), "start a service (fake controller)");
            Ok(Cmd(exe, dir, "test-svc stop Spooler").StartsWith("error It's one of Windows' own services"), "Microsoft's service: leave alone");
            Ok(Cmd(exe, dir, "test-svc stop AntiCheatExpert Service").StartsWith("error That's anti-cheat"), "anti-cheat's service: leave alone");
            Ok(Cmd(exe, dir, "test-svc stop NVDisplay.ContainerLocalSystem").StartsWith("error needs admin (the test copy never asks"), "needs admin: the test copy never asks Windows (no UAC in tests)");
            Ok(Cmd(exe, dir, "test-net net 1000 4300:1000:3000 4310:500:500") == "ok 5000", "precise network numbers reach Chrome's group (made up)");
            Ok(Cmd(exe, dir, "test-setlook light") == "ok light" && Cmd(exe, dir, "test-setlook dark") == "ok dark", "Look: Light / Dark here (live, no restart)");
            Directory.CreateDirectory(Path.Combine(dir, "Hallow")); File.WriteAllText(Path.Combine(dir, "Hallow", "theme.json"), "{\"accent\":\"#2E86DE\",\"mode\":\"light\"}");
            Ok(Cmd(exe, dir, "test-setlook hallow") == "ok light", "Follow Hallow: theme.json (the test copy's) says light");
            File.WriteAllText(Path.Combine(dir, "Hallow", "theme.json"), "{\"accent\":\"#2E86DE\"}");
            Ok(Cmd(exe, dir, "test-look") == "ok dark", "mode missing again: dark");
            Ok(Cmd(exe, dir, "test-historylog on") == "ok True" && File.Exists(Path.Combine(dir, "history-24h.jsonl")) && Cmd(exe, dir, "test-historylog off") == "ok False", "the 24-hour log (in the test folder)");
        }

        // ---------------- overhead: the real sampler in a real test copy (read-only), in the tray (5 s) and with the window "open" (1 s)
        static void OverheadCopy()
        {
            var exe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(Run).Assembly.Location)), "HallowTasks.exe");
            var dir = Path.Combine(Dir, "overhead"); Directory.CreateDirectory(dir);
            var psi = new ProcessStartInfo(exe, "--tray") { UseShellExecute = false, CreateNoWindow = true };
            psi.EnvironmentVariables["HALLOW_TASKS_TESTDIR"] = dir; psi.EnvironmentVariables["HALLOW_TASKS_HEADLESS"] = "1";
            var app = Process.Start(psi);
            try
            {
                string r = null; for (int i = 0; i < 40 && (r == null || !r.StartsWith("ok")); i++) { Thread.Sleep(250); r = Cmd(exe, dir, "ping"); }
                Thread.Sleep(12000);   // the first samples (counters, file info) are the expensive ones
                Func<int, double> measure = secs => { app.Refresh(); var c0 = app.TotalProcessorTime; var sw = Stopwatch.StartNew(); Thread.Sleep(secs * 1000); app.Refresh(); return 100.0 * (app.TotalProcessorTime - c0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds / Environment.ProcessorCount; };
                var tray = measure(60);
                Ok(tray < 1, "overhead in the tray with History (a sample every 5 s): " + tray.ToString("0.000") + "% of the CPU");
                Cmd(exe, dir, "test-active on"); Thread.Sleep(3000);
                var open = measure(30);
                Ok(open < 1, "overhead with the window open (a sample every second): " + open.ToString("0.000") + "% of the CPU");
                Cmd(exe, dir, "test-active off");
                r = Cmd(exe, dir, "status"); Ok(r.Contains("\"history\":") && !r.Contains("\"history\":0"), "the tray copy kept History");
                Cmd(exe, dir, "quit"); app.WaitForExit(8000);
            }
            finally { try { if (!app.HasExited) app.Kill(); } catch { } }
        }
    }
}
