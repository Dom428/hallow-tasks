// Hallow Tasks - the logic without Windows in it (unit-tested with made-up processes): what a process is, how processes are
// grouped into apps like Task Manager does it, the Impact estimate (the default sort), the power usage hint, the protected /
// anti-cheat / game lists, and what the game panel ranks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HallowTasks
{
    // one process in one sample
    public class ProcInfo
    {
        public int Pid, ParentPid, Session, Threads;
        public string Name = "";          // the exe file name ("chrome.exe"); "System", "Registry" etc. for the kernel's own
        public string Path;               // full path, or null when Windows won't say (protected / other users' processes)
        public long CreateTime;           // FILETIME ticks - with Pid it identifies the process (pids are reused)
        public double Cpu;                // % of the whole CPU (all cores = 100)
        public long CpuTime;              // CPU time used since it started (100 ns units)
        public long Ram;                  // private working set, bytes (Task Manager's "Memory" column)
        public double Gpu;                // % - the busiest 3D engine use of this process (GPU Engine counters, engtype_3D, summed per PID)
        public long GpuMem;               // dedicated GPU memory, bytes
        public double DiskRead, DiskWrite; // bytes/s of file and device I/O (like Process Explorer's I/O column)
        public int Conns = -1;            // open TCP connections (-1 = not counted)
        public double NetSend = -1, NetRecv = -1;   // bytes/s - only with the optional "Precise network" helper (admin, ETW); -1 = not known
        public bool HasWindow;            // has a visible top-level window (an "app" in Task Manager's words)
        public string Description, Company, Signer;   // from the file (filled in later, in the background)
        public bool? Signed;              // valid Authenticode signature (null = not checked yet / no file)
        public string Key { get { return Pid + ":" + CreateTime; } }
        public string Bare { get { return Known.Bare(Name); } }
        public ProcInfo Clone() { return (ProcInfo)MemberwiseClone(); }
    }

    // a group of processes that belong to one app (Discord, Chrome, Hallow...), Task Manager style
    public class AppGroup
    {
        public string Key, Title, Category;   // Category: "Apps", "Background", "Windows", "Hallow"
        public List<ProcInfo> Members = new List<ProcInfo>();
        public double Cpu, Gpu, DiskRead, DiskWrite; public long Ram, GpuMem; public int Conns = -1; public double NetBytes = -1;
        public double Impact; public string Power;
        public bool IsGame, IsAntiCheat, IsProtected;
        public ProcInfo Main { get { return Members.Count == 0 ? null : Members.FirstOrDefault(m => m.HasWindow) ?? Members.OrderBy(m => m.CreateTime).First(); } }
        public string Path { get { var m = Main; return m == null ? null : Members.Select(x => x.Path).FirstOrDefault(p => p != null); } }
        public string Publisher { get { return Members.Select(x => x.Signer ?? x.Company).FirstOrDefault(p => !string.IsNullOrEmpty(p)); } }
        public bool? Signed { get { if (Members.Any(x => x.Signed == false)) return false; if (Members.Any(x => x.Signed == true)) return true; return null; } }
        public void Sum()
        {
            Cpu = Members.Sum(m => m.Cpu); Gpu = Math.Min(100, Members.Sum(m => m.Gpu)); DiskRead = Members.Sum(m => m.DiskRead); DiskWrite = Members.Sum(m => m.DiskWrite);
            Ram = Members.Sum(m => m.Ram); GpuMem = Members.Sum(m => m.GpuMem);
            Conns = Members.All(m => m.Conns < 0) ? -1 : Members.Where(m => m.Conns > 0).Sum(m => m.Conns);
            NetBytes = Members.All(m => m.NetSend < 0) ? -1 : Members.Where(m => m.NetSend >= 0).Sum(m => m.NetSend + m.NetRecv);
            Impact = ImpactCalc.Score(Cpu, Gpu, DiskRead + DiskWrite, Ram); Power = ImpactCalc.Power(Cpu, Gpu, DiskRead + DiskWrite);
        }
    }

    // ---------------- the lists
    public static class Known
    {
        public static string Bare(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "";
            var s = exe.Trim(); if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.ToLowerInvariant();
        }
        // games (exe names without .exe). Settings > "Also treat these as games" adds more; Hallow Hub's "playing" list too (by name)
        public static readonly string[] Games = { "DeltaForceClient-Win64-Shipping", "Discovery-d" };

        // anti-cheat: never lowered or ended, shown as "Anti-cheat - leave alone". "*" = any ending.
        public static readonly string[] AntiCheat = {
            "sguard*", "sguardsvc*", "ace-*", "anticheatexpert*", "acebase*", "easyanticheat*", "eaanticheat*", "start_protected_game", "beservice*", "bedaisy*", "battleye*",
            "vgc", "vgtray", "vgk", "faceit*", "gamemon*", "gameguard*", "xhunter*", "xigncode*", "pnkbstr*", "nprotect*", "mhyprot*", "ricochet*", "equ8*", "atvi-randgrid*",
            "denuvo*", "tencentace*", "tp3helper", "ntrtscan*" };
        public const string AntiCheatTag = "Anti-cheat \u2014 leave alone";

        // Windows' own core processes: ending them crashes or signs out Windows, so Hallow Tasks refuses
        static readonly HashSet<string> Critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "system", "idle", "system idle process", "registry", "memory compression", "secure system", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
            "svchost", "dwm", "fontdrvhost", "sihost", "ctfmon", "explorer", "msmpeng", "nissrv", "securityhealthservice", "sgrmbroker", "wudfhost", "spoolsv", "audiodg",
            "lsm", "taskhostw", "startmenuexperiencehost", "shellexperiencehost", "searchhost", "textinputhost", "dllhost", "runtimebroker", "wmiprvse", "mssense",
            "mpdefendercoreservice", "smartscreen", "dashost", "mousocoreworker", "useroobebroker" };
        public static bool IsGameName(string exe, IEnumerable<string> extra)
        {
            var b = Bare(exe); if (b.Length == 0) return false;
            foreach (var g in Games) if (Bare(g) == b) return true;
            if (extra != null) foreach (var g in extra) if (Bare(g) == b) return true;
            return false;
        }
        public static bool Match(string pattern, string bare)
        {
            pattern = pattern.ToLowerInvariant();
            return pattern.EndsWith("*") ? bare.StartsWith(pattern.Substring(0, pattern.Length - 1)) : bare == pattern;
        }
        public static bool IsAntiCheat(string exe) { var b = Bare(exe); return b.Length > 0 && AntiCheat.Any(p => Match(p, b)); }
        public static bool IsCritical(string exe) { return Critical.Contains(Bare(exe)); }
        // a process Hallow Tasks never ends or reprioritises: Windows' core ones, pids 0 and 4, session-0 programs in the Windows folder
        public static bool IsProtected(ProcInfo p, string windowsDir)
        {
            if (p.Pid == 0 || p.Pid == 4) return true;
            if (IsCritical(p.Name)) return true;
            if (p.Session == 0 && (p.Path == null || (windowsDir != null && p.Path.StartsWith(windowsDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))) return true;
            return false;
        }

        // exe -> (group key, friendly name). Helpers of one app (Chrome's many chrome.exe, Steam's web helper) go in one group.
        static readonly Dictionary<string, string[]> Apps = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
            { "chrome", new[] { "chrome", "Google Chrome" } }, { "msedge", new[] { "edge", "Microsoft Edge" } }, { "firefox", new[] { "firefox", "Firefox" } },
            { "brave", new[] { "brave", "Brave" } }, { "opera", new[] { "opera", "Opera" } }, { "opera_gx", new[] { "opera", "Opera GX" } }, { "vivaldi", new[] { "vivaldi", "Vivaldi" } },
            { "discord", new[] { "discord", "Discord" } }, { "discordptb", new[] { "discord", "Discord" } }, { "discordcanary", new[] { "discord", "Discord" } },
            { "steam", new[] { "steam", "Steam" } }, { "steamwebhelper", new[] { "steam", "Steam" } }, { "steamservice", new[] { "steam", "Steam" } },
            { "gameoverlayui", new[] { "steam", "Steam" } }, { "gameoverlayui64", new[] { "steam", "Steam" } },
            { "epicgameslauncher", new[] { "epic", "Epic Games Launcher" } }, { "epicwebhelper", new[] { "epic", "Epic Games Launcher" } }, { "eosoverlayrenderer-win64-shipping", new[] { "epic", "Epic Games Launcher" } },
            { "battle.net", new[] { "battlenet", "Battle.net" } }, { "agent", new[] { "battlenet", "Battle.net" } },
            { "eadesktop", new[] { "ea", "EA app" } }, { "eabackgroundservice", new[] { "ea", "EA app" } }, { "ubisoftconnect", new[] { "ubisoft", "Ubisoft Connect" } }, { "upc", new[] { "ubisoft", "Ubisoft Connect" } },
            { "spotify", new[] { "spotify", "Spotify" } }, { "code", new[] { "vscode", "Visual Studio Code" } }, { "obs64", new[] { "obs", "OBS Studio" } },
            { "ms-teams", new[] { "teams", "Microsoft Teams" } }, { "teams", new[] { "teams", "Microsoft Teams" } }, { "slack", new[] { "slack", "Slack" } },
            { "whatsapp", new[] { "whatsapp", "WhatsApp" } }, { "telegram", new[] { "telegram", "Telegram" } }, { "onedrive", new[] { "onedrive", "OneDrive" } },
            { "nvcontainer", new[] { "nvidia", "NVIDIA" } }, { "nvidia app", new[] { "nvidia", "NVIDIA" } }, { "nvidia overlay", new[] { "nvidia", "NVIDIA" } }, { "nvidia share", new[] { "nvidia", "NVIDIA" } },
            { "nvdisplay.container", new[] { "nvidia", "NVIDIA" } }, { "nvsphelper64", new[] { "nvidia", "NVIDIA" } },
            { "radeonsoftware", new[] { "amd", "AMD Software" } }, { "amdrsserv", new[] { "amd", "AMD Software" } }, { "amdow", new[] { "amd", "AMD Software" } },
            { "svchost", new[] { "svchost", "Service Host (svchost)" } }, { "explorer", new[] { "explorer", "Windows Explorer" } },
            { "msmpeng", new[] { "defender", "Microsoft Defender" } }, { "nissrv", new[] { "defender", "Microsoft Defender" } }, { "mpdefendercoreservice", new[] { "defender", "Microsoft Defender" } },
            { "securityhealthservice", new[] { "defender", "Microsoft Defender" } }, { "securityhealthsystray", new[] { "defender", "Microsoft Defender" } },
            { "wallpaper64", new[] { "wallpaper", "Wallpaper Engine" } }, { "wallpaper32", new[] { "wallpaper", "Wallpaper Engine" } }, { "webwallpaper32", new[] { "wallpaper", "Wallpaper Engine" } },
        };
        // processes that belong to whichever app started them (Task Manager shows them under it)
        static readonly HashSet<string> Hosted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "conhost", "msedgewebview2", "werfault", "crashpad_handler", "cefsharp.browsersubprocess", "qtwebengineprocess" };
        public static bool IsHosted(string exe) { return Hosted.Contains(Bare(exe)); }
        public static string[] AppOf(string exe) { string[] a; return Apps.TryGetValue(Bare(exe), out a) ? a : null; }
        public static bool IsHallow(ProcInfo p)
        {
            var b = Bare(p.Name);
            if (b.StartsWith("hallow") && b.Length > 6) return true;
            return p.Path != null && p.Path.IndexOf(@"\Programs\Hallow ", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        // a friendlier name for a single process: the file's description, else the exe name without ".exe"
        public static string Friendly(ProcInfo p)
        {
            var a = AppOf(p.Name); if (a != null && p.Description == null) return a[1];
            if (!string.IsNullOrWhiteSpace(p.Description)) return p.Description.Trim();
            var n = p.Name ?? ""; return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n;
        }
    }

    // ---------------- grouping
    public static class Grouping
    {
        public static List<AppGroup> Build(IList<ProcInfo> procs, string windowsDir, IEnumerable<string> extraGames)
        {
            var byPid = new Dictionary<int, ProcInfo>(); foreach (var p in procs) byPid[p.Pid] = p;
            var key = new Dictionary<ProcInfo, string>(); var title = new Dictionary<string, string>();
            Func<ProcInfo, ProcInfo> parentOf = p =>
            {
                ProcInfo q; if (p.ParentPid == p.Pid || !byPid.TryGetValue(p.ParentPid, out q)) return null;
                return q.CreateTime <= p.CreateTime ? q : null;   // a parent that started later is a reused pid, not the real parent
            };
            Func<ProcInfo, string> own = p =>
            {
                if (Known.IsHallow(p)) { title["hallow"] = "Hallow"; return "hallow"; }
                var a = Known.AppOf(p.Name); if (a != null) { title[a[0]] = a[1]; return a[0]; }
                return null;
            };
            Func<ProcInfo, int, string> resolve = null;
            resolve = (p, depth) =>
            {
                string k; if (key.TryGetValue(p, out k)) return k;
                k = own(p);
                if (k == null && depth < 32)
                {
                    var par = parentOf(p);
                    if (par != null && !Known.IsCritical(par.Name))
                    {
                        // helpers of the same exe, things only hosted by an app (conhost, WebView2) and helpers installed in the app's folder
                        var pk = resolve(par, depth + 1);
                        if (string.Equals(par.Name, p.Name, StringComparison.OrdinalIgnoreCase) || Known.IsHosted(p.Name) || SameFolder(par.Path, p.Path)) k = pk;
                    }
                }
                if (k == null) k = "exe:" + Known.Bare(p.Name) + (string.IsNullOrEmpty(p.Path) ? "" : "|" + Folder(p.Path).ToLowerInvariant());
                key[p] = k; return k;
            };
            foreach (var p in procs) resolve(p, 0);

            var groups = new Dictionary<string, AppGroup>();
            foreach (var p in procs.OrderBy(x => x.CreateTime))
            {
                var k = key[p]; AppGroup g;
                if (!groups.TryGetValue(k, out g)) groups[k] = g = new AppGroup { Key = k };
                g.Members.Add(p);
            }
            foreach (var g in groups.Values)
            {
                string t; g.Title = title.TryGetValue(g.Key, out t) ? t : Known.Friendly(g.Main);
                g.IsGame = g.Members.Any(m => Known.IsGameName(m.Name, extraGames));
                g.IsAntiCheat = g.Members.Any(m => Known.IsAntiCheat(m.Name));
                g.IsProtected = g.Members.Any(m => Known.IsProtected(m, windowsDir));
                g.Category = g.Key == "hallow" ? "Hallow" : g.Members.Any(m => m.HasWindow) && !g.IsProtected ? "Apps" : g.Members.All(m => IsWindows(m, windowsDir)) ? "Windows" : "Background";
                if (g.Key == "explorer") g.Category = "Windows";
                g.Sum();
            }
            return groups.Values.ToList();
        }
        static bool IsWindows(ProcInfo m, string windowsDir)
        {
            if (m.Pid == 0 || m.Pid == 4 || Known.IsCritical(m.Name)) return true;
            if (m.Path == null) return m.Session == 0;
            return windowsDir != null && m.Path.StartsWith(windowsDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
        static string Folder(string path) { try { return System.IO.Path.GetDirectoryName(path) ?? ""; } catch { return ""; } }
        // same install folder (or one inside the other: "Discord\app-1.0" and "Discord\app-1.0\modules")
        public static bool SameFolder(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            var fa = Folder(a).TrimEnd('\\') + "\\"; var fb = Folder(b).TrimEnd('\\') + "\\";
            if (fa.Length < 4 || fb.Length < 4) return false;
            // never "the same app" just because both are in the Windows folder or Program Files itself
            foreach (var root in new[] { @"\windows\system32\", @"\windows\syswow64\", @"\windows\" }) if (fa.EndsWith(root, StringComparison.OrdinalIgnoreCase) || fb.EndsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            return fb.StartsWith(fa, StringComparison.OrdinalIgnoreCase) || fa.StartsWith(fb, StringComparison.OrdinalIgnoreCase);
        }

        // a process and everything it started (by parent PID; a "child" that started before its parent is a reused PID, not a child)
        public static List<ProcInfo> Tree(ProcInfo root, IList<ProcInfo> all)
        {
            var r = new List<ProcInfo>(); if (root == null) return r; var q = new Queue<ProcInfo>(); q.Enqueue(root); var seen = new HashSet<int>();
            while (q.Count > 0)
            {
                var p = q.Dequeue(); if (!seen.Add(p.Pid)) continue; r.Add(p);
                foreach (var c in all.Where(x => x.ParentPid == p.Pid && x.Pid != p.Pid && x.CreateTime >= p.CreateTime)) q.Enqueue(c);
            }
            return r;
        }

        // the tree view: every process under the one that started it (a "parent" that started later is a reused PID, so that process
        // is a root). Returns the rows in display order with their depth; collapsed = process keys whose children are hidden;
        // show = the processes that match the search/filter (their parents are kept so the tree stays readable; null = all).
        public class TreeRow { public ProcInfo Proc; public int Depth; public bool HasChildren; }
        public static List<TreeRow> Forest(IList<ProcInfo> procs, ICollection<string> collapsed, Func<ProcInfo, bool> show, Func<ProcInfo, double> order)
        {
            var byPid = new Dictionary<int, ProcInfo>(); foreach (var p in procs) byPid[p.Pid] = p;
            var kids = new Dictionary<int, List<ProcInfo>>(); var roots = new List<ProcInfo>();
            foreach (var p in procs)
            {
                ProcInfo par;
                if (p.Pid != 0 && p.ParentPid != p.Pid && byPid.TryGetValue(p.ParentPid, out par) && par.CreateTime <= p.CreateTime && par.Pid != 0)
                { List<ProcInfo> l; if (!kids.TryGetValue(par.Pid, out l)) kids[par.Pid] = l = new List<ProcInfo>(); l.Add(p); }
                else roots.Add(p);
            }
            var keep = new HashSet<int>();
            Func<ProcInfo, int, bool> mark = null;
            mark = (p, depth) =>
            {
                bool any = show == null || show(p); List<ProcInfo> l;
                if (depth < 64 && kids.TryGetValue(p.Pid, out l)) foreach (var c in l) if (mark(c, depth + 1)) any = true;
                if (any) keep.Add(p.Pid); return any;
            };
            foreach (var r in roots) mark(r, 0);
            var rows = new List<TreeRow>(); var seen = new HashSet<int>();
            Action<ProcInfo, int> walk = null;
            walk = (p, depth) =>
            {
                if (!keep.Contains(p.Pid) || !seen.Add(p.Pid)) return;
                List<ProcInfo> l; bool has = kids.TryGetValue(p.Pid, out l) && l.Any(c => keep.Contains(c.Pid));
                rows.Add(new TreeRow { Proc = p, Depth = depth, HasChildren = has });
                if (has && (collapsed == null || !collapsed.Contains(p.Key)) && depth < 64) foreach (var c in l.OrderByDescending(order).ThenBy(c => c.Pid)) walk(c, depth + 1);
            };
            foreach (var r in roots.Where(r => r.Pid != 0).OrderBy(r => r.Pid == 4 ? 0 : 1).ThenByDescending(order).ThenBy(r => r.Pid)) walk(r, 0);
            return rows;
        }

        // the list order: column + direction; ties by name
        public static List<AppGroup> Sort(IEnumerable<AppGroup> groups, string column, bool descending)
        {
            Func<AppGroup, double> f;
            switch (column)
            {
                case "cpu": f = g => g.Cpu; break;
                case "ram": f = g => g.Ram; break;
                case "gpu": f = g => g.Gpu; break;
                case "gpumem": f = g => g.GpuMem; break;
                case "disk": f = g => g.DiskRead + g.DiskWrite; break;
                case "net": f = g => g.NetBytes >= 0 ? g.NetBytes : g.Conns; break;
                case "power": f = g => ImpactCalc.PowerValue(g.Cpu, g.Gpu, g.DiskRead + g.DiskWrite); break;
                case "name": return (descending ? groups.OrderByDescending(g => g.Title, StringComparer.CurrentCultureIgnoreCase) : groups.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)).ToList();
                case "publisher": return (descending ? groups.OrderByDescending(g => g.Publisher ?? "", StringComparer.CurrentCultureIgnoreCase) : groups.OrderBy(g => g.Publisher ?? "\uffff", StringComparer.CurrentCultureIgnoreCase)).ThenBy(g => g.Title).ToList();
                default: f = g => g.Impact; break;
            }
            return (descending ? groups.OrderByDescending(f) : groups.OrderBy(f)).ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        // search (name, title, description, publisher, path, pid) and the filter chips
        public static bool Matches(AppGroup g, string search, string filter)
        {
            if (!string.IsNullOrEmpty(filter) && filter != "All" && g.Category != filter) return false;
            if (string.IsNullOrWhiteSpace(search)) return true;
            var s = search.Trim();
            if (Has(g.Title, s) || Has(g.Publisher, s)) return true;
            foreach (var m in g.Members) if (Has(m.Name, s) || Has(m.Description, s) || Has(m.Path, s) || m.Pid.ToString() == s) return true;
            return false;
        }
        static bool Has(string a, string s) { return a != null && a.IndexOf(s, StringComparison.CurrentCultureIgnoreCase) >= 0; }
    }

    // ---------------- Impact (the default sort) and the power usage hint - estimates, explained in the app
    public static class ImpactCalc
    {
        const double MB = 1024 * 1024, GB = 1024 * MB;
        // CPU % + 0.8 x GPU % + disk (2 points per MB/s, at most 30) + memory (2 points per GB, at most 10)
        public static double Score(double cpu, double gpu, double diskBps, long ram)
        {
            return Math.Max(0, cpu) + 0.8 * Math.Max(0, gpu) + Math.Min(30, Math.Max(0, diskBps) / MB * 2) + Math.Min(10, Math.Max(0, ram) / GB * 2);
        }
        public static double PowerValue(double cpu, double gpu, double diskBps) { return Math.Max(0, cpu) + 0.7 * Math.Max(0, gpu) + Math.Min(20, Math.Max(0, diskBps) / MB); }
        public static string Power(double cpu, double gpu, double diskBps)
        {
            var p = PowerValue(cpu, gpu, diskBps);
            return p < 0.5 ? "Very low" : p < 5 ? "Low" : p < 15 ? "Moderate" : p < 35 ? "High" : "Very high";
        }
        // 0..3 for the cell shading (more use = a stronger tint)
        public static int Heat(double percent) { return percent >= 50 ? 3 : percent >= 15 ? 2 : percent >= 3 ? 1 : 0; }
        public static int HeatBytes(double bytesPerSec) { return Heat(bytesPerSec / MB * 5); }
        public static int HeatRam(long bytes) { return Heat(bytes / GB * 25); }
    }

    // ---------------- which game is running
    public static class GameDetect
    {
        static string Norm(string s) { if (s == null) return ""; var sb = new StringBuilder(); foreach (var c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c); return sb.ToString(); }
        // the running game's process (the first match), or null. Known names first, then Hallow Hub's "playing" names
        // ("Delta Force" matches DeltaForceClient-Win64-Shipping.exe or a file described as "Delta Force").
        public static ProcInfo Find(IEnumerable<ProcInfo> procs, IEnumerable<string> extraGames, IEnumerable<string> hubPlaying)
        {
            var list = procs.ToList();
            var known = list.Where(p => Known.IsGameName(p.Name, extraGames)).OrderByDescending(p => p.Gpu).ThenBy(p => p.CreateTime).FirstOrDefault();
            if (known != null) return known;
            if (hubPlaying != null)
                foreach (var name in hubPlaying)
                {
                    var n = Norm(name); if (n.Length < 4) continue;
                    var hit = list.Where(p => !Known.IsAntiCheat(p.Name) && !Known.IsCritical(p.Name) && Known.AppOf(p.Name) == null &&
                                              (Norm(Known.Bare(p.Name)).StartsWith(n) || Norm(p.Description) == n))
                                  .OrderByDescending(p => p.Gpu).ThenByDescending(p => p.Ram).FirstOrDefault();
                    if (hit != null) return hit;
                }
            return null;
        }
        public static string Title(ProcInfo game, IEnumerable<string> hubPlaying)
        {
            if (game == null) return null;
            if (Known.Bare(game.Name) == "deltaforceclient-win64-shipping") return "Delta Force";
            if (hubPlaying != null) foreach (var n in hubPlaying) { var k = Norm(n); if (k.Length >= 4 && (Norm(Known.Bare(game.Name)).StartsWith(k) || Norm(game.Description) == k)) return n; }
            return Known.Friendly(game);
        }
    }

    // ---------------- "What's slowing my game": the other apps, ranked by what they took while the game ran
    public class GameRow
    {
        public AppGroup Group; public double Cpu, Gpu, Disk, Score; public int Samples;
        public string Tag;        // Known.AntiCheatTag, "Windows \u2014 leave alone", or null
        public bool CanAct { get { return Tag == null; } }
    }
    public class GameWatch
    {
        public int Window = 10;   // average over the last N samples (1 per second = 10 s)
        readonly Dictionary<string, Queue<double[]>> hist = new Dictionary<string, Queue<double[]>>();
        public string GameKey; public DateTime? Since;
        // one sample while the game runs (game = null: the history is cleared)
        public void Push(IList<AppGroup> groups, ProcInfo game, DateTime now)
        {
            if (game == null) { hist.Clear(); GameKey = null; Since = null; return; }
            if (GameKey != game.Key) { hist.Clear(); GameKey = game.Key; Since = now; }
            var seen = new HashSet<string>();
            foreach (var g in groups)
            {
                seen.Add(g.Key); Queue<double[]> q;
                if (!hist.TryGetValue(g.Key, out q)) hist[g.Key] = q = new Queue<double[]>();
                q.Enqueue(new[] { g.Cpu, g.Gpu, g.DiskRead + g.DiskWrite }); while (q.Count > Window) q.Dequeue();
            }
            foreach (var k in hist.Keys.Where(k => !seen.Contains(k)).ToList()) hist.Remove(k);
        }
        // the ranking (highest first). Never the game itself; anti-cheat and Windows' own processes are listed but marked.
        public List<GameRow> Rank(IList<AppGroup> groups, ProcInfo game, int max)
        {
            var rows = new List<GameRow>();
            if (game == null) return rows;
            foreach (var g in groups)
            {
                if (g.Members.Any(m => m.Key == game.Key) || g.IsGame) continue;
                Queue<double[]> q; double c = g.Cpu, gp = g.Gpu, d = g.DiskRead + g.DiskWrite; int n = 1;
                if (hist.TryGetValue(g.Key, out q) && q.Count > 0) { c = q.Average(x => x[0]); gp = q.Average(x => x[1]); d = q.Average(x => x[2]); n = q.Count; }
                var r = new GameRow { Group = g, Cpu = c, Gpu = gp, Disk = d, Samples = n, Score = ImpactCalc.Score(c, gp, d, 0) };
                r.Tag = g.IsAntiCheat ? Known.AntiCheatTag : g.IsProtected || g.Category == "Windows" ? "Windows \u2014 leave alone" : null;
                rows.Add(r);
            }
            return rows.Where(r => r.Score >= 0.2 || r.Tag == Known.AntiCheatTag).OrderByDescending(r => r.Score).ThenBy(r => r.Group.Title).Take(max).ToList();
        }
    }

    // ---------------- what may be touched (End task, priority, affinity)
    public static class Safety
    {
        // null = allowed; otherwise the reason, in plain words
        public static string WhyNot(ProcInfo p, ProcInfo game, IEnumerable<string> extraGames, string windowsDir, int selfPid)
        {
            if (p == null) return "That process has closed.";
            if (p.Pid == selfPid) return "That's Hallow Tasks itself - use Quit in its tray menu.";
            if (Known.IsAntiCheat(p.Name)) return "That's anti-cheat (" + p.Name + "). Leave it alone - games kick or ban you when it's touched.";
            if ((game != null && game.Key == p.Key) || Known.IsGameName(p.Name, extraGames)) return "That's your game - Hallow Tasks never touches it by itself, and priority / affinity / Efficiency stay off for games. If it hangs on exit, use Close game…";
            if (p.Pid == 0 || p.Pid == 4 || Known.IsCritical(p.Name)) return p.Name + " is part of Windows. Ending or changing it can crash Windows or sign you out, so Hallow Tasks won't.";
            if (Known.IsProtected(p, windowsDir)) return p.Name + " is a Windows system process. Hallow Tasks leaves those alone.";
            return null;
        }
        public static string WhyNot(AppGroup g, ProcInfo game, IEnumerable<string> extraGames, string windowsDir, int selfPid)
        {
            // Hallow Tasks itself is skipped when its group has others (the Hallow group): the actions leave it out
            var members = g.Members.Count > 1 ? g.Members.Where(m => m.Pid != selfPid).ToList() : g.Members;
            foreach (var m in members) { var w = WhyNot(m, game, extraGames, windowsDir, selfPid); if (w != null) return w; }
            return null;
        }
    }

    // ---------------- process control (real: Windows; tests: a fake or dummy children the test started)
    public enum Prio { Idle, BelowNormal, Normal, AboveNormal, High }
    public interface IProcessControl
    {
        Prio? GetPriority(int pid);
        string SetPriority(int pid, Prio p);          // null = done, else the error
        string Kill(int pid);
        string SetAffinity(int pid, long mask);
        long Affinity(int pid);
        string SetEfficiency(int pid, bool on);        // Efficiency mode: EcoQoS (power throttling) + Idle priority, like Windows 11 Task Manager
        bool? GetEfficiency(int pid);                  // null = can't tell
        long CreateTime(int pid);                      // 0 = gone
    }
    // lowers priorities while the game runs and puts them back afterwards (or when you click Restore / quit Hallow Tasks)
    public class PriorityKeeper
    {
        public class Entry { public int Pid; public long Created; public string Name; public Prio Was, Now; }
        readonly IProcessControl ctl; public readonly List<Entry> Lowered = new List<Entry>();
        public PriorityKeeper(IProcessControl c) { ctl = c; }
        public string Lower(ProcInfo p, Prio to)
        {
            if (to != Prio.BelowNormal && to != Prio.Idle) return "Only Below normal or Idle while the game runs.";
            var was = ctl.GetPriority(p.Pid); if (was == null) return "Windows won't let Hallow Tasks change " + p.Name + " (it may belong to another user or be protected).";
            var e = ctl.SetPriority(p.Pid, to); if (e != null) return e;
            var old = Lowered.FirstOrDefault(x => x.Pid == p.Pid && x.Created == p.CreateTime);
            if (old != null) old.Now = to; else Lowered.Add(new Entry { Pid = p.Pid, Created = p.CreateTime, Name = p.Name, Was = was.Value, Now = to });
            return null;
        }
        // back to what they were; processes that have closed (or whose pid now belongs to another process) are skipped
        public int RestoreAll()
        {
            int n = 0;
            foreach (var e in Lowered.ToList())
            {
                var ct = ctl.CreateTime(e.Pid);
                if (ct != 0 && ct == e.Created && ctl.SetPriority(e.Pid, e.Was) == null) n++;
                Lowered.Remove(e);
            }
            return n;
        }
        public bool Restore(int pid)
        {
            var e = Lowered.FirstOrDefault(x => x.Pid == pid); if (e == null) return false;
            Lowered.Remove(e); var ct = ctl.CreateTime(e.Pid);
            return ct != 0 && ct == e.Created && ctl.SetPriority(e.Pid, e.Was) == null;
        }
        public static string Word(Prio p) { return p == Prio.BelowNormal ? "Below normal" : p == Prio.AboveNormal ? "Above normal" : p.ToString(); }
    }

    // Efficiency mode, like Windows 11 Task Manager's: EcoQoS (SetProcessInformation ProcessPowerThrottling, execution speed) plus
    // Idle priority. It stays until you turn it off or the program closes (like Windows'); off puts its priority back.
    public class EcoKeeper
    {
        public class Entry { public int Pid; public long Created; public string Name; public Prio Was; }
        readonly IProcessControl ctl; public readonly List<Entry> On = new List<Entry>();
        public EcoKeeper(IProcessControl c) { ctl = c; }
        public string Set(ProcInfo p, bool on)
        {
            var old = On.FirstOrDefault(x => x.Pid == p.Pid && x.Created == p.CreateTime);
            if (on)
            {
                var was = ctl.GetPriority(p.Pid); if (was == null) return "Windows won't let Hallow Tasks change " + p.Name + " (it may belong to another user or be protected).";
                var e = ctl.SetEfficiency(p.Pid, true); if (e != null) return e;
                if (old == null) On.Add(new Entry { Pid = p.Pid, Created = p.CreateTime, Name = p.Name, Was = was.Value == Prio.Idle ? Prio.Normal : was.Value });
                return null;
            }
            var err = ctl.SetEfficiency(p.Pid, false);
            if (err == null) ctl.SetPriority(p.Pid, old != null ? old.Was : Prio.Normal);
            On.RemoveAll(x => x.Pid == p.Pid);
            return err;
        }
        public bool IsOn(ProcInfo p) { return On.Any(x => x.Pid == p.Pid && x.Created == p.CreateTime) || ctl.GetEfficiency(p.Pid) == true; }
        public void Forget(Func<int, long> createTime) { On.RemoveAll(x => createTime(x.Pid) != x.Created); }
        public const string What = "Efficiency mode does what Windows 11 Task Manager's Efficiency mode does: Windows' EcoQoS (power throttling - it runs on the slower, cooler cores at lower clocks) plus Idle priority. It stays on until you turn it off or the app closes.";
    }

    // ---------------- the startup impact estimate (Hallow Toolkit's rule: RAM and CPU time per hour now, plus Windows' "slowed down startup" events)
    public static class StartupImpact
    {
        public static string Rate(bool running, bool enabled, long ramBytes, double cpuSecondsPerHour, bool slowEvent, out string why)
        {
            why = "";
            double mb = ramBytes / 1048576.0; string imp;
            if (running)
            {
                imp = slowEvent || mb >= 300 || cpuSecondsPerHour >= 120 ? "High" : mb >= 100 || cpuSecondsPerHour >= 20 ? "Medium" : "Low";
                why = mb.ToString("0") + " MB, " + cpuSecondsPerHour.ToString("0") + " s CPU per hour";
            }
            else if (!enabled) imp = "Off";
            else imp = slowEvent ? "High" : "Not running";
            if (slowEvent) why = (why.Length > 0 ? why + "; " : "") + "Windows says it slowed down startup";
            return imp;
        }
        public static int Rank(string impact) { switch (impact) { case "High": return 0; case "Medium": return 1; case "Low": return 2; case "Not running": return 3; default: return 4; } }
        // "C:\x\app.exe" --tray  ->  app ; rundll32-style commands give the first exe
        public static string ExeOf(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            var m = Regex.Match(command, @"([^\\/""]+)\.exe", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    // ---------------- number formats (monospace columns)
    public static class Fmt
    {
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        public static string Pct(double v) { return v < 0.05 ? "0%" : v < 10 ? v.ToString("0.0", Inv) + "%" : v.ToString("0", Inv) + "%"; }
        public static string Bytes(long b)
        {
            if (b < 0) return "-";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0", Inv) + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString(b < 100L * 1024 * 1024 ? "0.0" : "0", Inv) + " MB";
            return (b / 1073741824.0).ToString("0.0", Inv) + " GB";
        }
        public static string Rate(double bps)
        {
            if (bps < 0.5) return "0.0 MB/s";
            if (bps < 100 * 1024) return (bps / 1048576.0).ToString("0.0", Inv) + " MB/s";
            return (bps / 1048576.0).ToString(bps < 10 * 1048576.0 ? "0.0" : "0", Inv) + " MB/s";
        }
        public static string Bits(double bytesPerSec)
        {
            var b = bytesPerSec * 8;
            if (b < 1000 * 1000) return (b / 1000).ToString("0", Inv) + " Kbps";
            if (b < 1000.0 * 1000 * 1000) return (b / 1e6).ToString("0.0", Inv) + " Mbps";
            return (b / 1e9).ToString("0.00", Inv) + " Gbps";
        }
    }
}
