// Hallow Tasks - History: "what spiked my PC". A rolling hour of per-app CPU / GPU / RAM / disk numbers in memory (a point per
// sample: once a second while the window is open, every 5 s while it's in the tray - Settings can turn the tray part off), an
// optional 24-hour log on disk (one line a minute, capped at about 1 MB, off by default), the spikes found in them, and while a game
// runs the stutter moments: from Hallow Overlay's live numbers when it runs (its status "live": a sudden drop of the 1% low, or a
// "frameSpikes" counter if Overlay offers one), otherwise from CPU / GPU peaks. Also the quiet in-game alerts (off by default).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;

namespace HallowTasks
{
    public class HistApp { public string Key, Title; public float Cpu, Gpu, Disk; public long Ram; public byte Kind; }   // Kind: 0 app, 1 game, 2 anti-cheat, 3 Windows
    public class HistPoint { public DateTime At; public float Cpu, Gpu, Ram, Disk; public string Game; public HistApp[] Apps = new HistApp[0]; public int Stutters; }
    public class Stutter { public DateTime At; public string Source, What, Cause; }   // Source: "overlay" or "peak"
    public class Spike
    {
        public string Key, Title; public DateTime From, To; public double Cpu, Gpu, Disk; public long Ram; public bool InGame; public byte Kind;
        public double Score { get { return Math.Max(Cpu, Gpu) + Math.Min(30, Disk / 1048576 * 2); } }
        public string What
        {
            get
            {
                var w = new List<string>();
                if (Cpu >= HistoryStore.SpikeCpu) w.Add(Fmt.Pct(Cpu) + " CPU"); if (Gpu >= HistoryStore.SpikeGpu) w.Add(Fmt.Pct(Gpu) + " GPU"); if (Disk >= HistoryStore.SpikeDisk) w.Add(Fmt.Rate(Disk) + " disk");
                return string.Join(", ", w);
            }
        }
    }

    public class HistoryStore
    {
        public const double SpikeCpu = 15, SpikeGpu = 20, SpikeDisk = 20 * 1048576.0;
        public TimeSpan Keep = TimeSpan.FromHours(1);
        readonly List<HistPoint> pts = new List<HistPoint>(); readonly List<Stutter> stutters = new List<Stutter>();
        readonly object gate = new object();
        public int Count { get { lock (gate) return pts.Count; } }

        // one sample: the PC's totals and the apps that did something (at most 30 a point - an hour stays a few MB)
        public HistPoint Add(Sample s, IList<AppGroup> groups, ProcInfo game, string gameTitle)
        {
            var apps = groups.Where(g => g.Key != "idle" && !(g.Members.Count == 1 && g.Members[0].Pid == 0))
                .Where(g => g.Cpu >= 0.5 || g.Gpu >= 0.5 || g.DiskRead + g.DiskWrite >= 256 * 1024 || g.IsGame)
                .OrderByDescending(g => g.Impact).Take(30)
                .Select(g => new HistApp { Key = g.Key, Title = g.Title, Cpu = (float)g.Cpu, Gpu = (float)g.Gpu, Disk = (float)(g.DiskRead + g.DiskWrite), Ram = g.Ram,
                    Kind = (byte)(g.IsGame || (game != null && g.Members.Any(m => m.Key == game.Key)) ? 1 : g.IsAntiCheat ? 2 : g.IsProtected || g.Category == "Windows" ? 3 : 0) }).ToArray();
            var p = new HistPoint { At = s.At, Cpu = (float)s.Sys.Cpu, Gpu = s.Sys.GpuKnown ? (float)s.Sys.Gpu3D : 0, Ram = s.Sys.RamTotal > 0 ? (float)(100.0 * s.Sys.RamUsed / s.Sys.RamTotal) : 0,
                Disk = (float)(s.Sys.DiskRead + s.Sys.DiskWrite), Game = game == null ? null : (gameTitle ?? game.Name), Apps = apps };
            lock (gate)
            {
                pts.Add(p);
                var cut = s.At - Keep; int n = 0; while (n < pts.Count && pts[n].At < cut) n++; if (n > 0) pts.RemoveRange(0, n);
                n = 0; while (n < stutters.Count && stutters[n].At < cut) n++; if (n > 0) stutters.RemoveRange(0, n);
            }
            if (p.Game != null) PeakStutter(p);
            return p;
        }
        public List<HistPoint> Points(DateTime from) { lock (gate) return pts.Where(x => x.At >= from).ToList(); }
        public List<Stutter> Stutters(DateTime from) { lock (gate) return stutters.Where(x => x.At >= from).ToList(); }
        public void AddStutter(Stutter s) { lock (gate) { stutters.Add(s); var p = pts.LastOrDefault(); if (p != null) p.Stutters++; } }
        public void AddStutterAt(Stutter s) { lock (gate) { stutters.Add(s); stutters.Sort((x, y) => x.At.CompareTo(y.At)); var p = pts.OrderBy(q => Math.Abs((q.At - s.At).TotalSeconds)).FirstOrDefault(); if (p != null) p.Stutters++; } }
        public void Clear() { lock (gate) { pts.Clear(); stutters.Clear(); } }

        // ---- spikes: per app, the stretches where it took at least 15% CPU, 20% GPU or 20 MB/s of disk (your game isn't one: it's meant to)
        public static List<Spike> FindSpikes(IList<HistPoint> points, int max = 60, double gap = 16)
        {
            var open = new Dictionary<string, Spike>(); var last = new Dictionary<string, DateTime>(); var done = new List<Spike>();
            foreach (var p in points.OrderBy(x => x.At))
            {
                foreach (var a in p.Apps)
                {
                    if (a.Kind == 1) continue;
                    bool hot = a.Cpu >= SpikeCpu || a.Gpu >= SpikeGpu || a.Disk >= SpikeDisk;
                    Spike sp; DateTime lt;
                    if (open.TryGetValue(a.Key, out sp) && last.TryGetValue(a.Key, out lt) && (p.At - lt).TotalSeconds > gap) { done.Add(sp); open.Remove(a.Key); sp = null; }
                    if (!hot) continue;
                    if (sp == null || !open.ContainsKey(a.Key)) { sp = new Spike { Key = a.Key, Title = a.Title, From = p.At, To = p.At, Kind = a.Kind }; open[a.Key] = sp; }
                    sp.To = p.At; last[a.Key] = p.At;
                    sp.Cpu = Math.Max(sp.Cpu, a.Cpu); sp.Gpu = Math.Max(sp.Gpu, a.Gpu); sp.Disk = Math.Max(sp.Disk, a.Disk); sp.Ram = Math.Max(sp.Ram, a.Ram);
                    if (p.Game != null) sp.InGame = true;
                }
                // a hot app that isn't in this point any more has cooled down
                foreach (var k in open.Keys.ToList()) { if (!p.Apps.Any(a => a.Key == k && (a.Cpu >= SpikeCpu || a.Gpu >= SpikeGpu || a.Disk >= SpikeDisk)) && (p.At - last[k]).TotalSeconds > gap) { done.Add(open[k]); open.Remove(k); } }
            }
            done.AddRange(open.Values);
            return done.OrderByDescending(x => x.Score).ThenByDescending(x => x.To).Take(max).ToList();
        }

        // ---- stutter moments
        DateTime lastStutter = DateTime.MinValue, overlayAt = DateTime.MinValue;
        readonly List<double> lows = new List<double>(); long spikeCount = -1;
        // without Overlay's numbers: the PC's CPU close to full, or another app suddenly taking much more than it did a moment ago
        void PeakStutter(HistPoint p)
        {
            if ((p.At - overlayAt).TotalSeconds < 15) return;   // Overlay's live numbers are the better source
            if ((p.At - lastStutter).TotalSeconds < 10) return;
            string what = null;
            if (p.Cpu >= 90) what = "The PC's CPU hit " + Fmt.Pct(p.Cpu);
            else
            {
                var jump = Jump(p, 15);
                if (jump != null) what = jump.Title + " jumped to " + (jump.Cpu >= jump.Gpu ? Fmt.Pct(jump.Cpu) + " CPU" : Fmt.Pct(jump.Gpu) + " GPU");
            }
            if (what == null) return;
            lastStutter = p.At;
            AddStutter(new Stutter { At = p.At, Source = "peak", What = what, Cause = Cause(p) });
        }
        // the other app that grew the most compared with its average over the minute before (null when nothing stands out)
        HistApp Jump(HistPoint p, double min)
        {
            List<HistPoint> before; lock (gate) before = pts.Where(x => x.At < p.At && x.At >= p.At.AddSeconds(-60)).ToList();
            if (before.Count < 3) return null;
            HistApp best = null; double bestD = 0;
            foreach (var a in p.Apps)
            {
                if (a.Kind == 1) continue;
                double avgC = before.Average(x => { var o = x.Apps.FirstOrDefault(q => q.Key == a.Key); return o == null ? 0.0 : o.Cpu; });
                double avgG = before.Average(x => { var o = x.Apps.FirstOrDefault(q => q.Key == a.Key); return o == null ? 0.0 : o.Gpu; });
                double d = Math.Max(a.Cpu >= min && a.Cpu >= 3 * avgC ? a.Cpu - avgC : 0, a.Gpu >= min && a.Gpu >= 3 * avgG ? a.Gpu - avgG : 0);
                if (d > bestD) { bestD = d; best = a; }
            }
            return best;
        }
        public string Cause(HistPoint p)
        {
            var j = Jump(p, 5);
            if (j != null) return j.Title + (j.Kind == 2 ? " (anti-cheat)" : j.Kind == 3 ? " (Windows)" : "") + ": " + Fmt.Pct(j.Cpu) + " CPU, " + Fmt.Pct(j.Gpu) + " GPU, " + Fmt.Rate(j.Disk) + " disk";
            var top = p.Apps.Where(a => a.Kind != 1).OrderByDescending(a => a.Cpu + a.Gpu * 0.8).FirstOrDefault();
            if (p.Cpu >= 90) return "Everything together" + (top != null ? " - the busiest other app was " + top.Title + " (" + Fmt.Pct(top.Cpu) + " CPU)" : "");
            return "Nothing else stood out - probably the game itself, a driver or the network";
        }
        // Hallow Overlay's live numbers (its status "live": fps, fpsLow; optionally a "frameSpikes" counter)
        public Stutter OverlayLive(DateTime at, double fps, double fpsLow, long frameSpikes)
        {
            overlayAt = at; Stutter s = null;
            HistPoint p; lock (gate) p = pts.LastOrDefault();
            if (frameSpikes >= 0)
            {
                if (spikeCount >= 0 && frameSpikes > spikeCount && (at - lastStutter).TotalSeconds >= 5)
                    s = new Stutter { At = at, Source = "overlay", What = "Hallow Overlay saw " + (frameSpikes - spikeCount) + " frame-time spike" + (frameSpikes - spikeCount == 1 ? "" : "s") };
                spikeCount = frameSpikes;
            }
            else if (fpsLow > 0)
            {
                if (lows.Count >= 5)
                {
                    var sorted = lows.OrderBy(x => x).ToList(); double usual = sorted[sorted.Count / 2];
                    if (usual >= 30 && fpsLow < usual * 0.5 && (at - lastStutter).TotalSeconds >= 8)
                        s = new Stutter { At = at, Source = "overlay", What = "1% low fell to " + Math.Round(fpsLow) + " fps (usually " + Math.Round(usual) + ")" + (fps > 0 ? ", average " + Math.Round(fps) : "") };
                }
                lows.Add(fpsLow); if (lows.Count > 30) lows.RemoveAt(0);
            }
            if (s == null) return null;
            lastStutter = at; s.Cause = p != null ? Cause(p) : "No samples yet"; AddStutter(s);
            return s;
        }
        public void GameEnded() { lows.Clear(); spikeCount = -1; }

        // Overlay's status.json "live" (only while a game runs; null when it's missing or older than 10 s)
        public static double[] ReadOverlay(string overlayDir)
        {
            try
            {
                var f = Path.Combine(overlayDir, "status.json"); if (!File.Exists(f)) return null;
                if ((DateTime.Now - File.GetLastWriteTime(f)).TotalSeconds > 10) return null;
                return ParseOverlay(File.ReadAllText(f), DateTime.Now);
            }
            catch { return null; }
        }
        // { fps, fpsLow, frameSpikes (-1 = none) }
        public static double[] ParseOverlay(string json, DateTime now)
        {
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); object v;
                if (d == null || !d.TryGetValue("live", out v)) return null;
                var live = v as Dictionary<string, object>; if (live == null) return null;
                object u; DateTime when;
                if (live.TryGetValue("updated", out u) && u is string && DateTime.TryParse((string)u, CultureInfo.InvariantCulture, DateTimeStyles.None, out when) && Math.Abs((now - when).TotalSeconds) > 10) return null;
                Func<string, double> num = k => { object o; return live.TryGetValue(k, out o) && o != null && !(o is string) ? Convert.ToDouble(o, CultureInfo.InvariantCulture) : -1; };
                return new[] { num("fps"), num("fpsLow"), num("frameSpikes") };
            }
            catch { return null; }
        }
    }

    // ---- the 24-hour log: one line a minute (the minute's peaks and its busiest 8 apps), JSON lines, capped
    public class HistoryLog
    {
        public readonly string Path; public long Cap = 1000000; public TimeSpan Keep = TimeSpan.FromHours(24);
        DateTime minute = DateTime.MinValue; readonly List<HistPoint> buf = new List<HistPoint>(); int lines;
        public HistoryLog(string path) { Path = path; }
        public void Push(HistPoint p)
        {
            var m = new DateTime(p.At.Year, p.At.Month, p.At.Day, p.At.Hour, p.At.Minute, 0);
            if (m != minute && buf.Count > 0) Flush();
            minute = m; buf.Add(p);
        }
        public void Flush()
        {
            if (buf.Count == 0) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.AppendAllText(Path, Line(minute, buf) + "\n");
                if (++lines % 60 == 1) Trim();
            }
            catch { }
            buf.Clear();
        }
        public static string Line(DateTime minute, IList<HistPoint> pts)
        {
            var apps = new Dictionary<string, HistApp>();
            foreach (var p in pts) foreach (var a in p.Apps)
                {
                    HistApp o; if (!apps.TryGetValue(a.Key, out o)) apps[a.Key] = o = new HistApp { Key = a.Key, Title = a.Title, Kind = a.Kind };
                    o.Cpu = Math.Max(o.Cpu, a.Cpu); o.Gpu = Math.Max(o.Gpu, a.Gpu); o.Disk = Math.Max(o.Disk, a.Disk); o.Ram = Math.Max(o.Ram, a.Ram);
                }
            var top = apps.Values.OrderByDescending(a => Math.Max(a.Cpu, a.Gpu) + Math.Min(30, a.Disk / 1048576 * 2)).Take(8)
                .Select(a => new object[] { a.Key, a.Title, Math.Round(a.Cpu, 1), Math.Round(a.Gpu, 1), Math.Round(a.Disk / 1048576.0, 2), a.Ram / 1048576, (int)a.Kind }).ToList();
            var game = pts.Select(p => p.Game).FirstOrDefault(g => g != null);
            var d = new Dictionary<string, object> { { "t", minute.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) }, { "cpu", Math.Round(pts.Max(p => p.Cpu), 1) }, { "gpu", Math.Round(pts.Max(p => p.Gpu), 1) },
                { "ram", Math.Round(pts.Average(p => p.Ram), 1) }, { "disk", Math.Round(pts.Max(p => p.Disk) / 1048576.0, 2) }, { "game", game }, { "st", pts.Sum(p => p.Stutters) }, { "apps", top } };
            return new JavaScriptSerializer().Serialize(d);
        }
        public static HistPoint Parse(string line)
        {
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line); object v;
                DateTime t; if (d == null || !d.TryGetValue("t", out v) || !DateTime.TryParseExact(v as string, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return null;
                Func<string, double> num = k => { object o; return d.TryGetValue(k, out o) && o != null && !(o is string) ? Convert.ToDouble(o, CultureInfo.InvariantCulture) : 0; };
                var p = new HistPoint { At = t, Cpu = (float)num("cpu"), Gpu = (float)num("gpu"), Ram = (float)num("ram"), Disk = (float)(num("disk") * 1048576), Stutters = (int)num("st") };
                object g; if (d.TryGetValue("game", out g)) p.Game = g as string;
                var apps = new List<HistApp>(); object al;
                if (d.TryGetValue("apps", out al) && al is System.Collections.IEnumerable)
                    foreach (var o in (System.Collections.IEnumerable)al)
                    {
                        var a = o as object[]; if (a == null) { var l = o as System.Collections.ArrayList; if (l != null) a = l.ToArray(); }
                        if (a == null || a.Length < 7) continue;
                        apps.Add(new HistApp { Key = a[0] as string, Title = a[1] as string, Cpu = (float)Convert.ToDouble(a[2], CultureInfo.InvariantCulture), Gpu = (float)Convert.ToDouble(a[3], CultureInfo.InvariantCulture),
                            Disk = (float)(Convert.ToDouble(a[4], CultureInfo.InvariantCulture) * 1048576), Ram = Convert.ToInt64(a[5], CultureInfo.InvariantCulture) * 1048576, Kind = (byte)Convert.ToInt32(a[6], CultureInfo.InvariantCulture) });
                    }
                p.Apps = apps.Where(a => a.Key != null).ToArray();
                return p;
            }
            catch { return null; }
        }
        public List<HistPoint> Read(DateTime since)
        {
            var r = new List<HistPoint>();
            try { if (File.Exists(Path)) foreach (var l in File.ReadAllLines(Path)) { var p = Parse(l); if (p != null && p.At >= since) r.Add(p); } } catch { }
            return r;
        }
        // older than 24 h out; over the cap: the oldest half out (written atomically)
        public void Trim()
        {
            try
            {
                if (!File.Exists(Path)) return;
                var all = File.ReadAllLines(Path).ToList(); var cut = DateTime.Now - Keep;
                var keep = all.Where(l => { var p = Parse(l); return p != null && p.At >= cut; }).ToList();
                long size = keep.Sum(l => (long)Encoding.UTF8.GetByteCount(l) + 1);
                while (size > Cap && keep.Count > 1) { int drop = keep.Count / 2; size -= keep.Take(drop).Sum(l => (long)Encoding.UTF8.GetByteCount(l) + 1); keep.RemoveRange(0, drop); }
                if (keep.Count == all.Count) return;
                var tmp = Path + ".tmp"; File.WriteAllText(tmp, keep.Count == 0 ? "" : string.Join("\n", keep) + "\n");
                File.Replace(tmp, Path, null);
            }
            catch { }
        }
        public long Size { get { try { return File.Exists(Path) ? new FileInfo(Path).Length : 0; } catch { return 0; } } }
    }

    // ---- quiet alerts while you play: a background app over X% CPU or GPU for more than N seconds (once per app per 10 minutes)
    public class AlertWatch
    {
        readonly Dictionary<string, DateTime> over = new Dictionary<string, DateTime>(), told = new Dictionary<string, DateTime>();
        public List<string[]> Check(IList<AppGroup> groups, ProcInfo game, DateTime now, int cpu, int gpu, int seconds, int selfPid)
        {
            var r = new List<string[]>();
            if (game == null) { over.Clear(); told.Clear(); return r; }
            var seen = new HashSet<string>();
            foreach (var g in groups)
            {
                if (g.IsGame || g.Members.Any(m => m.Key == game.Key) || g.Key == "idle" || (g.Members.Count == 1 && (g.Members[0].Pid == 0 || g.Members[0].Pid == selfPid))) continue;
                bool hot = g.Cpu > cpu || g.Gpu > gpu; if (!hot) continue;
                seen.Add(g.Key); DateTime since;
                if (!over.TryGetValue(g.Key, out since)) { over[g.Key] = now; continue; }
                DateTime t; if ((now - since).TotalSeconds < seconds || (told.TryGetValue(g.Key, out t) && (now - t).TotalMinutes < 10)) continue;
                told[g.Key] = now;
                var what = g.Cpu > cpu ? Fmt.Pct(g.Cpu) + " CPU" : Fmt.Pct(g.Gpu) + " GPU";
                r.Add(new[] { g.Title + " is busy while you play", what + " for over " + seconds + " s." + (g.IsAntiCheat ? " It's anti-cheat - leave it alone." : g.IsProtected || g.Category == "Windows" ? " It's part of Windows." : " Hallow Tasks > What's slowing my game can lower it or turn on Efficiency mode.") });
            }
            foreach (var k in over.Keys.Where(k => !seen.Contains(k)).ToList()) over.Remove(k);
            return r;
        }
    }

    // ---- the History timeline: the PC's CPU and GPU, the game's time (a band), spikes (marks at the top) and stutters (red lines)
    public class Timeline : FrameworkElement
    {
        public List<HistPoint> Points = new List<HistPoint>(); public List<Stutter> Stutters = new List<Stutter>(); public List<Spike> Spikes = new List<Spike>();
        public DateTime From, To;
        public Timeline() { MouseMove += (s, e) => Tip(e.GetPosition(this).X); }
        double X(DateTime t, double w) { var span = (To - From).TotalSeconds; return span <= 0 ? 0 : 1 + (w - 2) * Math.Max(0, Math.Min(1, (t - From).TotalSeconds / span)); }
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight - 16; if (w < 20 || h < 20) return;
            dc.DrawRectangle(Ui.R("GraphBg"), new Pen(Ui.R("Line"), 1), new Rect(0.5, 0.5, w - 1, h - 1));
            var grid = new Pen(Ui.R("GraphGrid"), 1); grid.Freeze();
            for (int i = 1; i < 4; i++) { double y = Math.Round(h * i / 4) + 0.5; dc.DrawLine(grid, new Point(1, y), new Point(w - 1, y)); }
            var pts = Points.OrderBy(p => p.At).ToList();
            double step = pts.Count > 1 ? (To - From).TotalSeconds / Math.Max(1, pts.Count) : 60;
            // game bands
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i].Game == null) continue; int j = i; while (j + 1 < pts.Count && pts[j + 1].Game != null && (pts[j + 1].At - pts[j].At).TotalSeconds < 120) j++;
                double x0 = X(pts[i].At, w), x1 = Math.Max(x0 + 2, X(pts[j].At, w));
                dc.DrawRectangle(Ui.R("GameBand"), null, new Rect(x0, 1, x1 - x0, h - 2)); i = j;
            }
            Action<Func<HistPoint, double>, string> line = (f, key) =>
            {
                if (pts.Count < 2) return;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    bool started = false; DateTime prev = DateTime.MinValue;
                    foreach (var p in pts)
                    {
                        var pt = new Point(X(p.At, w), h - 1 - (h - 3) * Math.Max(0, Math.Min(100, f(p))) / 100);
                        bool gap = started && (p.At - prev).TotalSeconds > Math.Max(30, step * 4);
                        if (!started || gap) { g.BeginFigure(pt, false, false); started = true; } else g.LineTo(pt, true, true);
                        prev = p.At;
                    }
                }
                geo.Freeze(); var pen = new Pen(Ui.R(key), 1.5) { LineJoin = PenLineJoin.Round }; pen.Freeze(); dc.DrawGeometry(null, pen, geo);
            };
            line(p => p.Gpu, "Graph3D"); line(p => p.Cpu, "GraphCpu");
            foreach (var s in Spikes) { double x = X(s.From, w); var tri = new StreamGeometry(); using (var g = tri.Open()) { g.BeginFigure(new Point(x - 4, 2), true, true); g.LineTo(new Point(x + 4, 2), true, false); g.LineTo(new Point(x, 9), true, false); } tri.Freeze(); dc.DrawGeometry(Ui.R("SpikeMark"), null, tri); }
            var sp = new Pen(Ui.R("StutterMark"), 1.5); sp.Freeze();
            foreach (var s in Stutters) { double x = X(s.At, w); dc.DrawLine(sp, new Point(x, 1), new Point(x, h - 1)); dc.DrawEllipse(Ui.R("StutterMark"), null, new Point(x, h - 5), 3, 3); }
            // time labels
            var span = (To - From).TotalMinutes; var fmt = span > 180 ? "HH:mm" : "HH:mm"; double dip = 1; try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
            for (int i = 0; i <= 6; i++)
            {
                var t = From.AddMinutes(span * i / 6); var ft = new FormattedText(t.ToString(fmt, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Mono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10, Ui.R("Muted"), dip);
                double x = X(t, w) - (i == 0 ? 0 : i == 6 ? ft.Width : ft.Width / 2); dc.DrawText(ft, new Point(x, h + 3));
            }
        }
        void Tip(double x)
        {
            if (Points.Count == 0 || ActualWidth < 20) { ToolTip = null; return; }
            var t = From.AddSeconds((To - From).TotalSeconds * Math.Max(0, Math.Min(1, (x - 1) / (ActualWidth - 2))));
            var p = Points.OrderBy(q => Math.Abs((q.At - t).TotalSeconds)).First();
            var top = p.Apps.Where(a => a.Kind != 1).OrderByDescending(a => a.Cpu + a.Gpu * 0.8).Take(3).Select(a => a.Title + " " + Fmt.Pct(a.Cpu) + " CPU / " + Fmt.Pct(a.Gpu) + " GPU");
            var st = Stutters.Where(s => Math.Abs((s.At - p.At).TotalSeconds) < Math.Max(10, (To - From).TotalSeconds / 200)).Select(s => "Stutter: " + s.What);
            ToolTip = p.At.ToString("HH:mm:ss") + "   PC CPU " + Fmt.Pct(p.Cpu) + ", GPU " + Fmt.Pct(p.Gpu) + (p.Game != null ? "   (" + p.Game + ")" : "") + "\n" + string.Join("\n", top.Concat(st));
        }
    }
}
