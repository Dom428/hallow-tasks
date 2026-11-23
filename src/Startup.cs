// Hallow Tasks - startup apps: the Run keys (yours and all users'), the Startup folders, and (1.4) your scheduled tasks that run when
// you sign in, where Windows lets you read them. Impact is an estimate: what the app costs now (RAM, CPU time per hour since it
// started), Windows' own StartupApproved switch (on / off, and since when), and the time Windows measured at boot (the
// Diagnostics-Performance log - read-only, and only when Windows shows it to you; often admin-only).
// 1.4: Disable / Enable work exactly like Task Manager's: Windows' StartupApproved value for the entry (02 = on, 03 + the time = off)
// in YOUR part of the registry - reversible, nothing is deleted or moved. Entries for all users show here and open Windows' Startup
// apps page (they need admin). Tests and the test copy use a fake registry root and temp folders (StartupPaths.ForTest) - never the
// real startup entries. 1.1-1.3's "moved to a backup" entries can still be restored (StartupStore below).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HallowTasks
{
    public class StartupItem
    {
        public string Name, Command, Where, Exe, Impact = "Not running", Why = "";
        public bool Enabled = true, Mine;      // Mine = yours (HKCU Run or your Startup folder): Disable / Enable here, like Task Manager
        public string Kind = "run";            // run, folder, task
        public string Approved, ApprovedName;  // StartupApproved\<Approved>\<ApprovedName> (Run, Run32, StartupFolder)
        public DateTime? OffSince;             // when it was switched off (from StartupApproved)
        public int BootMs = -1;                // what Windows measured at boot, ms (-1 = not known)
        public long Ram; public bool Running;
    }
    public interface IStartupSource
    {
        List<StartupItem> Read();
        HashSet<string> SlowExes();
        Dictionary<string, int> BootTimes();      // exe (bare) -> ms Windows measured at boot (0 = flagged, no time)
        string SetOn(StartupItem item, bool on);  // null = done
    }

    // where startup entries live: the real places, or a fake registry root and temp folders for tests
    public class StartupPaths
    {
        public RegistryKey UserHive, MachineHive;
        public string UserRun, MachineRun, MachineRun32, UserApproved, MachineApproved, UserFolder, CommonFolder;
        public bool Tasks;
        public const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        public static StartupPaths Real()
        {
            return new StartupPaths {
                UserHive = Registry.CurrentUser, MachineHive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
                UserRun = StartupStore.UserRun, MachineRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", MachineRun32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                UserApproved = ApprovedPath, MachineApproved = ApprovedPath,
                UserFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup), CommonFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Tasks = true };
        }
        // everything under HKCU\<root> and <folders>: "HKLM" is a fake key under HKCU too, so tests never need (or touch) the real one
        public static StartupPaths ForTest(string root, string folders)
        {
            return new StartupPaths {
                UserHive = Registry.CurrentUser, MachineHive = Registry.CurrentUser,
                UserRun = root + @"\HKCU\Run", MachineRun = root + @"\HKLM\Run", MachineRun32 = root + @"\HKLM\Run32",
                UserApproved = root + @"\HKCU\StartupApproved\", MachineApproved = root + @"\HKLM\StartupApproved\",
                UserFolder = Path.Combine(folders, "Startup"), CommonFolder = Path.Combine(folders, "CommonStartup"), Tasks = false };
        }
    }

    // Windows' StartupApproved switch - what Task Manager's and Settings' Startup apps change
    public static class Approval
    {
        public static byte[] On() { return new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; }
        public static byte[] Off(DateTime utc) { var b = new byte[12]; b[0] = 3; Array.Copy(BitConverter.GetBytes(utc.ToFileTimeUtc()), 0, b, 4, 8); return b; }
        // no value = on; first byte even = on, odd = off (then bytes 4-11 are when, as a FILETIME)
        public static bool IsOn(byte[] v, out DateTime? since)
        {
            since = null;
            if (v == null || v.Length == 0 || v[0] % 2 == 0) return true;
            if (v.Length >= 12) { var ft = BitConverter.ToInt64(v, 4); if (ft > 0) try { since = DateTime.FromFileTimeUtc(ft).ToLocalTime(); } catch { } }
            return false;
        }
        public static bool Read(RegistryKey hive, string approved, string sub, string name, out DateTime? since)
        {
            since = null;
            try { using (var k = hive.OpenSubKey(approved + sub)) return IsOn(k == null ? null : k.GetValue(name) as byte[], out since); }
            catch { return true; }
        }
        public static string Write(RegistryKey hive, string approved, string sub, string name, bool on)
        {
            try { using (var k = hive.CreateSubKey(approved + sub)) k.SetValue(name, on ? On() : Off(DateTime.UtcNow), RegistryValueKind.Binary); return null; }
            catch (Exception e) { return "Windows didn't let Hallow Tasks change it: " + e.Message; }
        }
    }

    public class WindowsStartup : IStartupSource
    {
        public readonly StartupPaths P;
        public WindowsStartup(StartupPaths paths) { P = paths; }
        public List<StartupItem> Read()
        {
            var list = new List<StartupItem>();
            var keys = new[] {
                Tuple.Create(P.UserHive, P.UserRun, "Registry (you)", P.UserApproved, "Run", true),
                Tuple.Create(P.MachineHive, P.MachineRun, "Registry (all users)", P.MachineApproved, "Run", false),
                Tuple.Create(P.MachineHive, P.MachineRun32, "Registry (all users, 32-bit)", P.MachineApproved, "Run32", false) };
            foreach (var k in keys)
                try
                {
                    using (var key = k.Item1.OpenSubKey(k.Item2))
                    {
                        if (key == null) continue;
                        foreach (var n in key.GetValueNames())
                        {
                            if (n.Length == 0) continue; var cmd = key.GetValue(n) as string; if (cmd == null) continue;
                            DateTime? since; var on = Approval.Read(k.Item1, k.Item4, k.Item5, n, out since);
                            list.Add(new StartupItem { Name = n, Command = cmd, Where = k.Item3, Exe = StartupImpact.ExeOf(cmd), Mine = k.Item6, Enabled = on, OffSince = since, Kind = "run", Approved = k.Item5, ApprovedName = n });
                        }
                    }
                }
                catch { }
            foreach (var f in new[] { Tuple.Create(P.UserFolder, "Startup folder (you)", P.UserHive, P.UserApproved, true), Tuple.Create(P.CommonFolder, "Startup folder (all users)", P.MachineHive, P.MachineApproved, false) })
                try
                {
                    if (string.IsNullOrEmpty(f.Item1) || !Directory.Exists(f.Item1)) continue;
                    foreach (var file in Directory.GetFiles(f.Item1))
                    {
                        var leaf = Path.GetFileName(file);
                        if (leaf.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? Shortcut(file) : file;
                        DateTime? since; var on = Approval.Read(f.Item3, f.Item4, "StartupFolder", leaf, out since);
                        list.Add(new StartupItem { Name = Path.GetFileNameWithoutExtension(file), Command = target ?? file, Where = f.Item2, Exe = StartupImpact.ExeOf(target ?? file) ?? Path.GetFileNameWithoutExtension(file), Enabled = on, OffSince = since, Mine = f.Item5, Kind = "folder", Approved = "StartupFolder", ApprovedName = leaf });
                    }
                }
                catch { }
            if (P.Tasks) try { list.AddRange(LogonTasks.Read()); } catch { }
            return list;
        }
        // Disable / Enable, Task Manager's way: only yours (HKCU), only the StartupApproved value - the entry itself is never touched
        public string SetOn(StartupItem i, bool on)
        {
            if (i == null || !i.Mine || i.Kind == "task" || string.IsNullOrEmpty(i.Approved) || string.IsNullOrEmpty(i.ApprovedName)) return "Only your own startup entries can be switched here.";
            return Approval.Write(P.UserHive, P.UserApproved, i.Approved, i.ApprovedName, on);
        }
        static string Shortcut(string lnk)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell"); var sh = Activator.CreateInstance(t);
                try { var sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk }); return sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string; }
                finally { Marshal.FinalReleaseComObject(sh); }
            }
            catch { return null; }
        }
        public HashSet<string> SlowExes() { return new HashSet<string>(BootTimes().Keys, StringComparer.OrdinalIgnoreCase); }
        // what Windows measured at boot (last 30 days): Diagnostics-Performance events 101 ("this app slowed down startup": Name,
        // TotalTime, DegradationTime). Read-only. Windows often shows this log only to administrators: then it's empty.
        public Dictionary<string, int> BootTimes()
        {
            var r = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!P.Tasks) return r;   // the test paths: no real event log
            try
            {
                var q = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational", PathType.LogName,
                    "*[System[(EventID=101 or EventID=102 or EventID=103 or EventID=106 or EventID=109) and TimeCreated[timediff(@SystemTime) <= 2592000000]]]");
                using (var rd = new EventLogReader(q))
                    for (int i = 0; i < 500; i++)
                        using (var e = rd.ReadEvent())
                        {
                            if (e == null) break;
                            string exe; int ms; if (BootEvent.Parse(e.ToXml(), out exe, out ms)) { int old; r[exe] = r.TryGetValue(exe, out old) ? Math.Max(old, ms) : ms; }
                        }
            }
            catch { }
            return r;
        }
    }

    // one Diagnostics-Performance event's XML -> the exe and the time it took at boot
    public static class BootEvent
    {
        public static bool Parse(string xml, out string exe, out int ms)
        {
            exe = null; ms = 0; if (string.IsNullOrEmpty(xml)) return false;
            Func<string, string> data = n => { var m = Regex.Match(xml, "<Data Name=['\"]" + n + "['\"]>([^<]*)</Data>"); return m.Success ? m.Groups[1].Value.Trim() : null; };
            var name = data("Name") ?? data("FileName") ?? data("FriendlyName");
            exe = StartupImpact.ExeOf(name) ?? (string.IsNullOrEmpty(name) ? null : Known.Bare(Path.GetFileName(name)));
            if (string.IsNullOrEmpty(exe)) return false;
            int t; if (int.TryParse(data("TotalTime"), out t) || int.TryParse(data("DegradationTime"), out t)) ms = Math.Max(0, t);
            exe = Known.Bare(exe); return true;
        }
    }

    // your scheduled tasks that run when you sign in (Task Scheduler, read-only; Windows' own \Microsoft tasks left out)
    public static class LogonTasks
    {
        static object Get(object o, string n) { return o.GetType().InvokeMember(n, BindingFlags.GetProperty, null, o, null); }
        static object Call(object o, string n, params object[] a) { return o.GetType().InvokeMember(n, BindingFlags.InvokeMethod, null, o, a); }
        static IEnumerable<object> Items(object coll) { int n = Convert.ToInt32(Get(coll, "Count")); for (int i = 1; i <= n; i++) yield return coll.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, coll, new object[] { i }); }
        public static List<StartupItem> Read()
        {
            var r = new List<StartupItem>();
            var t = Type.GetTypeFromProgID("Schedule.Service"); if (t == null) return r;
            var svc = Activator.CreateInstance(t);
            try { Call(svc, "Connect"); Walk(Call(svc, "GetFolder", "\\"), r, 0); }
            finally { Marshal.FinalReleaseComObject(svc); }
            return r;
        }
        static void Walk(object folder, List<StartupItem> r, int depth)
        {
            if (depth > 4 || r.Count > 200) return;
            var path = Get(folder, "Path") as string ?? "\\";
            if (path.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)) return;
            var me = Environment.UserName; var meFull = Environment.UserDomainName + "\\" + me;
            try
            {
                foreach (var task in Items(Call(folder, "GetTasks", 1)))
                    try
                    {
                        var def = Get(task, "Definition"); bool logon = false, mine = false;
                        foreach (var tr in Items(Get(def, "Triggers")))
                            if (Convert.ToInt32(Get(tr, "Type")) == 9) { logon = true; var u = Get(tr, "UserId") as string; if (Same(u, me, meFull)) mine = true; }
                        if (!logon) continue;
                        var pu = Get(Get(def, "Principal"), "UserId") as string; if (Same(pu, me, meFull)) mine = true;
                        string exe = null, args = null;
                        foreach (var a in Items(Get(def, "Actions"))) if (Convert.ToInt32(Get(a, "Type")) == 0) { exe = Get(a, "Path") as string; args = Get(a, "Arguments") as string; break; }
                        if (exe == null) continue;
                        r.Add(new StartupItem { Name = Get(task, "Name") as string, Command = (exe.Contains(" ") && !exe.StartsWith("\"") ? "\"" + exe + "\"" : exe) + (string.IsNullOrEmpty(args) ? "" : " " + args),
                            Where = mine ? "Scheduled task (you)" : "Scheduled task (all users)", Exe = StartupImpact.ExeOf(exe) ?? Path.GetFileNameWithoutExtension(exe), Enabled = (bool)Get(task, "Enabled"), Kind = "task" });
                    }
                    catch { }
                foreach (var sub in Items(Call(folder, "GetFolders", 0))) Walk(sub, r, depth + 1);
            }
            catch { }
        }
        static bool Same(string u, string me, string meFull) { return !string.IsNullOrEmpty(u) && (u.Equals(me, StringComparison.OrdinalIgnoreCase) || u.Equals(meFull, StringComparison.OrdinalIgnoreCase)); }
    }

    // made-up startup apps for --snap pictures and the test copy (switching them only changes this list)
    public class FakeStartup : IStartupSource
    {
        readonly List<StartupItem> items;
        public FakeStartup()
        {
            items = new List<StartupItem> {
                new StartupItem { Name = "Discord", Command = "\"C:\\Users\\Alex\\AppData\\Local\\Discord\\Update.exe\" --processStart Discord.exe", Where = "Registry (you)", Exe = "Discord", Mine = true },
                new StartupItem { Name = "Steam", Command = "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", Where = "Registry (you)", Exe = "steam", Mine = true },
                new StartupItem { Name = "Spotify", Command = "\"C:\\Users\\Alex\\AppData\\Roaming\\Spotify\\Spotify.exe\" --autostart --minimized", Where = "Registry (you)", Exe = "Spotify", Mine = true },
                new StartupItem { Name = "HallowHub", Command = "\"C:\\Users\\Alex\\AppData\\Local\\Programs\\Hallow Hub\\HallowHub.exe\" --tray", Where = "Registry (you)", Exe = "HallowHub", Mine = true },
                new StartupItem { Name = "EpicGamesLauncher", Command = "\"C:\\Program Files (x86)\\Epic Games\\Launcher\\Portal\\Binaries\\Win32\\EpicGamesLauncher.exe\" -silent", Where = "Registry (you)", Exe = "EpicGamesLauncher", Mine = true, Enabled = false, OffSince = DateTime.Now.AddDays(-12) },
                new StartupItem { Name = "SecurityHealth", Command = "%windir%\\system32\\SecurityHealthSystray.exe", Where = "Registry (all users)", Exe = "SecurityHealthSystray" },
                new StartupItem { Name = "RtkAudUService", Command = "\"C:\\Windows\\System32\\DriverStore\\FileRepository\\realtek\\RtkAudUService64.exe\" -background", Where = "Registry (all users)", Exe = "RtkAudUService64" },
                new StartupItem { Name = "Wallpaper Engine", Command = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\wallpaper_engine\\wallpaper64.exe", Where = "Startup folder (you)", Exe = "wallpaper64", Mine = true, Kind = "folder" },
                new StartupItem { Name = "OneDrive Updater", Command = "\"C:\\Users\\Alex\\AppData\\Local\\Microsoft\\OneDrive\\OneDriveStandaloneUpdater.exe\" /reporting", Where = "Scheduled task (you)", Exe = "OneDriveStandaloneUpdater", Kind = "task" } };
            foreach (var i in items) if (i.Kind != "task") { i.Approved = i.Kind == "folder" ? "StartupFolder" : "Run"; i.ApprovedName = i.Name; }
        }
        public List<StartupItem> Read() { return items.Select(Copy).ToList(); }
        static StartupItem Copy(StartupItem i) { return new StartupItem { Name = i.Name, Command = i.Command, Where = i.Where, Exe = i.Exe, Enabled = i.Enabled, Mine = i.Mine, Kind = i.Kind, Approved = i.Approved, ApprovedName = i.ApprovedName, OffSince = i.OffSince }; }
        public HashSet<string> SlowExes() { return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EpicGamesLauncher" }; }
        public Dictionary<string, int> BootTimes() { return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "epicgameslauncher", 4200 }, { "discord", 1900 }, { "steam", 2600 } }; }
        public string SetOn(StartupItem i, bool on)
        {
            var it = items.FirstOrDefault(x => x.Name == i.Name && x.Where == i.Where); if (it == null || !it.Mine) return "Only your own startup entries can be switched here.";
            it.Enabled = on; it.OffSince = on ? (DateTime?)null : DateTime.Now; return null;
        }
    }

    public static class StartupRate
    {
        // impact for each item from the newest sample's processes (RAM now, CPU time per hour since it started) and Windows' boot times
        public static void Apply(List<StartupItem> items, IList<ProcInfo> procs, HashSet<string> slow, DateTime now, Func<ProcInfo, double> cpuSeconds)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); if (slow != null) foreach (var s in slow) d[Known.Bare(s)] = 0;
            Apply(items, procs, d, now, cpuSeconds);
        }
        public static void Apply(List<StartupItem> items, IList<ProcInfo> procs, Dictionary<string, int> boot, DateTime now, Func<ProcInfo, double> cpuSeconds)
        {
            foreach (var i in items)
            {
                var exe = i.Exe == null ? null : Known.Bare(i.Exe);
                var ps = exe == null ? new List<ProcInfo>() : procs.Where(p => Known.Bare(p.Name) == exe).ToList();
                double cpu = 0, hours = 0.25;
                if (ps.Count > 0)
                {
                    cpu = ps.Sum(p => cpuSeconds(p));
                    var first = DateTime.FromFileTime(ps.Min(p => p.CreateTime));
                    hours = Math.Max(0.25, (now - first).TotalHours);
                }
                int ms = -1; bool slowEvent = exe != null && boot != null && boot.TryGetValue(exe, out ms);
                i.BootMs = slowEvent ? ms : -1;
                i.Running = ps.Count > 0; i.Ram = ps.Sum(p => p.Ram);
                string why; i.Impact = StartupImpact.Rate(i.Running, i.Enabled, i.Ram, cpu / hours, slowEvent, out why);
                if (slowEvent && ms > 0) why = why.Replace("Windows says it slowed down startup", "Windows measured " + StartupImpact.Secs(ms) + " at startup");
                i.Why = why;
            }
        }
    }

    // "Disable" for a startup entry in your own Run key: the value moves to Hallow Tasks' backup key, "Restore" moves it back
    // (Hallow Guard's StartupStore). Tests use their own fake keys (ForTest) and never the real Run key.
    public class DisabledEntry { public string Id, Name, Data, From, When; }
    public class StartupStore
    {
        public const string UserRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public readonly string RunPath, BackupPath;
        public StartupStore(string runPath, string backupPath) { RunPath = runPath; BackupPath = backupPath; }
        public static StartupStore Real() { return new StartupStore(UserRun, @"Software\HallowTasks\DisabledStartup"); }
        public static StartupStore ForTest(string root) { return new StartupStore(root + @"\Run", root + @"\DisabledStartup"); }

        public string Disable(string valueName)
        {
            try
            {
                using (var run = Registry.CurrentUser.OpenSubKey(RunPath, true))
                {
                    if (run == null || run.GetValue(valueName) == null) return "That startup entry isn't there any more.";
                    var kind = run.GetValueKind(valueName);
                    var data = run.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (data == null) return "That entry isn't a normal startup command.";
                    using (var bk = Registry.CurrentUser.CreateSubKey(BackupPath))
                    {
                        var id = Safe(valueName); for (int i = 2; bk.OpenSubKey(id) != null; i++) id = Safe(valueName) + " (" + i + ")";
                        using (var e = bk.CreateSubKey(id))
                        {
                            e.SetValue("Name", valueName); e.SetValue("Data", data, kind == RegistryValueKind.ExpandString ? RegistryValueKind.ExpandString : RegistryValueKind.String);
                            e.SetValue("From", "HKCU\\" + RunPath); e.SetValue("When", DateTime.Now.ToString("o"));
                        }
                    }
                    run.DeleteValue(valueName);   // only after the backup is written
                }
                return null;
            }
            catch (Exception e) { return "Couldn't disable it: " + e.Message; }
        }
        public string Restore(string id)
        {
            try
            {
                using (var bk = Registry.CurrentUser.OpenSubKey(BackupPath, true))
                using (var e = bk == null ? null : bk.OpenSubKey(id))
                {
                    if (e == null) return "That entry isn't in the backup.";
                    var name = e.GetValue("Name") as string; var data = e.GetValue("Data", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (string.IsNullOrEmpty(name) || data == null) return "The backup of that entry is damaged.";
                    var kind = e.GetValueKind("Data");
                    using (var run = Registry.CurrentUser.CreateSubKey(RunPath))
                    {
                        if (run.GetValue(name) != null) return "There's already a startup entry called " + name + ".";
                        run.SetValue(name, data, kind == RegistryValueKind.ExpandString ? RegistryValueKind.ExpandString : RegistryValueKind.String);
                    }
                    e.Close(); bk.DeleteSubKeyTree(id);
                }
                return null;
            }
            catch (Exception e) { return "Couldn't restore it: " + e.Message; }
        }
        public List<DisabledEntry> List()
        {
            var r = new List<DisabledEntry>();
            try
            {
                using (var bk = Registry.CurrentUser.OpenSubKey(BackupPath))
                    if (bk != null) foreach (var id in bk.GetSubKeyNames()) using (var e = bk.OpenSubKey(id))
                        r.Add(new DisabledEntry { Id = id, Name = e.GetValue("Name") as string, Data = e.GetValue("Data", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string, From = e.GetValue("From") as string, When = e.GetValue("When") as string });
            }
            catch { }
            return r;
        }
        static string Safe(string s) { var t = new string((s ?? "").Select(c => c == '\\' || char.IsControl(c) ? '_' : c).ToArray()).Trim(); return t.Length == 0 ? "entry" : t.Length > 200 ? t.Substring(0, 200) : t; }
    }
}
