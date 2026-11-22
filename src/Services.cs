// Hallow Tasks - the Services tab: every Windows service with its status, startup type, PID and description (EnumServicesStatusEx
// with the process ids in one call; startup type, program and description from the service's registry key - all readable without
// admin). Start / Stop / Restart where Windows lets you (OpenService with those rights answers that without doing anything);
// otherwise "needs admin": a button that starts "HallowTasks.exe --svc <start|stop|restart> <name>" elevated, so Windows asks you
// (UAC) first - Hallow Tasks never asks for admin by itself. Microsoft's services are marked "leave alone" (no buttons) unless you
// switch that off in Settings. Tests use a fake service list and controller: no real service is ever started or stopped in tests.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace HallowTasks
{
    public class ServiceInfo
    {
        public string Name, Display, Status = "Stopped", StartType = "Manual", Description, Program; public int Pid;
        public bool Microsoft, CanControl, CanStop = true;
        public bool Running { get { return Status == "Running"; } }
    }
    public interface IServiceSource
    {
        List<ServiceInfo> List();
        string Start(string name); string Stop(string name); string Restart(string name);   // null = done
    }

    public static class ServiceRules
    {
        // a service name that's safe to pass on a command line (Windows allows almost anything, but never quotes or slashes in practice)
        public static bool ValidName(string n) { return !string.IsNullOrEmpty(n) && n.Length <= 256 && Regex.IsMatch(n, @"^[A-Za-z0-9_.\-\$@ {}#+]+$"); }
        public static bool ValidOp(string op) { return op == "start" || op == "stop" || op == "restart"; }
        public static bool IsMicrosoft(string program, string company)
        {
            if (!string.IsNullOrEmpty(company) && company.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.IsNullOrEmpty(program)) return true;   // no program Windows shows = a Windows one
            var p = program.ToLowerInvariant();
            var win = (Environment.GetFolderPath(Environment.SpecialFolder.Windows) ?? @"C:\Windows").ToLowerInvariant().TrimEnd('\\') + "\\";
            return p.Contains(win) || p.Contains(@"%systemroot%") || p.Contains(@"%windir%") || p.StartsWith(@"system32\") || p.StartsWith(@"\systemroot\") || p.Contains(@"\microsoft\") || p.Contains(@"\microsoft shared\") || p.Contains(@"\windows defender\");
        }
        public static string StartWord(int start, bool delayed, bool trigger)
        {
            var s = start == 2 ? (delayed ? "Automatic (delayed)" : "Automatic") : start == 3 ? "Manual" : start == 4 ? "Disabled" : start == 0 ? "Boot" : start == 1 ? "System" : "Unknown";
            return trigger && (start == 2 || start == 3) ? s + " (trigger)" : s;
        }
        public static string StateWord(int st) { switch (st) { case 1: return "Stopped"; case 2: return "Starting"; case 3: return "Stopping"; case 4: return "Running"; case 5: return "Resuming"; case 6: return "Pausing"; case 7: return "Paused"; default: return "Unknown"; } }
        // the elevated helper's command line ("--svc stop Spooler")
        public static string HelperArgs(string op, string name)
        {
            if (!ValidOp(op) || !ValidName(name)) throw new ArgumentException("bad service request");
            return "--svc " + op + " \"" + name + "\"";
        }
    }

    public class WindowsServices : IServiceSource
    {
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr OpenSCManagerW(string machine, string db, int access);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr OpenServiceW(IntPtr scm, string name, int access);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool CloseServiceHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool EnumServicesStatusExW(IntPtr scm, int level, int type, int state, IntPtr buf, int size, out int needed, out int count, ref int resume, string group);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool StartServiceW(IntPtr h, int n, IntPtr args);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool ControlService(IntPtr h, int code, byte[] status);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool QueryServiceStatus(IntPtr h, byte[] status);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int SHLoadIndirectString(string src, StringBuilder buf, int size, IntPtr reserved);
        const int SC_CONNECT = 0x1, SC_ENUM = 0x4, SVC_QUERY = 0x4, SVC_START = 0x10, SVC_STOP = 0x20;

        public List<ServiceInfo> List()
        {
            var r = new List<ServiceInfo>();
            var scm = OpenSCManagerW(null, null, SC_CONNECT | SC_ENUM); if (scm == IntPtr.Zero) return r;
            try
            {
                int needed, count, resume = 0; EnumServicesStatusExW(scm, 0, 0x30, 3, IntPtr.Zero, 0, out needed, out count, ref resume, null);
                var buf = Marshal.AllocHGlobal(needed + 4096);
                try
                {
                    resume = 0;
                    if (!EnumServicesStatusExW(scm, 0, 0x30, 3, buf, needed + 4096, out needed, out count, ref resume, null)) return r;
                    int sz = IntPtr.Size == 8 ? 56 : 44, so = IntPtr.Size * 2;   // ENUM_SERVICE_STATUS_PROCESSW
                    for (int i = 0; i < count; i++)
                    {
                        var e = buf + i * sz;
                        var s = new ServiceInfo { Name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(e, 0)), Display = Marshal.PtrToStringUni(Marshal.ReadIntPtr(e, IntPtr.Size)) };
                        int state = Marshal.ReadInt32(e, so + 4), accepted = Marshal.ReadInt32(e, so + 8);
                        s.Status = ServiceRules.StateWord(state); s.Pid = Marshal.ReadInt32(e, so + 28); s.CanStop = (accepted & 1) != 0 || state != 4;
                        r.Add(s);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
                foreach (var s in r)
                {
                    try
                    {
                        using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + s.Name))
                            if (k != null)
                            {
                                var start = k.GetValue("Start"); var del = k.GetValue("DelayedAutostart"); bool trig; using (var t = k.OpenSubKey("TriggerInfo")) trig = t != null && t.SubKeyCount > 0;
                                s.StartType = ServiceRules.StartWord(start is int ? (int)start : -1, del is int && (int)del == 1, trig);
                                s.Program = k.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                                s.Description = Indirect(k.GetValue("Description") as string);
                                if (s.Display != null && s.Display.StartsWith("@")) s.Display = Indirect(s.Display);
                            }
                    }
                    catch { }
                    s.Microsoft = ServiceRules.IsMicrosoft(s.Program, null);
                    var h = OpenServiceW(scm, s.Name, SVC_QUERY | SVC_START | SVC_STOP);
                    s.CanControl = h != IntPtr.Zero; if (h != IntPtr.Zero) CloseServiceHandle(h);
                }
            }
            finally { CloseServiceHandle(scm); }
            return r.OrderBy(x => x.Display ?? x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        static string Indirect(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith("@")) return s;
            try { var sb = new StringBuilder(1024); return SHLoadIndirectString(s, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null; } catch { return null; }
        }
        static string Err(int e) { return e == 5 ? "needs admin" : e == 1056 ? "It's already running." : e == 1062 ? "It isn't running." : e == 1058 ? "It's disabled - Windows' Services console can change its startup type." : e == 1051 ? "Other running services depend on it - stop those first." : e == 1053 ? "It didn't answer in time." : new Win32Exception(e).Message; }
        string Do(string name, Func<IntPtr, string> act, int access)
        {
            if (!ServiceRules.ValidName(name)) return "That isn't a service name.";
            var scm = OpenSCManagerW(null, null, SC_CONNECT); if (scm == IntPtr.Zero) return Err(Marshal.GetLastWin32Error());
            try { var h = OpenServiceW(scm, name, access | SVC_QUERY); if (h == IntPtr.Zero) return Err(Marshal.GetLastWin32Error()); try { return act(h); } finally { CloseServiceHandle(h); } }
            finally { CloseServiceHandle(scm); }
        }
        static int State(IntPtr h) { var b = new byte[36]; return QueryServiceStatus(h, b) ? BitConverter.ToInt32(b, 4) : 0; }
        static bool WaitFor(IntPtr h, int state, int ms) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms) { if (State(h) == state) return true; Thread.Sleep(250); } return State(h) == state; }
        public string Start(string name) { return Do(name, h => StartServiceW(h, 0, IntPtr.Zero) ? (WaitFor(h, 4, 20000) ? null : "It's starting but hasn't said it's running yet.") : Err(Marshal.GetLastWin32Error()), SVC_START); }
        public string Stop(string name) { return Do(name, h => ControlService(h, 1, new byte[36]) ? (WaitFor(h, 1, 20000) ? null : "It's stopping but hasn't stopped yet.") : Err(Marshal.GetLastWin32Error()), SVC_STOP); }
        public string Restart(string name)
        {
            return Do(name, h =>
            {
                if (State(h) == 4) { if (!ControlService(h, 1, new byte[36])) return Err(Marshal.GetLastWin32Error()); if (!WaitFor(h, 1, 20000)) return "It didn't stop in time, so it wasn't started again."; }
                return StartServiceW(h, 0, IntPtr.Zero) ? (WaitFor(h, 4, 20000) ? null : "It's starting but hasn't said it's running yet.") : Err(Marshal.GetLastWin32Error());
            }, SVC_START | SVC_STOP);
        }

        // the elevated helper: "HallowTasks.exe --svc <op> <name>" - run by Windows after you said yes to UAC. Exit 0 = done.
        public static int RunHelper(string[] args, int at)
        {
            if (at < 0 || at + 1 >= args.Length) return 2;
            var op = args[at]; var name = args[at + 1];
            if (!ServiceRules.ValidOp(op) || !ServiceRules.ValidName(name)) return 2;
            var s = new WindowsServices(); var e = op == "start" ? s.Start(name) : op == "stop" ? s.Stop(name) : s.Restart(name);
            return e == null ? 0 : 1;
        }
        // asks Windows to run the helper elevated (UAC shows; you decide). Returns null when it ran and worked.
        public static string Elevated(string op, string name)
        {
            try
            {
                var psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, ServiceRules.HelperArgs(op, name)) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return "Windows didn't start it.";
                    if (!p.WaitForExit(60000)) return "It's taking long - check again in a moment.";
                    return p.ExitCode == 0 ? null : "Windows couldn't " + op + " it (code " + p.ExitCode + ").";
                }
            }
            catch (Win32Exception e) { return e.NativeErrorCode == 1223 ? "You said no in the Windows prompt, so nothing changed." : e.Message; }
            catch (Exception e) { return e.Message; }
        }
    }

    // made-up services for --snap and tests; the controller only remembers what it was asked
    public class FakeServices : IServiceSource
    {
        public readonly List<string> Calls = new List<string>();
        readonly List<ServiceInfo> list = new List<ServiceInfo> {
            new ServiceInfo { Name = "AudioSrv", Display = "Windows Audio", Status = "Running", StartType = "Automatic", Pid = 2412, Description = "Manages audio for Windows-based programs.", Program = @"C:\Windows\System32\svchost.exe -k LocalServiceNetworkRestricted -p", Microsoft = true },
            new ServiceInfo { Name = "BITS", Display = "Background Intelligent Transfer Service", Status = "Stopped", StartType = "Manual", Description = "Transfers files in the background using idle network bandwidth.", Program = @"C:\Windows\System32\svchost.exe -k netsvcs -p", Microsoft = true },
            new ServiceInfo { Name = "Spooler", Display = "Print Spooler", Status = "Running", StartType = "Automatic", Pid = 3180, Description = "Spools print jobs and handles interaction with the printer.", Program = @"C:\Windows\System32\spoolsv.exe", Microsoft = true },
            new ServiceInfo { Name = "wuauserv", Display = "Windows Update", Status = "Running", StartType = "Manual (trigger)", Pid = 5512, Description = "Enables the detection, download and installation of updates for Windows and other programs.", Program = @"C:\Windows\system32\svchost.exe -k netsvcs -p", Microsoft = true },
            new ServiceInfo { Name = "WSearch", Display = "Windows Search", Status = "Running", StartType = "Automatic (delayed)", Pid = 7100, Description = "Provides content indexing, property caching and search results for files, e-mail and other content.", Program = @"C:\Windows\system32\SearchIndexer.exe /Embedding", Microsoft = true },
            new ServiceInfo { Name = "Steam Client Service", Display = "Steam Client Service", Status = "Stopped", StartType = "Manual", Description = "Steam Client Service monitors and updates Steam content.", Program = "\"C:\\Program Files (x86)\\Common Files\\Steam\\steamservice.exe\" /RunAsService", CanControl = true },
            new ServiceInfo { Name = "NVDisplay.ContainerLocalSystem", Display = "NVIDIA Display Container LS", Status = "Running", StartType = "Automatic", Pid = 2950, Description = "Container service for NVIDIA root features.", Program = @"C:\Windows\System32\DriverStore\FileRepository\nv_dispi\Display.NvContainer\NVDisplay.Container.exe", Microsoft = false },
            new ServiceInfo { Name = "EasyAntiCheat_EOS", Display = "Easy Anti-Cheat (Epic Online Services)", Status = "Stopped", StartType = "Manual", Description = "Provides integrated security and services for online multiplayer games.", Program = "\"C:\\Program Files (x86)\\EasyAntiCheat_EOS\\EasyAntiCheat_EOS.exe\"" },
            new ServiceInfo { Name = "AntiCheatExpert Service", Display = "AntiCheatExpert Service", Status = "Running", StartType = "Manual", Pid = 5048, Description = "AntiCheatExpert protection service.", Program = "\"C:\\Program Files\\AntiCheatExpert\\SGuard\\x64\\SGuardSvc64.exe\"" },
            new ServiceInfo { Name = "OBS Virtual Camera", Display = "OBS Virtual Camera Helper", Status = "Stopped", StartType = "Disabled", Description = null, Program = "\"C:\\Program Files\\obs-studio\\bin\\64bit\\obs-virtualcam-service.exe\"", CanControl = true },
            new ServiceInfo { Name = "RtkAudioUniversalService", Display = "Realtek Audio Universal Service", Status = "Running", StartType = "Automatic", Pid = 7300, Description = "Realtek Audio Universal Service", Program = @"C:\Windows\System32\DriverStore\FileRepository\realtek\RtkAudUService64.exe" },
            new ServiceInfo { Name = "BthAvctpSvc", Display = "AVCTP service", Status = "Running", StartType = "Manual (trigger)", Pid = 1720, Description = "This is Audio Video Control Transport Protocol service", Program = @"C:\Windows\system32\svchost.exe -k LocalService -p", Microsoft = true },
        };
        public List<ServiceInfo> List() { return list.Select(s => (ServiceInfo)s.MemberwiseCloneHelper()).OrderBy(s => s.Display).ToList(); }
        ServiceInfo Find(string n) { return list.FirstOrDefault(s => s.Name == n); }
        public string Start(string name) { Calls.Add("start " + name); var s = Find(name); if (s == null) return "No such service."; if (!s.CanControl) return "needs admin"; if (s.StartType == "Disabled") return "It's disabled - Windows' Services console can change its startup type."; s.Status = "Running"; s.Pid = 9000 + Calls.Count; return null; }
        public string Stop(string name) { Calls.Add("stop " + name); var s = Find(name); if (s == null) return "No such service."; if (!s.CanControl) return "needs admin"; s.Status = "Stopped"; s.Pid = 0; return null; }
        public string Restart(string name) { Calls.Add("restart " + name); var s = Find(name); if (s == null) return "No such service."; if (!s.CanControl) return "needs admin"; s.Status = "Running"; s.Pid = 9000 + Calls.Count; return null; }
    }
    static class CloneExt { public static object MemberwiseCloneHelper(this ServiceInfo s) { return new ServiceInfo { Name = s.Name, Display = s.Display, Status = s.Status, StartType = s.StartType, Description = s.Description, Program = s.Program, Pid = s.Pid, Microsoft = s.Microsoft, CanControl = s.CanControl, CanStop = s.CanStop }; } }
}
