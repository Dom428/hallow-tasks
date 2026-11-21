// Hallow Tasks - reading the PC (read-only, no admin): one NtQuerySystemInformation call gives every process with its CPU times,
// private working set and I/O counts; the "GPU Engine" / "GPU Process Memory" performance counters give GPU use per process
// (engtype_3D, summed per PID) and dedicated GPU memory; GetExtendedTcpTable counts each process's TCP connections. Per-process
// network bytes would need ETW, which needs admin, so they aren't shown. Paths, descriptions, publishers and signatures are read
// once per process and kept. Everything is a snapshot: compare two to get rates.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace HallowTasks
{
    // the whole-PC numbers for the Performance tab
    public class SysStats
    {
        public double Cpu; public double[] Cores = new double[0];
        public long RamUsed, RamTotal, Committed, CommitLimit;
        public double Gpu3D, GpuEncode, GpuDecode, GpuCopy; public long VramUsed, VramTotal; public string GpuName;
        public double DiskRead, DiskWrite, DiskActive = -1;
        public double NetUp, NetDown; public int Processes, Threads;
        public bool GpuKnown;
    }
    public class Sample { public DateTime At; public List<ProcInfo> Procs = new List<ProcInfo>(); public SysStats Sys = new SysStats(); public double SampleMs; }
    public interface ISampler { Sample Take(); string WindowsDir { get; } }

    public class WindowsSampler : ISampler
    {
        public string WindowsDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [StructLayout(LayoutKind.Sequential)] class MEMSTATUS { public int dwLength = 64; public int load; public ulong total, avail, pageTotal, pageAvail, virtTotal, virtAvail, extAvail; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMSTATUS m);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("iphlpapi.dll")] static extern int GetExtendedTcpTable(IntPtr buf, ref int size, bool sort, int af, int cls, int reserved);

        IntPtr buf = IntPtr.Zero; int bufLen = 1 << 20;
        Dictionary<int, long[]> prevTimes = new Dictionary<int, long[]>();   // pid -> createTime, cpu100ns, readBytes, writeBytes
        long prevTick; long[] prevCores; long prevSysIdle, prevSysTotal;
        readonly int cores = Environment.ProcessorCount;
        readonly Dictionary<string, string> paths = new Dictionary<string, string>();   // pid:create -> path ("" = unknown)
        public readonly FileInfoCache Files = new FileInfoCache();
        Dictionary<int, int> conns = new Dictionary<int, int>(); int connsAt = -100000;
        // GPU counters (one ReadCategory per category per sample)
        PerformanceCounterCategory gpuEngine, gpuProcMem, gpuAdapterMem; bool gpuTried;
        Dictionary<string, CounterSample> prevEngine = new Dictionary<string, CounterSample>();
        PerformanceCounter diskRead, diskWrite, diskIdle; bool diskTried;
        Dictionary<string, long[]> prevNet; long prevNetTick; List<NetworkInterface> nics; int nicsAt = -100000;
        long vramTotal = -1; string gpuName;
        public int GpuEvery = 1; int n;

        public Sample Take()
        {
            var sw = Stopwatch.StartNew();
            var s = new Sample { At = DateTime.Now };
            long tick = Stopwatch.GetTimestamp(); double secs = prevTick == 0 ? 0 : (tick - prevTick) / (double)Stopwatch.Frequency; prevTick = tick;
            ReadProcesses(s, secs);
            MarkWindows(s.Procs);
            if (unchecked(Environment.TickCount - connsAt) > 4500) { conns = CountConnections(); connsAt = Environment.TickCount; }
            foreach (var p in s.Procs) { int c; p.Conns = conns.TryGetValue(p.Pid, out c) ? c : 0; }
            if (n++ % Math.Max(1, GpuEvery) == 0) ReadGpu(s); else s.Sys.GpuKnown = false;
            ReadSystem(s, secs);
            Files.Fill(s.Procs);
            s.SampleMs = sw.Elapsed.TotalMilliseconds;
            return s;
        }

        // ---- processes: SYSTEM_PROCESS_INFORMATION (64-bit layout)
        void ReadProcesses(Sample s, double secs)
        {
            if (IntPtr.Size != 8) { ReadProcessesSlow(s, secs); return; }
            int ret = 0, st;
            for (int i = 0; i < 6; i++)
            {
                if (buf == IntPtr.Zero) buf = Marshal.AllocHGlobal(bufLen);
                st = NtQuerySystemInformation(5, buf, bufLen, out ret);
                if (st == 0) break;
                Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; bufLen = Math.Max(bufLen * 2, ret + 65536);
                if (i == 5) return;
            }
            var next = new Dictionary<int, long[]>(); int off = 0; double denom = secs * cores * 1e7;
            while (true)
            {
                var e = buf + off;
                int nextOff = Marshal.ReadInt32(e, 0);
                var p = new ProcInfo();
                p.Threads = Marshal.ReadInt32(e, 4);
                long privWs = Marshal.ReadInt64(e, 8);
                p.CreateTime = Marshal.ReadInt64(e, 32);
                long user = Marshal.ReadInt64(e, 40), kern = Marshal.ReadInt64(e, 48);
                int nameLen = Marshal.ReadInt16(e, 56); var namePtr = Marshal.ReadIntPtr(e, 64);
                p.Pid = (int)Marshal.ReadInt64(e, 80); p.ParentPid = (int)Marshal.ReadInt64(e, 88);
                p.Session = Marshal.ReadInt32(e, 100);
                long ws = Marshal.ReadInt64(e, 144);
                long rd = Marshal.ReadInt64(e, 232), wr = Marshal.ReadInt64(e, 240);
                p.Name = namePtr == IntPtr.Zero || nameLen == 0 ? (p.Pid == 0 ? "System Idle Process" : "?") : Marshal.PtrToStringUni(namePtr, nameLen / 2);
                p.Ram = privWs > 0 ? privWs : ws;
                long cpu = user + kern; p.CpuTime = cpu;
                long[] prev;
                if (secs > 0 && prevTimes.TryGetValue(p.Pid, out prev) && prev[0] == p.CreateTime)
                {
                    if (p.Pid != 0) p.Cpu = Math.Max(0, Math.Min(100, (cpu - prev[1]) / denom * 100));
                    p.DiskRead = Math.Max(0, (rd - prev[2]) / secs); p.DiskWrite = Math.Max(0, (wr - prev[3]) / secs);
                }
                next[p.Pid] = new[] { p.CreateTime, cpu, rd, wr };
                if (p.Pid == 0) p.Ram = 0;
                p.Path = PathOf(p);
                s.Procs.Add(p);
                if (nextOff == 0) break;
                off += nextOff;
            }
            prevTimes = next;
            s.Sys.Processes = s.Procs.Count; s.Sys.Threads = s.Procs.Sum(x => x.Threads);
            // forget paths of processes that are gone
            if (paths.Count > s.Procs.Count * 3) { var keep = new HashSet<string>(s.Procs.Select(x => x.Key)); foreach (var k in paths.Keys.Where(k => !keep.Contains(k)).ToList()) paths.Remove(k); }
        }
        void ReadProcessesSlow(Sample s, double secs)
        {
            var next = new Dictionary<int, long[]>();
            foreach (var pr in Process.GetProcesses())
            {
                using (pr)
                {
                    var p = new ProcInfo { Pid = pr.Id, Name = pr.ProcessName + (pr.Id > 4 ? ".exe" : "") };
                    try { p.Ram = pr.PrivateMemorySize64; p.Session = pr.SessionId; p.Threads = pr.Threads.Count; } catch { }
                    long cpu = 0; try { cpu = pr.TotalProcessorTime.Ticks; p.CreateTime = pr.StartTime.ToFileTime(); } catch { }
                    long[] prev; if (secs > 0 && prevTimes.TryGetValue(p.Pid, out prev) && prev[0] == p.CreateTime) p.Cpu = Math.Max(0, (cpu - prev[1]) / (secs * cores * 1e7) * 100);
                    next[p.Pid] = new[] { p.CreateTime, cpu, 0L, 0L };
                    p.Path = PathOf(p); s.Procs.Add(p);
                }
            }
            prevTimes = next;
        }
        string PathOf(ProcInfo p)
        {
            if (p.Pid == 0 || p.Pid == 4) return null;
            string v; if (paths.TryGetValue(p.Key, out v)) return v.Length == 0 ? null : v;
            v = ImagePath(p.Pid) ?? ""; paths[p.Key] = v;
            return v.Length == 0 ? null : v;
        }
        public static string ImagePath(int pid)
        {
            var h = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (h == IntPtr.Zero) return null;
            try { var sb = new StringBuilder(1024); int len = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref len) ? sb.ToString(0, len) : null; }
            finally { CloseHandle(h); }
        }

        // ---- which processes have a visible top-level window (Task Manager's "Apps")
        void MarkWindows(List<ProcInfo> procs)
        {
            var pids = new HashSet<int>();
            EnumWindows((h, l) =>
            {
                if (IsWindowVisible(h) && GetWindow(h, 4 /*GW_OWNER*/) == IntPtr.Zero && GetWindowTextLength(h) > 0) { int pid; GetWindowThreadProcessId(h, out pid); pids.Add(pid); }
                return true;
            }, IntPtr.Zero);
            foreach (var p in procs) p.HasWindow = pids.Contains(p.Pid);
        }

        // ---- TCP connections per process (IPv4 + IPv6, established ones; no admin needed)
        static Dictionary<int, int> CountConnections()
        {
            var d = new Dictionary<int, int>();
            foreach (var af in new[] { 2, 23 })
            {
                int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, 5 /*OWNER_PID_ALL*/, 0);
                if (size <= 0) continue;
                var b = Marshal.AllocHGlobal(size + 4096); size += 4096;
                try
                {
                    if (GetExtendedTcpTable(b, ref size, false, af, 5, 0) != 0) continue;
                    int n = Marshal.ReadInt32(b), row = af == 2 ? 24 : 56, pidOff = af == 2 ? 20 : 52, stOff = af == 2 ? 0 : 48;
                    for (int i = 0; i < n; i++)
                    {
                        var r = b + 4 + i * row; int state = Marshal.ReadInt32(r, stOff), pid = Marshal.ReadInt32(r, pidOff);
                        if (state != 5 /*ESTABLISHED*/) continue;
                        int c; d.TryGetValue(pid, out c); d[pid] = c + 1;
                    }
                }
                catch { }
                finally { Marshal.FreeHGlobal(b); }
            }
            return d;
        }

        // ---- GPU: utilisation per engine instance ("pid_1234_luid_0x..._phys_0_eng_3_engtype_3D") and dedicated memory per process
        static int PidOf(string inst) { if (!inst.StartsWith("pid_")) return -1; int e = inst.IndexOf('_', 4); int v; return e > 4 && int.TryParse(inst.Substring(4, e - 4), out v) ? v : -1; }
        void ReadGpu(Sample s)
        {
            try
            {
                if (!gpuTried)
                {
                    gpuTried = true;
                    if (PerformanceCounterCategory.Exists("GPU Engine")) gpuEngine = new PerformanceCounterCategory("GPU Engine");
                    if (PerformanceCounterCategory.Exists("GPU Process Memory")) gpuProcMem = new PerformanceCounterCategory("GPU Process Memory");
                    if (PerformanceCounterCategory.Exists("GPU Adapter Memory")) gpuAdapterMem = new PerformanceCounterCategory("GPU Adapter Memory");
                    ReadVram();
                }
                if (gpuEngine == null) return;
                var byPid = new Dictionary<int, double>(); var engines = new Dictionary<string, double>();   // "luid_eng|type" -> %
                var data = gpuEngine.ReadCategory();
                var util = data.Contains("Utilization Percentage") ? data["Utilization Percentage"] : null;
                var next = new Dictionary<string, CounterSample>();
                if (util != null)
                    foreach (InstanceData d in util.Values)
                    {
                        var inst = d.InstanceName; var smp = d.Sample; next[inst] = smp;
                        CounterSample old; if (!prevEngine.TryGetValue(inst, out old)) continue;
                        double v; try { v = CounterSample.Calculate(old, smp); } catch { continue; }
                        if (v <= 0 || double.IsNaN(v)) continue;
                        int ti = inst.IndexOf("_engtype_", StringComparison.Ordinal); if (ti < 0) continue;
                        var type = inst.Substring(ti + 9); int li = inst.IndexOf("_luid_", StringComparison.Ordinal);
                        var eng = (li >= 0 ? inst.Substring(li, ti - li) : inst) + "|" + type;
                        double t; engines.TryGetValue(eng, out t); engines[eng] = t + v;
                        if (type == "3D") { int pid = PidOf(inst); if (pid > 0) { double pv; byPid.TryGetValue(pid, out pv); byPid[pid] = pv + v; } }
                    }
                prevEngine = next;
                Func<string, double> top = type => Math.Min(100, engines.Where(kv => kv.Key.EndsWith("|" + type, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value).DefaultIfEmpty(0).Max());
                s.Sys.Gpu3D = top("3D"); s.Sys.GpuEncode = top("VideoEncode"); s.Sys.GpuDecode = top("VideoDecode"); s.Sys.GpuCopy = top("Copy"); s.Sys.GpuKnown = util != null;
                var mem = new Dictionary<int, long>();
                if (gpuProcMem != null)
                {
                    var md = gpuProcMem.ReadCategory();
                    if (md.Contains("Dedicated Usage")) foreach (InstanceData d in md["Dedicated Usage"].Values) { int pid = PidOf(d.InstanceName); if (pid > 0) { long m; mem.TryGetValue(pid, out m); mem[pid] = m + d.RawValue; } }
                }
                if (gpuAdapterMem != null)
                {
                    var ad = gpuAdapterMem.ReadCategory();
                    if (ad.Contains("Dedicated Usage")) { long used = 0; foreach (InstanceData d in ad["Dedicated Usage"].Values) used = Math.Max(used, d.RawValue); s.Sys.VramUsed = used; }
                }
                s.Sys.VramTotal = vramTotal; s.Sys.GpuName = gpuName;
                foreach (var p in s.Procs) { double g; long m; if (byPid.TryGetValue(p.Pid, out g)) p.Gpu = Math.Min(100, g); if (mem.TryGetValue(p.Pid, out m)) p.GpuMem = m; }
            }
            catch { }
        }
        // the graphics card with the most dedicated memory (the display adapter class key - readable without admin)
        void ReadVram()
        {
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    if (cls != null)
                        foreach (var n in cls.GetSubKeyNames())
                        {
                            if (n.Length != 4) continue;
                            try
                            {
                                using (var k = cls.OpenSubKey(n))
                                {
                                    var v = k.GetValue("HardwareInformation.qwMemorySize"); long mem = 0;
                                    if (v is long) mem = (long)v; else if (v is byte[] && ((byte[])v).Length >= 8) mem = BitConverter.ToInt64((byte[])v, 0); else if (v is byte[] && ((byte[])v).Length >= 4) mem = BitConverter.ToUInt32((byte[])v, 0);
                                    if (mem > vramTotal) { vramTotal = mem; gpuName = k.GetValue("DriverDesc") as string; }
                                }
                            }
                            catch { }
                        }
            }
            catch { }
        }

        // ---- the whole PC: CPU per core, memory, disk, network
        void ReadSystem(Sample s, double secs)
        {
            // SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION: Idle, Kernel (includes idle), User, Dpc, Interrupt (8 bytes each), InterruptCount (+pad) = 48 bytes
            try
            {
                int len = 48 * cores; var b = Marshal.AllocHGlobal(len); int ret;
                try
                {
                    if (NtQuerySystemInformation(8, b, len, out ret) == 0)
                    {
                        var now = new long[cores * 2]; long idleAll = 0, totalAll = 0;
                        for (int i = 0; i < cores; i++) { long idle = Marshal.ReadInt64(b, i * 48), k = Marshal.ReadInt64(b, i * 48 + 8), u = Marshal.ReadInt64(b, i * 48 + 16); now[i * 2] = idle; now[i * 2 + 1] = k + u; idleAll += idle; totalAll += k + u; }
                        if (prevCores != null)
                        {
                            s.Sys.Cores = new double[cores];
                            for (int i = 0; i < cores; i++) { long dt = now[i * 2 + 1] - prevCores[i * 2 + 1], di = now[i * 2] - prevCores[i * 2]; s.Sys.Cores[i] = dt <= 0 ? 0 : Math.Max(0, Math.Min(100, 100.0 * (dt - di) / dt)); }
                            long dT = totalAll - prevSysTotal, dI = idleAll - prevSysIdle; s.Sys.Cpu = dT <= 0 ? 0 : Math.Max(0, Math.Min(100, 100.0 * (dT - dI) / dT));
                        }
                        prevCores = now; prevSysIdle = idleAll; prevSysTotal = totalAll;
                    }
                }
                finally { Marshal.FreeHGlobal(b); }
            }
            catch { }
            try { var m = new MEMSTATUS(); if (GlobalMemoryStatusEx(m)) { s.Sys.RamTotal = (long)m.total; s.Sys.RamUsed = (long)(m.total - m.avail); s.Sys.CommitLimit = (long)m.pageTotal; s.Sys.Committed = (long)(m.pageTotal - m.pageAvail); } } catch { }
            try
            {
                if (!diskTried)
                {
                    diskTried = true;
                    diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true); diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
                    diskIdle = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", true);
                    diskRead.NextValue(); diskWrite.NextValue(); diskIdle.NextValue();
                }
                if (diskRead != null) { s.Sys.DiskRead = diskRead.NextValue(); s.Sys.DiskWrite = diskWrite.NextValue(); s.Sys.DiskActive = Math.Max(0, Math.Min(100, 100 - diskIdle.NextValue())); }
            }
            catch { diskRead = null; }
            try
            {
                if (nics == null || unchecked(Environment.TickCount - nicsAt) > 30000)
                {
                    nicsAt = Environment.TickCount;
                    nics = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel).ToList();
                }
                var now = new Dictionary<string, long[]>(); long t = Stopwatch.GetTimestamp();
                foreach (var nic in nics) { try { var st = nic.GetIPStatistics(); now[nic.Id] = new[] { st.BytesSent, st.BytesReceived }; } catch { } }
                if (prevNet != null)
                {
                    double dt = (t - prevNetTick) / (double)Stopwatch.Frequency; double up = 0, down = 0;
                    foreach (var kv in now) { long[] o; if (prevNet.TryGetValue(kv.Key, out o)) { up += Math.Max(0, kv.Value[0] - o[0]); down += Math.Max(0, kv.Value[1] - o[1]); } }
                    if (dt > 0) { s.Sys.NetUp = up / dt; s.Sys.NetDown = down / dt; }
                }
                prevNet = now; prevNetTick = t;
            }
            catch { }
        }
    }

    // descriptions, publishers and signatures, read once per file on a background thread (never on the UI thread)
    public class FileInfoCache
    {
        public class Info { public string Description, Company, Signer; public bool? Signed; }
        readonly Dictionary<string, Info> cache = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> todo = new Queue<string>(); readonly HashSet<string> queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Thread worker; readonly object gate = new object();
        public void Fill(List<ProcInfo> procs)
        {
            lock (gate)
                foreach (var p in procs)
                {
                    if (p.Path == null) continue;
                    Info i; if (cache.TryGetValue(p.Path, out i)) { p.Description = i.Description; p.Company = i.Company; p.Signer = i.Signer; p.Signed = i.Signed; }
                    else if (queued.Add(p.Path)) todo.Enqueue(p.Path);
                }
            if (todo.Count > 0 && worker == null)
            {
                worker = new Thread(Work) { IsBackground = true, Name = "file info", Priority = ThreadPriority.BelowNormal }; worker.Start();
            }
        }
        void Work()
        {
            while (true)
            {
                string path;
                lock (gate) { if (todo.Count == 0) { worker = null; return; } path = todo.Dequeue(); }
                var i = Read(path);
                lock (gate) { cache[path] = i; queued.Remove(path); }
            }
        }
        public static Info Read(string path)
        {
            var i = new Info();
            try { var v = FileVersionInfo.GetVersionInfo(path); i.Description = string.IsNullOrWhiteSpace(v.FileDescription) ? null : v.FileDescription.Trim(); i.Company = string.IsNullOrWhiteSpace(v.CompanyName) ? null : v.CompanyName.Trim(); } catch { }
            try { i.Signer = Signing.Signer(path); i.Signed = i.Signer != null ? Signing.Verify(path) : (bool?)false; if (i.Signed == false && i.Signer != null) i.Signer = null; } catch { }
            if (i.Signed != true && Signing.CatalogSigned(path)) { i.Signed = true; if (i.Signer == null && i.Company != null && i.Company.StartsWith("Microsoft")) i.Signer = "Microsoft Windows"; }
            return i;
        }
    }

    // Authenticode (from Hallow Guard's leftover hunt): embedded signatures, plus Windows' own catalog-signed files
    public static class Signing
    {
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct WINTRUST_FILE_INFO { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
        [StructLayout(LayoutKind.Sequential)] struct WINTRUST_DATA
        {
            public int cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public int dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile; public int dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference; public int dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
        }
        static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        public static bool Verify(string path)
        {
            var fi = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = path };
            var pFile = Marshal.AllocHGlobal(fi.cbStruct);
            try
            {
                Marshal.StructureToPtr(fi, pFile, false);
                var d = new WINTRUST_DATA { cbStruct = Marshal.SizeOf(typeof(WINTRUST_DATA)), dwUIChoice = 2, fdwRevocationChecks = 0, dwUnionChoice = 1, pFile = pFile, dwStateAction = 1, dwProvFlags = 0x1000 | 0x80 };
                int r = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref d);
                d.dwStateAction = 2; WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref d);
                return r == 0;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(pFile); }
        }
        public static string Signer(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var c = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                var cn = c.GetNameInfo(X509NameType.SimpleName, false);
                return string.IsNullOrWhiteSpace(cn) ? null : cn.Trim();
            }
            catch { return null; }
        }
        // Windows' own files are mostly signed through catalogs, not embedded signatures
        [DllImport("wintrust.dll", SetLastError = true)] static extern bool CryptCATAdminAcquireContext(out IntPtr h, IntPtr subsystem, int flags);
        [DllImport("wintrust.dll")] static extern bool CryptCATAdminReleaseContext(IntPtr h, int flags);
        [DllImport("wintrust.dll", SetLastError = true)] static extern bool CryptCATAdminCalcHashFromFileHandle(IntPtr file, ref int size, byte[] hash, int flags);
        [DllImport("wintrust.dll")] static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr h, byte[] hash, int size, int flags, IntPtr prev);
        [DllImport("wintrust.dll")] static extern bool CryptCATAdminReleaseCatalogContext(IntPtr h, IntPtr cat, int flags);
        public static bool CatalogSigned(string path)
        {
            try
            {
                using (var f = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    IntPtr ctx; if (!CryptCATAdminAcquireContext(out ctx, IntPtr.Zero, 0)) return false;
                    try
                    {
                        int size = 0; CryptCATAdminCalcHashFromFileHandle(f.SafeFileHandle.DangerousGetHandle(), ref size, null, 0);
                        if (size <= 0 || size > 256) return false;
                        var hash = new byte[size]; if (!CryptCATAdminCalcHashFromFileHandle(f.SafeFileHandle.DangerousGetHandle(), ref size, hash, 0)) return false;
                        var cat = CryptCATAdminEnumCatalogFromHash(ctx, hash, size, 0, IntPtr.Zero);
                        if (cat == IntPtr.Zero) return false;
                        CryptCATAdminReleaseCatalogContext(ctx, cat, 0); return true;
                    }
                    finally { CryptCATAdminReleaseContext(ctx, 0); }
                }
            }
            catch { return false; }
        }
    }

    // ---------------- process control for real (only what the user clicked, never in tests except on the tests' own dummy children)
    public class WindowsControl : IProcessControl
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int GetPriorityClass(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetPriorityClass(IntPtr h, int c);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr h, int code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(IntPtr h, out long create, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr h, out int code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessAffinityMask(IntPtr h, out UIntPtr proc, out UIntPtr sys);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessAffinityMask(IntPtr h, UIntPtr mask);
        const int QUERY_LIMITED = 0x1000, SET_INFO = 0x0200, TERMINATE = 0x0001, QUERY = 0x0400;
        static int Class(Prio p) { switch (p) { case Prio.Idle: return 0x40; case Prio.BelowNormal: return 0x4000; case Prio.AboveNormal: return 0x8000; case Prio.High: return 0x80; default: return 0x20; } }
        static Prio? FromClass(int c) { switch (c) { case 0x40: return Prio.Idle; case 0x4000: return Prio.BelowNormal; case 0x20: return Prio.Normal; case 0x8000: return Prio.AboveNormal; case 0x80: return Prio.High; case 0x100: return Prio.High; default: return null; } }
        static string Err(string what) { var e = Marshal.GetLastWin32Error(); return e == 5 ? "Windows said no (access denied) - " + what + " belongs to another user or is protected. Hallow Tasks doesn't ask for admin." : e == 87 ? "That process has closed." : "Windows said no (error " + e + ")."; }
        public Prio? GetPriority(int pid)
        {
            var h = OpenProcess(QUERY_LIMITED, false, pid); if (h == IntPtr.Zero) return null;
            try { var c = GetPriorityClass(h); return c == 0 ? null : FromClass(c); } finally { CloseHandle(h); }
        }
        public string SetPriority(int pid, Prio p)
        {
            var h = OpenProcess(SET_INFO | QUERY_LIMITED, false, pid); if (h == IntPtr.Zero) return Err("it");
            try { return SetPriorityClass(h, Class(p)) ? null : Err("it"); } finally { CloseHandle(h); }
        }
        public string Kill(int pid)
        {
            var h = OpenProcess(TERMINATE | QUERY_LIMITED, false, pid); if (h == IntPtr.Zero) return Err("it");
            try { return TerminateProcess(h, 1) ? null : Err("it"); } finally { CloseHandle(h); }
        }
        public long Affinity(int pid)
        {
            var h = OpenProcess(QUERY_LIMITED, false, pid); if (h == IntPtr.Zero) return 0;
            try { UIntPtr a, s; return GetProcessAffinityMask(h, out a, out s) ? (long)a.ToUInt64() : 0; } finally { CloseHandle(h); }
        }
        public string SetAffinity(int pid, long mask)
        {
            if (mask == 0) return "Pick at least one CPU.";
            var h = OpenProcess(SET_INFO | QUERY_LIMITED, false, pid); if (h == IntPtr.Zero) return Err("it");
            try { return SetProcessAffinityMask(h, new UIntPtr((ulong)mask)) ? null : Err("it"); } finally { CloseHandle(h); }
        }
        public long CreateTime(int pid)
        {
            var h = OpenProcess(QUERY_LIMITED, false, pid); if (h == IntPtr.Zero) return 0;
            try { long c, e, k, u; int code; if (GetExitCodeProcess(h, out code) && code != 259 /*STILL_ACTIVE*/) return 0; return GetProcessTimes(h, out c, out e, out k, out u) ? c : 0; } finally { CloseHandle(h); }
        }
    }
}
