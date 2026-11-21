// Hallow Tasks - startup apps (the same sources as Hallow Guard's leftover hunt and Hallow Toolkit's startup manager): the Run keys
// (yours and all users'), the Startup folders, and whether Windows' Startup apps page has them switched off. Impact is Toolkit's
// estimate: what the app costs now (RAM, CPU time per hour since it started) plus Windows' "slowed down startup" events when
// Windows lets a normal user read them. Only YOUR Run entries (HKCU) can be disabled here - moved to Hallow Tasks' backup key, and
// Restore puts them back (like Hallow Guard). Everything else opens Windows' Startup apps page.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HallowTasks
{
    public class StartupItem
    {
        public string Name, Command, Where, Exe, Impact = "Not running", Why = "";
        public bool Enabled = true, Mine;      // Mine = in your own Run key (HKCU) - the only ones Hallow Tasks changes
        public long Ram; public bool Running;
    }
    public interface IStartupSource { List<StartupItem> Read(); HashSet<string> SlowExes(); }

    public class WindowsStartup : IStartupSource
    {
        public readonly string RunPath;
        public WindowsStartup(string runPath) { RunPath = runPath; }
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        public List<StartupItem> Read()
        {
            var list = new List<StartupItem>();
            var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            var keys = new[] {
                Tuple.Create(Registry.CurrentUser, RunPath, "Registry (you)", Registry.CurrentUser, "Run", true),
                Tuple.Create(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Registry (all users)", hklm, "Run", false),
                Tuple.Create(hklm, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Registry (all users, 32-bit)", hklm, "Run32", false) };
            foreach (var k in keys)
                try
                {
                    using (var key = k.Item1.OpenSubKey(k.Item2))
                    {
                        if (key == null) continue;
                        foreach (var n in key.GetValueNames())
                        {
                            if (n.Length == 0) continue; var cmd = key.GetValue(n) as string; if (cmd == null) continue;
                            list.Add(new StartupItem { Name = n, Command = cmd, Where = k.Item3, Exe = StartupImpact.ExeOf(cmd), Mine = k.Item6, Enabled = IsOn(k.Item4, k.Item5, n) });
                        }
                    }
                }
                catch { }
            foreach (var f in new[] { Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup folder (you)", Registry.CurrentUser), Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Startup folder (all users)", (RegistryKey)hklm) })
                try
                {
                    if (string.IsNullOrEmpty(f.Item1) || !Directory.Exists(f.Item1)) continue;
                    foreach (var file in Directory.GetFiles(f.Item1))
                    {
                        if (Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? Shortcut(file) : file;
                        list.Add(new StartupItem { Name = Path.GetFileNameWithoutExtension(file), Command = target ?? file, Where = f.Item2, Exe = StartupImpact.ExeOf(target ?? file) ?? Path.GetFileNameWithoutExtension(file), Enabled = IsOn(f.Item3, "StartupFolder", Path.GetFileName(file)) });
                    }
                }
                catch { }
            return list;
        }
        // Windows' Startup apps switch: StartupApproved\<Run|Run32|StartupFolder>\<name>, first byte even = on (no value = on)
        static bool IsOn(RegistryKey root, string sub, string name)
        {
            try { using (var k = root.OpenSubKey(Approved + sub)) { var v = k == null ? null : k.GetValue(name) as byte[]; return v == null || v.Length == 0 || v[0] % 2 == 0; } }
            catch { return true; }
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
        // exes Windows says slowed down startup (last 30 days). Windows may only show this log to administrators: then it's empty.
        public HashSet<string> SlowExes()
        {
            var r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var q = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational", PathType.LogName,
                    "*[System[(EventID=101 or EventID=102 or EventID=103 or EventID=106 or EventID=109) and TimeCreated[timediff(@SystemTime) <= 2592000000]]]");
                using (var rd = new EventLogReader(q))
                    for (int i = 0; i < 500; i++)
                    {
                        using (var e = rd.ReadEvent())
                        {
                            if (e == null) break;
                            foreach (var p in e.Properties) { var exe = StartupImpact.ExeOf(p.Value as string); if (exe != null) r.Add(exe); }
                        }
                    }
            }
            catch { }
            return r;
        }
    }

    // made-up startup apps for --snap pictures and the test copy
    public class FakeStartup : IStartupSource
    {
        public List<StartupItem> Read()
        {
            return new List<StartupItem> {
                new StartupItem { Name = "Discord", Command = "\"C:\\Users\\Alex\\AppData\\Local\\Discord\\Update.exe\" --processStart Discord.exe", Where = "Registry (you)", Exe = "Discord", Mine = true },
                new StartupItem { Name = "Steam", Command = "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", Where = "Registry (you)", Exe = "steam", Mine = true },
                new StartupItem { Name = "Spotify", Command = "\"C:\\Users\\Alex\\AppData\\Roaming\\Spotify\\Spotify.exe\" --autostart --minimized", Where = "Registry (you)", Exe = "Spotify", Mine = true },
                new StartupItem { Name = "HallowHub", Command = "\"C:\\Users\\Alex\\AppData\\Local\\Programs\\Hallow Hub\\HallowHub.exe\" --tray", Where = "Registry (you)", Exe = "HallowHub", Mine = true },
                new StartupItem { Name = "EpicGamesLauncher", Command = "\"C:\\Program Files (x86)\\Epic Games\\Launcher\\Portal\\Binaries\\Win32\\EpicGamesLauncher.exe\" -silent", Where = "Registry (you)", Exe = "EpicGamesLauncher", Mine = true, Enabled = false },
                new StartupItem { Name = "SecurityHealth", Command = "%windir%\\system32\\SecurityHealthSystray.exe", Where = "Registry (all users)", Exe = "SecurityHealthSystray" },
                new StartupItem { Name = "RtkAudUService", Command = "\"C:\\Windows\\System32\\DriverStore\\FileRepository\\realtek\\RtkAudUService64.exe\" -background", Where = "Registry (all users)", Exe = "RtkAudUService64" },
                new StartupItem { Name = "Wallpaper Engine", Command = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\wallpaper_engine\\wallpaper64.exe", Where = "Startup folder (you)", Exe = "wallpaper64" } };
        }
        public HashSet<string> SlowExes() { return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EpicGamesLauncher" }; }
    }

    public static class StartupRate
    {
        // impact for each item from the newest sample's processes (RAM now, CPU time per hour since it started)
        public static void Apply(List<StartupItem> items, IList<ProcInfo> procs, HashSet<string> slow, DateTime now, Func<ProcInfo, double> cpuSeconds)
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
                i.Running = ps.Count > 0; i.Ram = ps.Sum(p => p.Ram);
                string why; i.Impact = StartupImpact.Rate(i.Running, i.Enabled, i.Ram, cpu / hours, exe != null && slow != null && slow.Contains(exe), out why); i.Why = why;
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
