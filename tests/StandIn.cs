// A stand-in "game" for Hallow Tasks' Close game tests - never a real game. It shows one small window far off-screen (a tool
// window that never takes the focus or shows in the taskbar), so WM_CLOSE has something to close.
//   StandIn.exe close  <seconds> [child.exe ...]   closes when asked (WM_CLOSE)
//   StandIn.exe ignore <seconds> [child.exe ...]   ignores WM_CLOSE (a game that hangs on exit) - only TerminateProcess ends it
//   StandIn.exe hang   <seconds>                   1.4: stops reading its messages right after its window shows ("Not responding"
//                                                  after 5 s) - an app that hangs; only TerminateProcess ends it before <seconds>
// Each child (a copy of Dummy.exe) is started first with <seconds>, so the stand-in has a process tree like a game.
// It always quits by itself after <seconds> (at most 10 minutes), so nothing is left behind.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

static class StandIn
{
    class Game : Form
    {
        readonly bool ignore;
        public Game(bool ignore, int secs)
        {
            this.ignore = ignore; Text = "Hallow Tasks stand-in game"; StartPosition = FormStartPosition.Manual; Location = new Point(-32000, -32000);
            Size = new Size(160, 90); FormBorderStyle = FormBorderStyle.None;
            var t = new Timer { Interval = Math.Max(1, Math.Min(600, secs)) * 1000 }; t.Tick += (s, e) => Environment.Exit(0); t.Start();
        }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x80 | 0x08000000; return p; } }   // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        protected override bool ShowWithoutActivation { get { return true; } }
        // "ignore": a game that hangs on exit - WM_CLOSE is swallowed before WinForms sees it, and any close is cancelled
        protected override void WndProc(ref Message m) { if (ignore && m.Msg == 0x0010) return; base.WndProc(ref m); }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (ignore) e.Cancel = true; base.OnFormClosing(e); }
    }
    [STAThread]
    static void Main(string[] a)
    {
        bool ignore = a.Length > 0 && a[0] == "ignore", hang = a.Length > 0 && a[0] == "hang"; int secs = 60; if (a.Length > 1) int.TryParse(a[1], out secs);
        for (int i = 2; i < a.Length; i++) Process.Start(new ProcessStartInfo(a[i], secs.ToString()) { UseShellExecute = false, CreateNoWindow = true });
        var g = new Game(ignore, secs);
        if (hang) g.Shown += (s, e) => g.BeginInvoke(new Action(() => { System.Threading.Thread.Sleep(Math.Max(1, Math.Min(600, secs)) * 1000); Environment.Exit(0); }));
        Application.Run(g);
    }
}
