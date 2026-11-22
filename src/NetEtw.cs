// Hallow Tasks - "Precise network (needs admin)": bytes per second per process. Windows only gives those to an ETW trace, and a
// kernel network trace needs admin. So when you switch it on (Settings), Hallow Tasks asks Windows to start a small helper - its
// own exe with "--netetw <pipe> <pid>" - elevated: Windows shows UAC and you decide. The helper runs one real-time session on
// Microsoft-Windows-Kernel-Network (TCP / UDP send and receive, IPv4 and IPv6 - only the process id and the byte count of each event
// are read, never addresses or contents) and sends "net <ms> <pid>:<sent>:<received> ..." once a second over a local pipe that
// only this Windows user can open. It stops (and ends its trace) when Hallow Tasks closes, when you switch it off, or when the pipe
// breaks. It is never started by itself: not at startup, not by the tests.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HallowTasks
{
    // a real-time ETW session (the same code as Hallow Overlay's, which has run its FPS counter for months)
    public class EtwSession
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint StartTraceW(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint ControlTraceW(ulong handle, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll")] static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint code, byte level, ulong any, ulong all, uint timeout, IntPtr parms);
        [DllImport("advapi32.dll")] static extern ulong OpenTraceW(IntPtr logfile);
        [DllImport("advapi32.dll")] static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern uint CloseTrace(ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void RecordCallback(IntPtr rec);
        const int PropsSize = 120 + 2 * 1024;
        readonly string name; readonly Guid[] providers; readonly ulong[] keywords; readonly Action<IntPtr> onEvent;
        ulong session, trace = ulong.MaxValue; IntPtr logfile = IntPtr.Zero, loggerName = IntPtr.Zero; RecordCallback cb; Thread thread;
        public bool Running { get; private set; }
        public bool Denied { get; private set; }
        public uint LastError { get; private set; }
        public ushort[][] EventIds;
        public EtwSession(string sessionName, Guid[] providerIds, ulong[] providerKeywords, Action<IntPtr> handler) { name = sessionName; providers = providerIds; keywords = providerKeywords; onEvent = handler; }
        static IntPtr IdFilter(ushort[] ids, List<IntPtr> free)
        {
            int fsize = 4 + 2 * ids.Length; var f = Marshal.AllocHGlobal(fsize); free.Add(f);
            Marshal.WriteByte(f, 0, 1); Marshal.WriteByte(f, 1, 0); Marshal.WriteInt16(f, 2, (short)ids.Length);
            for (int i = 0; i < ids.Length; i++) Marshal.WriteInt16(f, 4 + 2 * i, (short)ids[i]);
            var d = Marshal.AllocHGlobal(16); free.Add(d);
            Marshal.WriteInt64(d, 0, f.ToInt64()); Marshal.WriteInt32(d, 8, fsize); Marshal.WriteInt32(d, 12, unchecked((int)0x80000200));
            var p = Marshal.AllocHGlobal(48); free.Add(p);
            for (int i = 0; i < 48; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, 2); Marshal.WriteIntPtr(p, 32, d); Marshal.WriteInt32(p, 40, 1);
            return p;
        }
        static IntPtr Props()
        {
            var p = Marshal.AllocHGlobal(PropsSize);
            for (int i = 0; i < PropsSize; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, PropsSize); Marshal.WriteInt32(p, 40, 1); Marshal.WriteInt32(p, 44, 0x20000); Marshal.WriteInt32(p, 64, 0x100); Marshal.WriteInt32(p, 68, 1); Marshal.WriteInt32(p, 116, 120);
            return p;
        }
        public bool Start()
        {
            if (Running) return true;
            if (IntPtr.Size != 8) return false;
            var p = Props();
            try
            {
                uint r = StartTraceW(out session, name, p);
                if (r == 183) { var q = Props(); try { ControlTraceW(0, name, q, 1); } finally { Marshal.FreeHGlobal(q); } Marshal.FreeHGlobal(p); p = Props(); r = StartTraceW(out session, name, p); }
                LastError = r;
                if (r != 0) { Denied = r == 5; return false; }
                Denied = false; uint ok = 0, last = 0; var free = new List<IntPtr>();
                try
                {
                    for (int i = 0; i < providers.Length; i++)
                    {
                        var g = providers[i]; var ids = EventIds != null && i < EventIds.Length ? EventIds[i] : null;
                        last = EnableTraceEx2(session, ref g, 1, 5, keywords[i], 0, 0, ids == null ? IntPtr.Zero : IdFilter(ids, free));
                        if (last != 0 && ids != null) last = EnableTraceEx2(session, ref g, 1, 5, keywords[i], 0, 0, IntPtr.Zero);
                        if (last == 0) ok++;
                    }
                }
                finally { foreach (var f in free) Marshal.FreeHGlobal(f); }
                if (ok == 0) { StopSession(); Denied = last == 5; LastError = last; return false; }
                logfile = Marshal.AllocHGlobal(448);
                for (int i = 0; i < 448; i++) Marshal.WriteByte(logfile, i, 0);
                loggerName = Marshal.StringToHGlobalUni(name);
                Marshal.WriteIntPtr(logfile, 8, loggerName);
                Marshal.WriteInt32(logfile, 28, 0x100 | 0x10000000);
                cb = rec => { try { onEvent(rec); } catch { } };
                Marshal.WriteIntPtr(logfile, 424, Marshal.GetFunctionPointerForDelegate(cb));
                trace = OpenTraceW(logfile);
                if (trace == ulong.MaxValue) { StopSession(); Free(); return false; }
                var h = trace;
                thread = new Thread(() => { try { ProcessTrace(new[] { h }, 1, IntPtr.Zero, IntPtr.Zero); } catch { } }) { IsBackground = true, Name = "tasks " + name };
                thread.Start(); Running = true; return true;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(p); }
        }
        void StopSession() { var q = Props(); try { ControlTraceW(session, null, q, 1); } catch { } finally { Marshal.FreeHGlobal(q); } }
        void Free() { if (logfile != IntPtr.Zero) { Marshal.FreeHGlobal(logfile); logfile = IntPtr.Zero; } if (loggerName != IntPtr.Zero) { Marshal.FreeHGlobal(loggerName); loggerName = IntPtr.Zero; } }
        public void Stop()
        {
            if (!Running) return; Running = false; StopSession();
            try { CloseTrace(trace); } catch { }
            trace = ulong.MaxValue; if (thread != null) thread.Join(3000); thread = null; Free();
        }
    }

    public static class NetHelper
    {
        public static readonly Guid KernelNetwork = new Guid("7DD42A49-5329-4832-8DFD-43D979153A88");
        // TCP sent / received (IPv4 10 / 11, IPv6 26 / 27), UDP sent / received (IPv4 42 / 43, IPv6 58 / 59)
        public static readonly ushort[] Ids = { 10, 11, 26, 27, 42, 43, 58, 59 };
        public static bool IsSend(int id) { return id == 10 || id == 26 || id == 42 || id == 58; }
        public static bool ValidPipe(string n) { return n != null && Regex.IsMatch(n, @"^HallowTasksNet\.[A-Za-z0-9]{8,40}$"); }
        // the byte counts of one second: "net <ms> <pid>:<sent>:<received> ..."
        public static string Line(int ms, IDictionary<int, long[]> counts)
        {
            var sb = new StringBuilder("net " + ms);
            foreach (var kv in counts) if (kv.Value[0] > 0 || kv.Value[1] > 0) sb.Append(' ').Append(kv.Key).Append(':').Append(kv.Value[0]).Append(':').Append(kv.Value[1]);
            return sb.ToString();
        }
        // pid -> { sent/s, received/s }; null when it isn't a "net" line
        public static Dictionary<int, double[]> Parse(string line)
        {
            if (line == null || !line.StartsWith("net ")) return null;
            var parts = line.Split(' '); int ms; if (parts.Length < 2 || !int.TryParse(parts[1], out ms) || ms <= 0) return null;
            var d = new Dictionary<int, double[]>();
            for (int i = 2; i < parts.Length; i++)
            {
                var f = parts[i].Split(':'); int pid; long s, r;
                if (f.Length == 3 && int.TryParse(f[0], out pid) && long.TryParse(f[1], out s) && long.TryParse(f[2], out r) && s >= 0 && r >= 0) d[pid] = new[] { s * 1000.0 / ms, r * 1000.0 / ms };
            }
            return d;
        }
        // one Kernel-Network event: its UserData starts with the process id (uint) and the size (uint)
        public static void Count(IntPtr rec, Dictionary<int, long[]> counts)
        {
            int id = (ushort)Marshal.ReadInt16(rec, 40); int len = (ushort)Marshal.ReadInt16(rec, 86); var data = Marshal.ReadIntPtr(rec, 96);
            if (len < 8 || data == IntPtr.Zero) return;
            int pid = Marshal.ReadInt32(data, 0); long size = (uint)Marshal.ReadInt32(data, 4);
            long[] c; if (!counts.TryGetValue(pid, out c)) counts[pid] = c = new long[2];
            if (IsSend(id)) c[0] += size; else c[1] += size;
        }

        // the helper ("--netetw <pipe> <pid>"): runs elevated after you said yes to UAC; only for the Hallow Tasks that asked for it
        public static int Run(string pipe, string pidText)
        {
            int parentPid; if (!ValidPipe(pipe) || !int.TryParse(pidText, out parentPid)) return 2;
            Process parent;
            try
            {
                parent = Process.GetProcessById(parentPid);
                var own = Assembly.GetExecutingAssembly().Location; string theirs = null; try { theirs = parent.MainModule.FileName; } catch { }
                if (TaskSettings.TestDir == null && !string.Equals(own, theirs, StringComparison.OrdinalIgnoreCase)) return 3;   // never for anyone else
            }
            catch { return 3; }
            try
            {
                using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out))
                {
                    client.Connect(10000);
                    var w = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    var counts = new Dictionary<int, long[]>(); var gate = new object();
                    var etw = new EtwSession("HallowTasksNet", new[] { KernelNetwork }, new[] { 0x30UL }, rec => { lock (gate) Count(rec, counts); }) { EventIds = new[] { Ids } };
                    if (!etw.Start()) { w.WriteLine(etw.Denied ? "error denied" : "error " + etw.LastError); return 1; }
                    try
                    {
                        w.WriteLine("ok started"); var sw = Stopwatch.StartNew();
                        while (!parent.HasExited)
                        {
                            Thread.Sleep(1000);
                            Dictionary<int, long[]> snap; lock (gate) { snap = new Dictionary<int, long[]>(counts); counts.Clear(); }
                            w.WriteLine(Line((int)Math.Max(1, sw.ElapsedMilliseconds), snap)); sw.Restart();
                        }
                    }
                    catch (IOException) { }   // Hallow Tasks closed the pipe: stop
                    finally { etw.Stop(); }
                }
                return 0;
            }
            catch { return 1; }
        }
    }

    // Hallow Tasks' side: the pipe, the elevated start, the newest numbers
    public class NetPrecise
    {
        public bool On { get; private set; }
        public bool Starting { get; private set; }
        public string State = "off";
        Dictionary<int, double[]> rates = new Dictionary<int, double[]>(); DateTime at = DateTime.MinValue; NamedPipeServerStream server; readonly object gate = new object();
        public static string NewPipeName() { return "HallowTasksNet." + Guid.NewGuid().ToString("N"); }
        public static NamedPipeServerStream MakeServer(string name)
        {
            var sec = new PipeSecurity();
            sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            return new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, sec);
        }
        // starts the helper elevated (UAC) - only when you switch it on. done gets null when it runs, else why not.
        public void Start(Action<string> done)
        {
            if (On || Starting) { done(null); return; }
            Starting = true; State = "starting";
            new Thread(() =>
            {
                string err = null;
                try
                {
                    var name = NewPipeName(); var s = MakeServer(name);
                    var psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--netetw " + name + " " + Process.GetCurrentProcess().Id) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                    try { Process.Start(psi); }
                    catch (Win32Exception e) { s.Dispose(); err = e.NativeErrorCode == 1223 ? "You said no in the Windows prompt, so the Network column keeps counting connections." : e.Message; }
                    if (err == null) err = Attach(s, 30000);
                }
                catch (Exception e) { err = e.Message; }
                Starting = false; if (err != null) State = "off";
                done(err);
            }) { IsBackground = true, Name = "precise network" }.Start();
        }
        // waits for the helper, reads its first line, then keeps reading on this thread's successor
        public string Attach(NamedPipeServerStream s, int waitMs)
        {
            var ar = s.BeginWaitForConnection(null, null);
            if (!ar.AsyncWaitHandle.WaitOne(waitMs)) { s.Dispose(); return "The helper didn't start (no answer in " + waitMs / 1000 + " s)."; }
            try { s.EndWaitForConnection(ar); } catch (Exception e) { s.Dispose(); return e.Message; }
            var r = new StreamReader(s, Encoding.UTF8); var first = r.ReadLine();
            if (first != "ok started") { s.Dispose(); return first == "error denied" ? "Windows didn't allow the network trace (it needs admin)." : "The helper couldn't start the network trace (" + (first ?? "no answer") + ")."; }
            lock (gate) server = s;
            On = true; State = "on";
            new Thread(() =>
            {
                try { string line; while ((line = r.ReadLine()) != null) Feed(line); } catch { }
                lock (gate) { if (server == s) server = null; } On = false; State = "off"; try { s.Dispose(); } catch { }
            }) { IsBackground = true, Name = "precise network reader" }.Start();
            return null;
        }
        public void Feed(string line) { var d = NetHelper.Parse(line); if (d == null) return; lock (gate) { rates = d; at = DateTime.Now; } }
        // closing the pipe stops the helper (and its trace)
        public void Stop() { NamedPipeServerStream s; lock (gate) { s = server; server = null; } if (s != null) try { s.Dispose(); } catch { } On = false; State = "off"; lock (gate) rates = new Dictionary<int, double[]>(); }
        public void FakeOn() { On = true; State = "on"; }
        // the newest numbers onto a sample (only while they're fresh)
        public void Apply(IList<ProcInfo> procs)
        {
            Dictionary<int, double[]> r; DateTime t; lock (gate) { r = rates; t = at; }
            bool fresh = On && (DateTime.Now - t).TotalSeconds < 4;
            foreach (var p in procs) { double[] v; if (fresh && r.TryGetValue(p.Pid, out v)) { p.NetSend = v[0]; p.NetRecv = v[1]; } else if (fresh) { p.NetSend = 0; p.NetRecv = 0; } else { p.NetSend = -1; p.NetRecv = -1; } }
        }
    }
}
