// Hallow Tasks - made-up processes, counters and process control for --snap pictures and the test copy. Never this PC's real
// processes, names or paths. The fake control remembers what it was asked and never touches a real process.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HallowTasks
{
    public class FakeSampler : ISampler
    {
        public string WindowsDir { get { return @"C:\Windows"; } }
        public bool GameOn = true; int n;
        static readonly long Boot = DateTime.Now.AddHours(-6).ToFileTime();
        // pid, parent, exe, path, window, cpu, ram MB, gpu, gpu MB, disk KB/s, conns, description, publisher, signed (1/0/-1)
        static readonly object[][] Rows = {
            new object[] { 0, 0, "System Idle Process", null, false, 0.0, 0, 0.0, 0, 0, 0, null, null, -1 },
            new object[] { 4, 0, "System", null, false, 0.6, 0, 0.0, 0, 380, 0, null, null, -1 },
            new object[] { 148, 4, "Registry", null, false, 0.0, 38, 0.0, 0, 0, 0, null, null, -1 },
            new object[] { 612, 4, "smss.exe", null, false, 0.0, 1, 0.0, 0, 0, 0, null, null, -1 },
            new object[] { 804, 612, "csrss.exe", null, false, 0.1, 2, 0.0, 0, 0, 0, null, null, -1 },
            new object[] { 960, 612, "wininit.exe", null, false, 0.0, 1, 0.0, 0, 0, 0, null, null, -1 },
            new object[] { 1032, 960, "services.exe", null, false, 0.1, 6, 0.0, 0, 0, 0, null, null, -1 },
            new object[] { 1060, 960, "lsass.exe", null, false, 0.1, 12, 0.0, 0, 0, 0, null, null, -1 },
            new object[] { 1240, 1032, "svchost.exe", @"C:\Windows\System32\svchost.exe", false, 0.4, 24, 0.0, 0, 40, 2, "Host Process for Windows Services", "Microsoft Windows", 1 },
            new object[] { 1384, 1032, "svchost.exe", @"C:\Windows\System32\svchost.exe", false, 0.2, 14, 0.0, 0, 10, 0, "Host Process for Windows Services", "Microsoft Windows", 1 },
            new object[] { 1720, 1032, "svchost.exe", @"C:\Windows\System32\svchost.exe", false, 1.1, 62, 0.0, 0, 210, 4, "Host Process for Windows Services", "Microsoft Windows", 1 },
            new object[] { 1544, 960, "dwm.exe", @"C:\Windows\System32\dwm.exe", false, 1.8, 96, 6.0, 210, 0, 0, "Desktop Window Manager", "Microsoft Windows", 1 },
            new object[] { 2210, 1032, "MsMpEng.exe", @"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.25090.3009-0\MsMpEng.exe", false, 2.4, 188, 0.0, 0, 2600, 1, "Antimalware Service Executable", "Microsoft Corporation", 1 },
            new object[] { 3620, 3500, "explorer.exe", @"C:\Windows\explorer.exe", true, 0.3, 120, 0.4, 40, 12, 3, "Windows Explorer", "Microsoft Windows", 1 },
            new object[] { 5020, 3620, "DeltaForceClient-Win64-Shipping.exe", @"D:\SteamLibrary\steamapps\common\Delta Force\Game\DeltaForce\Binaries\Win64\DeltaForceClient-Win64-Shipping.exe", true, 31.0, 7400, 88.0, 6900, 900, 6, "Delta Force", "Proxima Beta Pte. Limited", 1 },
            new object[] { 5044, 5020, "SGuard64.exe", @"C:\Program Files\AntiCheatExpert\SGuard\x64\SGuard64.exe", false, 1.2, 48, 0.0, 0, 30, 1, "SGuard", "Anti-Cheat Expert", 1 },
            new object[] { 5048, 1032, "SGuardSvc64.exe", @"C:\Program Files\AntiCheatExpert\SGuard\x64\SGuardSvc64.exe", false, 0.4, 12, 0.0, 0, 5, 0, "SGuard Service", "Anti-Cheat Expert", 1 },
            new object[] { 4100, 3620, "Discord.exe", @"C:\Users\Alex\AppData\Local\Discord\app-1.0.9213\Discord.exe", true, 1.5, 210, 2.0, 120, 30, 9, "Discord", "Discord Inc.", 1 },
            new object[] { 4110, 4100, "Discord.exe", @"C:\Users\Alex\AppData\Local\Discord\app-1.0.9213\Discord.exe", false, 4.2, 180, 6.5, 160, 60, 0, "Discord", "Discord Inc.", 1 },
            new object[] { 4120, 4100, "Discord.exe", @"C:\Users\Alex\AppData\Local\Discord\app-1.0.9213\Discord.exe", false, 0.8, 95, 0.0, 0, 8, 0, "Discord", "Discord Inc.", 1 },
            new object[] { 4130, 4100, "Discord.exe", @"C:\Users\Alex\AppData\Local\Discord\app-1.0.9213\Discord.exe", false, 0.2, 40, 0.0, 0, 0, 0, "Discord", "Discord Inc.", 1 },
            new object[] { 4300, 3620, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", true, 2.0, 260, 1.0, 90, 120, 14, "Google Chrome", "Google LLC", 1 },
            new object[] { 4310, 4300, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false, 6.8, 410, 9.0, 300, 40, 0, "Google Chrome", "Google LLC", 1 },
            new object[] { 4320, 4300, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false, 0.9, 150, 0.0, 0, 6, 0, "Google Chrome", "Google LLC", 1 },
            new object[] { 4330, 4300, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false, 0.4, 120, 0.0, 0, 0, 0, "Google Chrome", "Google LLC", 1 },
            new object[] { 4340, 4300, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false, 0.3, 90, 0.0, 0, 0, 0, "Google Chrome", "Google LLC", 1 },
            new object[] { 4500, 3620, "steam.exe", @"C:\Program Files (x86)\Steam\steam.exe", true, 0.6, 140, 0.0, 0, 20, 5, "Steam", "Valve Corp.", 1 },
            new object[] { 4510, 4500, "steamwebhelper.exe", @"C:\Program Files (x86)\Steam\bin\cef\cef.win64\steamwebhelper.exe", false, 1.1, 230, 1.5, 80, 0, 0, "Steam Client WebHelper", "Valve Corp.", 1 },
            new object[] { 4520, 4510, "steamwebhelper.exe", @"C:\Program Files (x86)\Steam\bin\cef\cef.win64\steamwebhelper.exe", false, 0.2, 110, 0.0, 0, 0, 0, "Steam Client WebHelper", "Valve Corp.", 1 },
            new object[] { 4700, 3620, "obs64.exe", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", true, 3.6, 380, 7.0, 420, 4200, 1, "OBS Studio", "OBS Project", 1 },
            new object[] { 4800, 3620, "Spotify.exe", @"C:\Users\Alex\AppData\Roaming\Spotify\Spotify.exe", true, 0.7, 190, 0.5, 60, 15, 3, "Spotify", "Spotify AB", 1 },
            new object[] { 4900, 3620, "wallpaper64.exe", @"C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe", false, 2.9, 160, 11.0, 350, 0, 0, "Wallpaper Engine", "Wallpaper Engine Team", 1 },
            new object[] { 6100, 3620, "HallowHub.exe", @"C:\Users\Alex\AppData\Local\Programs\Hallow Hub\HallowHub.exe", false, 0.1, 64, 0.0, 0, 0, 0, "Hallow Hub", null, -1 },
            new object[] { 6110, 3620, "HallowMixer.exe", @"C:\Users\Alex\AppData\Local\Programs\Hallow Mixer\HallowMixer.exe", false, 0.3, 58, 0.0, 0, 0, 0, "Hallow Mixer", null, -1 },
            new object[] { 6120, 3620, "HallowOverlay.exe", @"C:\Users\Alex\AppData\Local\Programs\Hallow Overlay\HallowOverlay.exe", false, 0.4, 52, 0.6, 24, 0, 0, "Hallow Overlay", null, -1 },
            new object[] { 6130, 3620, "HallowClips.exe", @"C:\Users\Alex\AppData\Local\Programs\Hallow Clips\HallowClips.exe", false, 1.4, 120, 3.0, 140, 900, 0, "Hallow Clips", null, -1 },
            new object[] { 6140, 3620, "HallowTasks.exe", @"C:\Users\Alex\AppData\Local\Programs\Hallow Tasks\HallowTasks.exe", true, 0.4, 70, 0.3, 20, 0, 0, "Hallow Tasks", null, -1 },
            new object[] { 7000, 1032, "OneDrive.exe", @"C:\Users\Alex\AppData\Local\Microsoft\OneDrive\OneDrive.exe", false, 0.2, 80, 0.0, 0, 350, 2, "Microsoft OneDrive", "Microsoft Corporation", 1 },
            new object[] { 7100, 1240, "SearchIndexer.exe", @"C:\Windows\System32\SearchIndexer.exe", false, 3.1, 46, 0.0, 0, 5200, 0, "Microsoft Windows Search Indexer", "Microsoft Windows", 1 },
            new object[] { 7200, 3620, "updsvc.exe", @"C:\Users\Alex\AppData\Roaming\syshelper\updsvc.exe", false, 0.5, 22, 0.0, 0, 40, 1, null, null, 0 },
            new object[] { 7300, 1032, "RtkAudUService64.exe", @"C:\Windows\System32\DriverStore\FileRepository\realtek\RtkAudUService64.exe", false, 0.1, 6, 0.0, 0, 0, 0, "Realtek HD Audio Universal Service", "Realtek Semiconductor Corp.", 1 },
        };
        public bool Gaming { get { return GameOn; } }
        public Sample Take()
        {
            n++;
            var s = new Sample { At = DateTime.Now, SampleMs = 3.1 };
            foreach (var r in Rows)
            {
                var exe = (string)r[2];
                if (!GameOn && (exe.StartsWith("DeltaForce") || exe.StartsWith("SGuard64"))) continue;
                int pid = (int)r[0];
                double wob = 1 + 0.15 * Math.Sin(n * 0.7 + pid);
                var p = new ProcInfo {
                    Pid = pid, ParentPid = (int)r[1], Name = exe, Path = (string)r[3], HasWindow = (bool)r[4], Cpu = (double)r[5] * wob, Ram = (long)(int)r[6] * 1048576L,
                    Gpu = (double)r[7] * wob, GpuMem = (long)(int)r[8] * 1048576L, DiskRead = (int)r[9] * 1024.0 * 0.6 * wob, DiskWrite = (int)r[9] * 1024.0 * 0.4 * wob, Conns = (int)r[10],
                    Description = (string)r[11], Company = (string)r[12], Signer = (int)r[13] == 1 ? (string)r[12] : null, Signed = (int)r[13] == 1 ? true : (int)r[13] == 0 ? false : (bool?)null,
                    Session = pid < 3000 || pid == 7000 || pid == 7300 ? 0 : 1, CreateTime = Boot + pid * 10000000L, Threads = 8, CpuTime = (long)((double)r[5] / 100 * 16 * 6 * 3600 * 1e7 * 0.05) };   // mostly idle over 6 hours
                if (p.Path == null && pid > 4) p.Session = 0;
                s.Procs.Add(p);
            }
            var sys = s.Sys; sys.GpuKnown = true; sys.GpuName = "NVIDIA GeForce RTX 4070";
            int cores = 16; sys.Cores = new double[cores];
            for (int i = 0; i < cores; i++) sys.Cores[i] = Math.Max(2, Math.Min(100, (GameOn ? 38 : 9) + 30 * Math.Sin(n * 0.31 + i * 1.7) * (i % 3 == 0 ? 1 : 0.4)));
            sys.Cpu = sys.Cores.Average();
            sys.RamTotal = 32L << 30; sys.RamUsed = (long)((GameOn ? 19.4 : 11.2) * (1 << 30)); sys.CommitLimit = 40L << 30; sys.Committed = (long)((GameOn ? 24.1 : 14.3) * (1 << 30));
            sys.Gpu3D = GameOn ? 91 + 5 * Math.Sin(n * 0.5) : 6; sys.GpuEncode = 12 + 2 * Math.Sin(n); sys.GpuDecode = 3; sys.GpuCopy = 4 + 2 * Math.Sin(n * 0.9);
            sys.VramTotal = 12L << 30; sys.VramUsed = (long)((GameOn ? 9.6 : 1.8) * (1 << 30));
            sys.DiskRead = 9.5 * 1048576 * (1 + 0.5 * Math.Sin(n * 0.4)); sys.DiskWrite = 4.2 * 1048576; sys.DiskActive = 14 + 8 * Math.Sin(n * 0.4);
            sys.NetDown = 2.6 * 1048576 / 8 * 10 * (1 + 0.3 * Math.Sin(n)); sys.NetUp = 0.4 * 1048576; sys.Processes = s.Procs.Count; sys.Threads = s.Procs.Count * 23;
            return s;
        }
    }

    // a process control that only remembers what it was asked (tests and --snap)
    public class FakeControl : IProcessControl
    {
        public readonly Dictionary<int, Prio> Prios = new Dictionary<int, Prio>();
        public readonly List<string> Calls = new List<string>();
        public readonly HashSet<int> Gone = new HashSet<int>();
        public readonly Dictionary<int, long> Created = new Dictionary<int, long>();
        public Prio? GetPriority(int pid) { if (Gone.Contains(pid)) return null; Prio p; return Prios.TryGetValue(pid, out p) ? p : Prio.Normal; }
        public string SetPriority(int pid, Prio p) { Calls.Add("prio " + pid + " " + p); if (Gone.Contains(pid)) return "That process has closed."; Prios[pid] = p; return null; }
        public string Kill(int pid) { Calls.Add("kill " + pid); if (Gone.Contains(pid)) return "That process has closed."; Gone.Add(pid); return null; }
        public string SetAffinity(int pid, long mask) { Calls.Add("affinity " + pid + " " + mask); return mask == 0 ? "Pick at least one CPU." : null; }
        public long Affinity(int pid) { return 0xFFFF; }
        public long CreateTime(int pid) { if (Gone.Contains(pid)) return 0; long c; return Created.TryGetValue(pid, out c) ? c : 0; }
    }
}
