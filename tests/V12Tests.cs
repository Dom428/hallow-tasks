// Hallow Tasks 1.2 tests: the busy-app count (made-up groups), the pipe's "quick" (a cached sample never more than 5 s old,
// answered at once) on a made-up test copy, status.json kept fresh while someone asks, no History points from samples taken
// only for Remote / Hub while History in the tray is off, and "quick" on a test copy with the REAL sampler (read-only).
// Every test copy has its own test folder and test pipe - never the real app's data or pipe.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowTasks.Tests
{
    public static partial class Run
    {
        static void V12()
        {
            Func<string, double, double, int, AppGroup> G = (key, cpu, gpu, pid) => { var g = new AppGroup { Key = key, Title = key, Cpu = cpu, Gpu = gpu }; g.Members.Add(new ProcInfo { Pid = pid, Name = key + ".exe" }); return g; };
            var gs = new List<AppGroup> { G("idle", 90, 0, 0), G("chrome", 8, 1, 10), G("discord", 1, 7, 11), G("clips", 5, 0, 12), G("hub", 0.3, 0, 13), G("edge", 4.9, 4.9, 14) };
            Ok(Controller.BusyCount(gs) == 3, "busy apps: at least 5% CPU or GPU (Chrome, Discord, Clips), never the idle process: " + Controller.BusyCount(gs));
            Ok(Controller.BusyCount(new List<AppGroup>()) == 0, "busy apps: none");
            var odd = new AppGroup { Key = "System Idle Process", Cpu = 95 }; odd.Members.Add(new ProcInfo { Pid = 0 });
            Ok(Controller.BusyCount(new[] { odd }) == 0, "busy apps: pid 0 under another key doesn't count either");
        }

        // one line to a test copy's pipe, straight (no --cmd process), so the timing is the app's own
        static string Pipe(string dir, string line, int ms = 6000)
        {
            var name = "HallowFamily.Tasks." + Environment.UserName + ".test-" + Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
            try
            {
                using (var c = new NamedPipeClientStream(".", name, PipeDirection.InOut))
                {
                    c.Connect(2000); var b = Encoding.UTF8.GetBytes(line + "\n"); c.Write(b, 0, b.Length); c.Flush();
                    var r = new StreamReader(c, Encoding.UTF8); string s = null; var t = new Thread(() => { try { s = r.ReadLine(); } catch { } }); t.Start();
                    return t.Join(ms) ? s : null;
                }
            }
            catch { return null; }
        }
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static Dictionary<string, object> Parse(string okReply) { try { return okReply != null && okReply.StartsWith("ok {") ? Js.Deserialize<Dictionary<string, object>>(okReply.Substring(3)) : null; } catch { return null; } }
        // seconds since a "yyyy-MM-ddTHH:mm:ss" time (whole seconds, so up to 1 s more than the real age)
        static double Age(Dictionary<string, object> d, string key)
        {
            object v; DateTime t;
            if (d == null || !d.TryGetValue(key, out v) || !DateTime.TryParseExact(v as string ?? "", "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return 999;
            return (DateTime.Now - t).TotalSeconds;
        }
        static Process StartCopy(string exe, string dir, bool fake)
        {
            var psi = new ProcessStartInfo(exe, "--tray") { UseShellExecute = false, CreateNoWindow = true };
            psi.EnvironmentVariables["HALLOW_TASKS_TESTDIR"] = dir; psi.EnvironmentVariables["HALLOW_TASKS_HEADLESS"] = "1";
            if (fake) psi.EnvironmentVariables["HALLOW_TASKS_FAKE"] = "1";
            var app = Process.Start(psi);
            for (int i = 0; i < 40; i++) { Thread.Sleep(250); var r = Pipe(dir, "ping", 2000); if (r != null && r.StartsWith("ok")) break; }
            return app;
        }

        static void V12Pipe()
        {
            var exe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(Run).Assembly.Location)), "HallowTasks.exe");
            if (!File.Exists(exe)) { Ok(false, "HallowTasks.exe is built"); return; }
            // 1) made-up processes, History in the tray on (the default)
            var dir = Path.Combine(Dir, "quick"); Directory.CreateDirectory(dir);
            var app = StartCopy(exe, dir, true);
            try
            {
                Ok(Pipe(dir, "ping") == "ok {\"app\":\"Tasks\",\"version\":\"" + Program.Version + "\"}", "1.2 copy answers ping");
                var sw = Stopwatch.StartNew(); var r = Pipe(dir, "quick"); var first = sw.ElapsedMilliseconds; var d = Parse(r);
                Ok(d != null && (string)d["app"] == "Tasks" && d.ContainsKey("cpu") && d.ContainsKey("topCpu") && d.ContainsKey("busyApps") && d.ContainsKey("busyPercent"), "quick: the status fields plus busyApps / busyPercent (" + first + " ms)");
                Ok(d != null && d["busyApps"] is int && (int)d["busyApps"] >= 1 && Convert.ToDouble(d["busyPercent"]) == 5, "quick: busyApps is a count (" + (d == null ? "?" : d["busyApps"]) + "), busyPercent 5");
                Ok(r != null && !r.Contains(":\\\\") && !r.Contains("\"busyNames\""), "quick: names and numbers only - no paths");
                Ok(Age(d, "sampled") <= QuickLimit, "quick: the sample is at most 5 s old (" + Age(d, "sampled").ToString("0.0") + " s by the clock)");
                // the cached answer is immediate; over 14 s every answer stays fresh
                long slowest = 0; double oldest = 0; int answers = 0;
                for (int i = 0; i < 8; i++)
                {
                    Thread.Sleep(1700);
                    sw.Restart(); var q = Parse(Pipe(dir, "quick")); slowest = Math.Max(slowest, sw.ElapsedMilliseconds);
                    if (q != null) { answers++; oldest = Math.Max(oldest, Age(q, "sampled")); }
                }
                Ok(answers == 8, "quick: answered 8 of 8");
                Ok(oldest <= QuickLimit, "quick: never older than 5 s over 14 s (oldest " + oldest.ToString("0.0") + " s by the clock)");
                Ok(slowest < 400, "quick: answered at once from the cache (slowest " + slowest + " ms)");
                // status uses the same fresh sample (no 1 s wait) and still has the old fields
                sw.Restart(); var st = Parse(Pipe(dir, "status")); var stMs = sw.ElapsedMilliseconds;
                Ok(st != null && st.ContainsKey("topGpu") && st.ContainsKey("processes") && st.ContainsKey("sampling") && st.ContainsKey("busyApps"), "status: every old field and busyApps");
                Ok(stMs < 700, "status: from the fresh cached sample (" + stMs + " ms; 1.1 took about 1 s)");
                // status.json: rewritten with the samples while someone asks (about every 5 s, not every 60 s)
                var f = Path.Combine(dir, "status.json"); Dictionary<string, object> js = null; double worst = 0;
                for (int i = 0; i < 3; i++) { Thread.Sleep(4000); try { js = Js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f)); } catch { js = null; } worst = Math.Max(worst, Age(js, "updated")); }
                Ok(js != null && js.ContainsKey("busyApps") && js.ContainsKey("topCpu"), "status.json: has busyApps and the top users");
                Ok(worst <= 7, "status.json: rewritten every ~5 s while asked (oldest seen " + worst.ToString("0.0") + " s)");
                Ok(Pipe(dir, "quit") == "ok" && app.WaitForExit(8000), "quick copy quits");
            }
            finally { try { if (!app.HasExited) app.Kill(); } catch { } }

            // 2) History in the tray OFF: "quick" starts a 2-minute lease of 5 s samples, and none of them becomes History
            var dir2 = Path.Combine(Dir, "quick-nohist"); Directory.CreateDirectory(dir2);
            File.WriteAllText(Path.Combine(dir2, "settings.json"), "{\"HistoryInTray\":false}");
            var app2 = StartCopy(exe, dir2, true);
            try
            {
                Thread.Sleep(6000);
                var a = Parse(Pipe(dir2, "status"));
                Ok(a != null && a.ContainsKey("historyInTray") && (bool)a["historyInTray"] == false && Convert.ToInt32(a["history"]) == 0, "History in the tray off: no samples and no History while nobody asks");
                var q1 = Parse(Pipe(dir2, "quick"));
                Ok(q1 != null && Age(q1, "sampled") <= QuickLimit, "History off: quick still answers fresh (" + Age(q1, "sampled").ToString("0.0") + " s)");
                Thread.Sleep(12000);
                var sw = Stopwatch.StartNew(); var q2 = Parse(Pipe(dir2, "quick")); var ms = sw.ElapsedMilliseconds;
                Ok(q2 != null && Age(q2, "sampled") <= QuickLimit && ms < 400, "History off: the lease keeps sampling, so quick answers at once (" + ms + " ms, " + Age(q2, "sampled").ToString("0.0") + " s old)");
                Ok(q2 != null && Convert.ToInt32(q2["history"]) == 0, "History off: samples taken for Remote / Hub aren't kept as History (" + (q2 == null ? "?" : q2["history"]) + ")");
                Ok(Pipe(dir2, "quit") == "ok" && app2.WaitForExit(8000), "no-History copy quits");
            }
            finally { try { if (!app2.HasExited) app2.Kill(); } catch { } }

            // 3) the REAL sampler (read-only, no windows)
            var dir3 = Path.Combine(Dir, "quick-real"); Directory.CreateDirectory(dir3);
            var app3 = StartCopy(exe, dir3, false);
            try
            {
                Thread.Sleep(6500);   // 1.2.1: the tray's first 5 s sample (no CPU numbers yet - the first one never has) must not be the answer
                var sw = Stopwatch.StartNew(); var q = Parse(Pipe(dir3, "quick")); var ms1 = sw.ElapsedMilliseconds;
                Ok(q != null && q["busyApps"] is int && Convert.ToInt32(q["processes"]) > 10, "real copy: quick with " + (q == null ? "?" : q["processes"]) + " processes, " + (q == null ? "?" : q["busyApps"]) + " busy apps (" + ms1 + " ms)");
                Ok(q != null && Convert.ToDouble(q["cpu"]) > 0 && Age(q, "sampled") <= QuickLimit, "real copy: right after start the answer has real CPU numbers, not the first sample's zeros (cpu " + (q == null ? "?" : q["cpu"]) + ")");
                Thread.Sleep(2000);
                sw.Restart(); var q2 = Parse(Pipe(dir3, "quick")); var ms2 = sw.ElapsedMilliseconds;
                Ok(q2 != null && Age(q2, "sampled") <= QuickLimit && ms2 < 400, "real copy: the second quick comes from the cache (" + ms2 + " ms)");
                Ok(Pipe(dir3, "quit") == "ok" && app3.WaitForExit(8000), "real copy quits");
            }
            finally { try { if (!app3.HasExited) app3.Kill(); } catch { } }
        }
        const double QuickLimit = 6;   // 5 s + the 1 s the whole-second "sampled" time can add
    }
}
