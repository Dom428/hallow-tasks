// Hallow Tasks - the small themed dialogs: "End ...?" (always asked before ending anything), Set priority, Set affinity (with its
// warning), a message, and the crash notice. Each is a Window built in code; --snap renders them without showing them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowTasks
{
    public static class Dialogs
    {
        static Window Make(string title, double width, Window owner)
        {
            var w = new Window(); Ui.Dress(w, title, width);
            w.ResizeMode = ResizeMode.NoResize; w.ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; } else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return w;
        }
        static StackPanel Body(Window w) { var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) }; w.Content = sp; return sp; }
        static WrapPanel Buttons(StackPanel sp) { var row = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right }; sp.Children.Add(row); return row; }

        public static Window ConfirmWindow(string title, string text, string okLabel, bool danger, Action<bool> done)
        {
            var w = Make("Hallow Tasks", 460, null); var sp = Body(w);
            sp.Children.Add(Ui.T(title, 15, true, null, 6)); sp.Children.Add(Ui.Muted(text, 12.5));
            var row = Buttons(sp); bool answer = false;
            row.Children.Add(Ui.Btn("Cancel", () => w.Close()));
            var ok = danger ? Ui.Danger(okLabel, () => { answer = true; w.Close(); }) : Ui.Btn(okLabel, () => { answer = true; w.Close(); }, true); ok.Margin = new Thickness(0); ok.MinWidth = 90;
            row.Children.Add(ok);
            w.Closed += (s, e) => { if (done != null) done(answer); };
            w.Loaded += (s, e) => ((Button)row.Children[0]).Focus();   // Cancel has the focus: Enter never ends anything by accident
            return w;
        }
        public static bool Confirm(Window owner, string title, string text, string okLabel, bool danger)
        {
            bool r = false; var w = ConfirmWindow(title, text, okLabel, danger, a => r = a); Own(w, owner); w.ShowDialog(); return r;
        }
        static void Own(Window w, Window owner) { if (owner != null && owner.IsVisible) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; } }

        public static Window InfoWindow(string title, string text, Action done)
        {
            var w = Make("Hallow Tasks", 460, null); var sp = Body(w);
            sp.Children.Add(Ui.T(title, 15, true, null, 6)); sp.Children.Add(Ui.Muted(text, 12.5));
            var row = Buttons(sp); var ok = Ui.Btn("OK", () => w.Close(), true); ok.Margin = new Thickness(0); ok.MinWidth = 80; row.Children.Add(ok);
            w.Closed += (s, e) => { if (done != null) done(); };
            return w;
        }
        public static void Info(Window owner, string title, string text) { var w = InfoWindow(title, text, null); Own(w, owner); w.ShowDialog(); }

        public static Window PriorityWindow(string name, Prio current, Action<Prio?> done)
        {
            var w = Make("Hallow Tasks", 420, null); var sp = Body(w);
            sp.Children.Add(Ui.T("Priority of " + name, 15, true, null, 6));
            sp.Children.Add(Ui.Muted("Lower priority = it gets the CPU after everything else. High can make other apps (and your game) stutter. Realtime isn't offered. It lasts until the program closes.", 12, 12));
            Prio? pick = null;
            foreach (var p in new[] { Prio.High, Prio.AboveNormal, Prio.Normal, Prio.BelowNormal, Prio.Idle })
            {
                var pp = p;
                var b = new Button { Style = Ui.S("Btn"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(12, 7, 12, 7) };
                var line = new DockPanel();
                var word = new TextBlock { Text = PriorityKeeper.Word(p), FontWeight = p == current ? FontWeights.SemiBold : FontWeights.Normal, Width = 120 };
                line.Children.Add(word);
                if (p == current) line.Children.Add(Ui.Pill("now", "AccentBrush", "AccentGlow"));
                line.Width = 330; b.Content = line; b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.Click += (s, e) => { pick = pp; w.Close(); };
                sp.Children.Add(b);
            }
            var row = Buttons(sp); var c = Ui.Btn("Cancel", () => w.Close()); c.Margin = new Thickness(0); row.Children.Add(c);
            w.Closed += (s, e) => { if (done != null) done(pick); };
            return w;
        }
        public static Prio? Priority(Window owner, string name, Prio current) { Prio? r = null; var w = PriorityWindow(name, current, p => r = p); Own(w, owner); w.ShowDialog(); return r; }

        public static Window AffinityWindow(string name, int cores, long mask, Action<long?> done)
        {
            var w = Make("Hallow Tasks", 460, null); var sp = Body(w);
            sp.Children.Add(Ui.T("Which CPUs may " + name + " use?", 15, true, null, 6));
            var warn = new Border { Background = Ui.R("AmberBg"), BorderBrush = Ui.R("Amber"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 12),
                Child = Ui.T("Careful: fewer CPUs makes the program slower, and it can stutter or hang. Never needed for games - and never change a game's or anti-cheat's affinity (Hallow Tasks won't). It lasts until the program closes.", 12, false, Ui.R("Text")) };
            sp.Children.Add(warn);
            var grid = new WrapPanel(); var boxes = new List<CheckBox>();
            for (int i = 0; i < Math.Min(cores, 64); i++)
            {
                var cb = new CheckBox { Content = new TextBlock { Text = "CPU " + i, FontFamily = Ui.Mono, FontSize = 11.5 }, IsChecked = (mask >> i & 1) == 1, Style = Ui.S("Tick"), Width = 96, Margin = new Thickness(0, 2, 6, 2) };
                boxes.Add(cb); grid.Children.Add(cb);
            }
            sp.Children.Add(grid);
            var msg = Ui.Muted("", 12); msg.Foreground = Ui.R("Red"); sp.Children.Add(msg);
            long? pick = null;
            var row = Buttons(sp);
            row.Children.Add(Ui.Btn("Cancel", () => w.Close()));
            var set = Ui.Btn("Set", () =>
            {
                long m = 0; for (int i = 0; i < boxes.Count; i++) if (boxes[i].IsChecked == true) m |= 1L << i;
                if (m == 0) { msg.Text = "Pick at least one CPU."; return; }
                pick = m; w.Close();
            }, true);
            set.Margin = new Thickness(0); set.MinWidth = 80; row.Children.Add(set);
            w.Closed += (s, e) => { if (done != null) done(pick); };
            return w;
        }
        public static long? Affinity(Window owner, string name, int cores, long mask) { long? r = null; var w = AffinityWindow(name, cores, mask, p => r = p); Own(w, owner); w.ShowDialog(); return r; }
    }

    public static class CrashNotice
    {
        public static Window Make(string file)
        {
            var w = new Window(); Ui.Dress(w, "Hallow Tasks", 440);
            w.ResizeMode = ResizeMode.NoResize; w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Topmost = true;
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            sp.Children.Add(Ui.T("Hallow Tasks closed unexpectedly last time", 15, true, null, 6));
            DateTime when = DateTime.Now; try { if (File.Exists(file)) when = File.GetLastWriteTime(file); } catch { }
            sp.Children.Add(Ui.Muted("On " + when.ToString("d MMM yyyy, HH:mm") + ". A report was saved on this PC - nothing was sent anywhere.", 12.5, 14));
            var row = new WrapPanel();
            row.Children.Add(Ui.Btn("Show details", () => { try { System.Diagnostics.Process.Start("notepad.exe", "\"" + file + "\""); } catch { } }));
            row.Children.Add(Ui.Btn("Open folder", () => { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } }));
            var ok = Ui.Btn("OK", () => w.Close(), true); ok.MinWidth = 70; row.Children.Add(ok);
            sp.Children.Add(row); w.Content = sp;
            return w;
        }
    }
}
