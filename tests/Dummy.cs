// A do-nothing program for Hallow Tasks' tests: it waits (no window, no console) so the tests can end, reprioritise and pin a
// process they started themselves - never a real one. "Dummy.exe <seconds> [child]" - with "child" it starts one more Dummy first.
using System;
using System.Diagnostics;
using System.Threading;

static class Dummy
{
    static void Main(string[] a)
    {
        int secs = 60; if (a.Length > 0) int.TryParse(a[0], out secs);
        if (a.Length > 1 && a[1] == "child") Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, secs.ToString()) { UseShellExecute = false, CreateNoWindow = true });
        Thread.Sleep(Math.Max(1, Math.Min(600, secs)) * 1000);
    }
}
