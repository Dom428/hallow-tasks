// Hallow Tasks 1.1 pages: History ("what spiked my PC"), Services, and the new Settings cards (Look, History and alerts, Precise
// network, Microsoft services).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HallowTasks
{
    public partial class MainWindow
    {
        // ================= History
        string histRange = "hour"; Timeline timeline; TextBlock histInfo; DateTime histBuilt;
        FrameworkElement HistoryPage()
        {
            var sp = PageBody("History - what spiked my PC", "Which apps took a lot of CPU, GPU or disk, and when. While a game ran: the stutter moments, and what else was busy right then. Kept in memory for an hour" + (c.S.HistoryLog ? "; the 24-hour log is on." : "; Settings can keep a 24-hour log on disk."));
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var chipsRow = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var r in new[] { new[] { "10m", "Last 10 minutes" }, new[] { "hour", "Last hour" }, new[] { "day", "Last 24 hours" } })
            {
                var id = r[0]; var t = new ToggleButton { Content = r[1], Style = Ui.S("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = histRange == id };
                if (id == "day" && c.HistLog == null) { t.IsEnabled = false; t.ToolTip = "Turn on \"Keep a 24-hour log\" in Settings"; ToolTipService.SetShowOnDisabled(t, true); }
                t.Click += (s, e) => { histRange = id; BuildPage(); };
                chipsRow.Children.Add(t);
            }
            var rb = Ui.Btn("Refresh", () => BuildPage()); rb.Margin = new Thickness(6, 0, 0, 0); chipsRow.Children.Add(rb);
            DockPanel.SetDock(chipsRow, Dock.Left); bar.Children.Add(chipsRow);
            histInfo = Ui.Muted("", 11.5); histInfo.HorizontalAlignment = HorizontalAlignment.Right; histInfo.VerticalAlignment = VerticalAlignment.Center; histInfo.FontFamily = Ui.Mono; bar.Children.Add(histInfo);
            sp.Children.Add(bar);

            DateTime to = Snap.On ? DateTime.Now : DateTime.Now, from = histRange == "10m" ? to.AddMinutes(-10) : histRange == "day" ? to.AddHours(-24) : to.AddHours(-1);
            List<HistPoint> pts; List<Stutter> sts; List<Spike> spikes;
            Gather(from, out pts, out sts, out spikes);
            histBuilt = DateTime.Now;

            // the timeline
            var tbox = new StackPanel();
            var th = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var legend = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var l in new[] { new[] { "PC CPU", "GraphCpu" }, new[] { "PC GPU", "Graph3D" }, new[] { "Game running", "GameBand" }, new[] { "Spike", "SpikeMark" }, new[] { "Stutter", "StutterMark" } })
            { var sw = new Border { Width = 10, Height = l[1] == "GameBand" ? 10 : 3, Background = Ui.R(l[1]), BorderBrush = l[1] == "GameBand" ? Ui.R("Green") : null, BorderThickness = new Thickness(l[1] == "GameBand" ? 1 : 0), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center }; var t = Ui.Muted(l[0], 11); t.Margin = new Thickness(0, 0, 14, 0); legend.Children.Add(Ui.Row(sw, t)); }
            DockPanel.SetDock(legend, Dock.Right); th.Children.Add(legend); th.Children.Add(Ui.Caps("Timeline", 12));
            tbox.Children.Add(th);
            timeline = new Timeline { Height = 170, From = from, To = to, Points = pts, Stutters = sts, Spikes = spikes };
            tbox.Children.Add(timeline);
            var tc = Ui.Card(tbox, 14); tc.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(tc);
            if (pts.Count == 0)
                tbox.Children.Add(Ui.Muted(c.S.HistoryInTray ? "Nothing recorded yet - History keeps a point every 5 s in the tray and every second while this window is open." : "Nothing recorded yet. History only records while this window is open - Settings can keep it running in the tray (a sample every 5 s).", 12));

            // what spiked, and the stutters
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel();
            left.Children.Add(Ui.Caps("What spiked", 12));
            left.Children.Add(Ui.Muted("Apps that took 15%+ CPU, 20%+ GPU or 20+ MB/s of disk - biggest first. Your game isn't listed (it's meant to be busy).", 11.5, 8));
            if (spikes.Count == 0) left.Children.Add(Ui.T("Nothing spiked in this time.", 12.5, false, Ui.R("TextSoft")));
            foreach (var s in spikes.Take(14))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
                var when = Ui.Num(s.From.ToString(histRange == "day" ? "ddd HH:mm" : "HH:mm:ss") + (s.To > s.From ? "\u2013" + s.To.ToString(histRange == "day" ? "HH:mm" : "HH:mm:ss") : ""), 11.5, Ui.R("Muted"));
                when.Width = histRange == "day" ? 140 : 132; when.VerticalAlignment = VerticalAlignment.Top; when.Margin = new Thickness(0, 2, 8, 0); DockPanel.SetDock(when, Dock.Left); row.Children.Add(when);
                var tags = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
                if (s.InGame) tags.Children.Add(Ui.Pill("While you played", "Green", "GreenBg"));
                if (s.Kind == 2) { var pl = Ui.Pill("Anti-cheat", "Amber", "AmberBg"); pl.Margin = new Thickness(6, 0, 0, 0); tags.Children.Add(pl); }
                else if (s.Kind == 3) { var pl = Ui.Pill("Windows", "Lilac", "LilacBg"); pl.Margin = new Thickness(6, 0, 0, 0); tags.Children.Add(pl); }
                DockPanel.SetDock(tags, Dock.Right); row.Children.Add(tags);
                var txt = new StackPanel(); var nm = Ui.T(s.Title, 12.5, true); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.TextWrapping = TextWrapping.NoWrap; txt.Children.Add(nm);
                var nums = Ui.Num("peak " + s.What + (s.Ram > 0 ? " \u00B7 " + Fmt.Bytes(s.Ram) + " RAM" : "") + " \u00B7 " + Dur(s.To - s.From), 11, Ui.R("Lilac")); nums.TextWrapping = TextWrapping.Wrap; txt.Children.Add(nums);
                row.Children.Add(txt);
                left.Children.Add(row);
            }
            var lc = Ui.Card(left, 14); grid.Children.Add(lc);
            var right = new StackPanel();
            right.Children.Add(Ui.Caps("Stutter moments", 12));
            right.Children.Add(Ui.Muted(sts.Any(x => x.Source == "overlay") ? "From Hallow Overlay's live numbers (a sudden drop of the 1% low), with what else was busy right then." : "While a game runs: from Hallow Overlay's live numbers when Overlay runs, otherwise the moments the CPU was nearly full or another app suddenly jumped.", 11.5, 8));
            if (sts.Count == 0) right.Children.Add(Ui.T(pts.Any(p => p.Game != null) ? "No stutter moments while you played in this time." : "No game ran in this time.", 12.5, false, Ui.R("TextSoft")));
            foreach (var s in sts.OrderByDescending(x => x.At).Take(12))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
                var when = Ui.Num(s.At.ToString(histRange == "day" ? "ddd HH:mm" : "HH:mm:ss"), 11.5, Ui.R("Red")); when.Width = 84; when.VerticalAlignment = VerticalAlignment.Top; when.Margin = new Thickness(0, 2, 8, 0); DockPanel.SetDock(when, Dock.Left); row.Children.Add(when);
                var src = Ui.Pill(s.Source == "overlay" ? "Overlay" : s.Source == "log" ? "Log" : "CPU/GPU peak", "Lilac", "LilacBg"); src.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(src, Dock.Right); row.Children.Add(src);
                var txt = new StackPanel(); txt.Children.Add(Ui.T(s.What, 12.5, true));
                if (!string.IsNullOrEmpty(s.Cause)) { var cz = Ui.Muted("Busy then: " + s.Cause, 11.5); txt.Children.Add(cz); }
                row.Children.Add(txt);
                right.Children.Add(row);
            }
            List<string[]> notes; lock (c.Notes) notes = c.Notes.ToList();
            if (notes.Count > 0)
            {
                var nh = Ui.Caps("Alerts this session", 11); nh.Margin = new Thickness(0, 10, 0, 4); right.Children.Add(nh);
                foreach (var n in notes.AsEnumerable().Reverse().Take(6)) right.Children.Add(Ui.Muted(n[0] + "  " + n[1] + " - " + n[2], 11.5, 3));
            }
            var rc = Ui.Card(right, 14); Grid.SetColumn(rc, 2); grid.Children.Add(rc);
            sp.Children.Add(grid);
            UpdateHistInfo(pts);
            return Scroll(sp);
        }
        static string Dur(TimeSpan t) { return t.TotalSeconds < 5 ? "a moment" : t.TotalMinutes < 1 ? (int)t.TotalSeconds + " s" : t.TotalHours < 1 ? (int)t.TotalMinutes + " min" : t.TotalHours.ToString("0.0") + " h"; }
        void Gather(DateTime from, out List<HistPoint> pts, out List<Stutter> sts, out List<Spike> spikes)
        {
            if (histRange == "day" && c.HistLog != null)
            {
                pts = c.HistLog.Read(from);
                var mem = c.Hist.Points(from); var lastLogged = pts.Count == 0 ? DateTime.MinValue : pts.Max(p => p.At).AddMinutes(1);
                sts = pts.Where(p => p.Stutters > 0).Select(p => new Stutter { At = p.At, Source = "log", What = p.Stutters + " stutter moment" + (p.Stutters == 1 ? "" : "s") + (p.Game != null ? " in " + p.Game : "") }).ToList();
                sts.AddRange(c.Hist.Stutters(lastLogged));
                spikes = HistoryStore.FindSpikes(pts, 60, 130).Concat(HistoryStore.FindSpikes(mem.Where(p => p.At >= lastLogged).ToList())).OrderByDescending(s => s.Score).ToList();
                pts.AddRange(mem.Where(p => p.At >= lastLogged));
                return;
            }
            pts = c.Hist.Points(from); sts = c.Hist.Stutters(from); spikes = HistoryStore.FindSpikes(pts);
        }
        void UpdateHistInfo(List<HistPoint> pts)
        {
            if (histInfo == null) return;
            histInfo.Text = pts.Count + " points · " + (c.Active || Snap.On ? "1/s now" : "") + (c.S.HistoryInTray ? (c.Active || Snap.On ? ", " : "") + "5 s in the tray" : "") + (c.HistLog != null ? " · log " + Fmt.Bytes(c.HistLog.Size) : "");
        }
        // a new sample: the timeline moves on (the lists only on Refresh, so they don't jump while you read them)
        void RefreshHistory()
        {
            if (timeline == null || histRange == "day") return;
            var to = DateTime.Now; var from = histRange == "10m" ? to.AddMinutes(-10) : to.AddHours(-1);
            timeline.From = from; timeline.To = to; timeline.Points = c.Hist.Points(from); timeline.Stutters = c.Hist.Stutters(from); timeline.InvalidateVisual();
            UpdateHistInfo(timeline.Points);
        }

        // ================= Services
        string svcSearch = "", svcFilter = "All", svcSel;
        public void SnapServices(string name) { svcSel = name; }
        FrameworkElement ServicesPage()
        {
            var outer = new DockPanel { Margin = new Thickness(22, 16, 22, 12) };
            var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top); outer.Children.Add(head);
            head.Children.Add(Ui.T("Services", 20, true, null, 2));
            head.Children.Add(Ui.Muted("Windows services: status, startup type, PID and what they do. Start, stop or restart the ones Windows lets you; the rest say \"admin\" - Windows asks you (UAC) before anything happens. Windows' own services are marked \"leave alone\".", 12, 12));
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var chipsRow = new StackPanel { Orientation = Orientation.Horizontal };
            var chipList = new List<ToggleButton>();
            foreach (var f in new[] { "All", "Running", "Not Microsoft" })
            {
                var ff = f; var t = new ToggleButton { Content = f, Style = Ui.S("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = svcFilter == f };
                t.Click += (s, e) => { svcFilter = ff; BuildPage(); }; chipList.Add(t); chipsRow.Children.Add(t);
            }
            var rf = Ui.Btn(c.ServicesReading ? "Loading..." : "Refresh", () => c.RefreshServices()); rf.Margin = new Thickness(6, 0, 0, 0); chipsRow.Children.Add(rf);
            DockPanel.SetDock(chipsRow, Dock.Left); bar.Children.Add(chipsRow);
            var sbox = new Grid { Width = 240, HorizontalAlignment = HorizontalAlignment.Right };
            var field = Ui.Field(svcSearch, 240); field.ToolTip = "Search by name, description or PID";
            var hint = Ui.Muted("Search", 12); hint.Margin = new Thickness(11, 0, 0, 0); hint.VerticalAlignment = VerticalAlignment.Center; hint.IsHitTestVisible = false; hint.FontFamily = Ui.Mono; hint.Visibility = svcSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            sbox.Children.Add(field); sbox.Children.Add(hint); bar.Children.Add(sbox);
            DockPanel.SetDock(bar, Dock.Top); outer.Children.Add(bar);

            var table = new StackPanel();
            var hdr = SvcGrid();
            Action<int, string, HorizontalAlignment> h = (col, t, al) => { var tb = Ui.Muted(t, 11); tb.HorizontalAlignment = al; tb.Margin = new Thickness(6, 0, 6, 6); Grid.SetColumn(tb, col); hdr.Children.Add(tb); };
            h(0, "Name", HorizontalAlignment.Left); h(1, "Status", HorizontalAlignment.Left); h(2, "Startup", HorizontalAlignment.Left); h(3, "PID", HorizontalAlignment.Right); h(4, "Description", HorizontalAlignment.Left); h(5, "", HorizontalAlignment.Left);
            var hwrap = new Border { Child = hdr, BorderBrush = Ui.R("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(12, 10, 22, 0) };
            var rowsPanel = new StackPanel { Margin = new Thickness(12, 0, 12, 10) };
            var card = new Border { Background = Ui.R("Panel"), BorderBrush = Ui.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
            var cd = new DockPanel(); DockPanel.SetDock(hwrap, Dock.Top); cd.Children.Add(hwrap);
            cd.Children.Add(Scroll(rowsPanel)); card.Child = cd; outer.Children.Add(card);
            Action fill = () => FillServices(rowsPanel);
            field.TextChanged += (s, e) => { svcSearch = field.Text; hint.Visibility = svcSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed; fill(); };
            fill();
            return outer;
        }
        static Grid SvcGrid()
        {
            var g = new Grid();
            foreach (var w in new[] { -1.1, 82, 150, 64, -1, 250 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(-w, GridUnitType.Star) : new GridLength(w) });
            return g;
        }
        void FillServices(StackPanel panel)
        {
            panel.Children.Clear();
            if (c.ServiceList == null) { panel.Children.Add(Ui.Muted("Reading the services...", 12.5)); return; }
            var q = svcSearch.Trim();
            var list = c.ServiceList.Where(s => (svcFilter != "Running" || s.Running) && (svcFilter != "Not Microsoft" || !s.Microsoft) &&
                (q.Length == 0 || (s.Display ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || s.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || (s.Description ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || s.Pid.ToString() == q)).ToList();
            int i = 0;
            foreach (var s in list)
            {
                var sv = s; i++;
                var row = SvcGrid(); row.MinHeight = 38;
                bool sel = svcSel == s.Name;
                var bg = new Border { Background = sel ? Ui.R("RowSel") : i % 2 == 0 ? Ui.R("RowChild") : Ui.R("Heat0"), BorderBrush = Ui.R("LineDim"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
                bg.MouseLeftButtonDown += (o, e) => { svcSel = sv.Name; FillServices(panel); };
                var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 3, 6, 3) };
                var t1 = Ui.T(s.Display ?? s.Name, 12.5, true); t1.TextWrapping = TextWrapping.NoWrap; t1.TextTrimming = TextTrimming.CharacterEllipsis; nm.Children.Add(t1);
                var t2 = Ui.Muted(s.Name, 10.5); t2.FontFamily = Ui.Mono; t2.TextWrapping = TextWrapping.NoWrap; t2.TextTrimming = TextTrimming.CharacterEllipsis; nm.Children.Add(t2);
                row.Children.Add(nm);
                var st = Ui.T(s.Status, 12, s.Running, Ui.R(s.Running ? "Green" : s.Status == "Stopped" ? "Muted" : "Amber")); st.VerticalAlignment = VerticalAlignment.Center; st.Margin = new Thickness(6, 0, 6, 0); Grid.SetColumn(st, 1); row.Children.Add(st);
                var su = Ui.T(s.StartType, 11.5, false, Ui.R(s.StartType == "Disabled" ? "Muted" : "TextSoft")); su.VerticalAlignment = VerticalAlignment.Center; su.Margin = new Thickness(6, 0, 6, 0); Grid.SetColumn(su, 2); row.Children.Add(su);
                var pid = Ui.Num(s.Pid > 0 ? s.Pid.ToString() : "-", 11.5, Ui.R("Text")); pid.HorizontalAlignment = HorizontalAlignment.Right; pid.VerticalAlignment = VerticalAlignment.Center; pid.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(pid, 3); row.Children.Add(pid);
                var ds = Ui.Muted(s.Description ?? "", 11.5); ds.VerticalAlignment = VerticalAlignment.Center; ds.Margin = new Thickness(6, 0, 6, 0);
                if (sel) ds.TextWrapping = TextWrapping.Wrap; else { ds.TextWrapping = TextWrapping.NoWrap; ds.TextTrimming = TextTrimming.CharacterEllipsis; }
                ds.ToolTip = (s.Description ?? "") + (s.Program != null ? "\n\n" + s.Program : ""); Grid.SetColumn(ds, 4); row.Children.Add(ds);
                var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                var why = c.WhyNotService(s);
                if (why != null)
                {
                    bool ac = why.StartsWith("That's anti-cheat"); var pl = Ui.Pill(ac ? "Anti-cheat — leave alone" : "Windows — leave alone", ac ? "Amber" : "Lilac", ac ? "AmberBg" : "LilacBg"); pl.ToolTip = why; acts.Children.Add(pl);
                }
                else
                {
                    var suffix = s.CanControl ? "" : " (admin)";
                    Action<string, string> btn = (op, label) =>
                    {
                        var b = Ui.Btn(label + suffix, () => ServiceAction(sv, op), false, s.CanControl ? null : "Needs admin: Windows asks you first (UAC), then a small helper does just this.");
                        b.Padding = new Thickness(8, 3, 8, 3); b.Margin = new Thickness(6, 0, 0, 0); b.IsEnabled = c.ServiceBusy == null && !(op == "start" && s.StartType == "Disabled") && !(op != "start" && !s.CanStop);
                        acts.Children.Add(b);
                    };
                    if (s.Running) { btn("stop", "Stop"); btn("restart", "Restart"); } else btn("start", "Start");
                }
                Grid.SetColumn(acts, 5); row.Children.Add(acts);
                panel.Children.Add(bg);
            }
            if (list.Count == 0) panel.Children.Add(Ui.Muted("No service matches.", 12.5));
        }
        void ServiceAction(ServiceInfo s, string op)
        {
            var nm = s.Display ?? s.Name; var word = op == "start" ? "Start" : op == "stop" ? "Stop" : "Restart";
            if (op != "start" || !s.CanControl)
            {
                var text = (!s.CanControl ? "This needs admin: Windows will ask you first (UAC). " : "") + (op == "start" ? "It starts now (its startup type doesn't change)." : "Programs that use it may stop working until it runs again." + (op == "restart" ? " It starts again right after." : ""));
                if (!Dialogs.Confirm(this, word + " " + nm + "?", text, word + (s.CanControl ? "" : " (admin)"), op == "stop")) return;
            }
            Say((op == "start" ? "Starting " : op == "stop" ? "Stopping " : "Restarting ") + nm + "...");
            c.ControlService(s, op, e => { if (e != null) Dialogs.Info(this, "Hallow Tasks", e); else Say(nm + (op == "start" ? " is running." : op == "stop" ? " stopped." : " restarted.")); });
            if (page == "services") BuildPage();
        }

        // ================= Settings cards (1.1)
        FrameworkElement LookCard()
        {
            var b = new StackPanel();
            b.Children.Add(Ui.Caps("Look", 11));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
            var chips = new List<ToggleButton>();
            foreach (var o in new[] { new[] { "hallow", "Follow Hallow" }, new[] { "light", "Light" }, new[] { "dark", "Dark" } })
            {
                var id = o[0]; var t = new ToggleButton { Content = o[1], Style = Ui.S("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = (c.S.Look ?? "hallow") == id };
                t.Click += (s, e) => { c.SetLook(id); BuildPage(); };
                row.Children.Add(t);
            }
            b.Children.Add(row);
            var mode = Look.ReadMode();
            b.Children.Add(Ui.Muted("Follow Hallow uses Hallow Toolkit's Light / Dark / Same as Windows (now: " + (mode == "system" ? "same as Windows" : mode) + "). Light or Dark here is for Hallow Tasks only. It changes at once - no restart.", 11.5, 0));
            var card = Ui.Card(b); card.Margin = new Thickness(0, 12, 0, 0); return card;
        }
        FrameworkElement HistoryCard()
        {
            var b = new StackPanel();
            b.Children.Add(Ui.Caps("History and alerts", 11));
            b.Children.Add(Ui.Switch("Keep History while Hallow Tasks is in the tray (a sample every 5 s - well under 1% CPU)", c.S.HistoryInTray, on => { c.S.HistoryInTray = on; c.S.Save(); }));
            b.Children.Add(Ui.Switch("Keep a 24-hour log on disk (one line a minute in the data folder, at most about 1 MB)", c.S.HistoryLog, on => c.SetHistoryLog(on)));
            b.Children.Add(Ui.Switch("Quiet alerts while you play: a note through Hallow Hub when a background app is busy (never a pop-up over the game)", c.S.Alerts, on => { c.S.Alerts = on; c.S.Save(); }));
            var row = new WrapPanel { Margin = new Thickness(52, 2, 0, 4) };
            Func<string, int, TextBox> num = (label, v) => { var l = Ui.Muted(label, 12); l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(0, 0, 6, 0); row.Children.Add(l); var f = Ui.Field(v.ToString(), 56); f.Margin = new Thickness(0, 0, 14, 0); row.Children.Add(f); return f; };
            var cpu = num("CPU over", c.S.AlertCpu); var gpu = num("% or GPU over", c.S.AlertGpu); var secs = num("% for more than", c.S.AlertSeconds);
            var sl = Ui.Muted("seconds", 12); sl.VerticalAlignment = VerticalAlignment.Center; sl.Margin = new Thickness(0, 0, 14, 0); row.Children.Add(sl);
            row.Children.Add(Ui.Btn("Save", () =>
            {
                int a, g, s; if (!int.TryParse(cpu.Text, out a) || !int.TryParse(gpu.Text, out g) || !int.TryParse(secs.Text, out s)) { Say("Numbers only, please."); return; }
                c.S.AlertCpu = Math.Max(5, Math.Min(100, a)); c.S.AlertGpu = Math.Max(5, Math.Min(100, g)); c.S.AlertSeconds = Math.Max(5, Math.Min(600, s)); c.S.Save();
                Say("Alerts: over " + c.S.AlertCpu + "% CPU or " + c.S.AlertGpu + "% GPU for more than " + c.S.AlertSeconds + " s while you play."); BuildPage();
            }, true));
            b.Children.Add(row);
            b.Children.Add(Ui.Muted("Alerts need Hallow Hub running (they show from its icon); without it they only appear in History. At most one per app every 10 minutes.", 11.5, 0));
            var card = Ui.Card(b); card.Margin = new Thickness(0, 12, 0, 0); return card;
        }
        FrameworkElement NetworkCard()
        {
            var b = new StackPanel();
            b.Children.Add(Ui.Caps("Network and services", 11));
            var sw = Ui.Switch("Precise network (needs admin) - bytes per second per app in the Network column" + (c.Net.Starting ? " (starting...)" : ""), c.Net.On, null);
            sw.Click += (s, e) =>
            {
                bool on = sw.IsChecked == true;
                if (on && !Dialogs.Confirm(this, "Turn on precise network?", "Windows will ask you for admin (UAC). Then a small helper (Hallow Tasks itself) runs a Windows network trace that reads only which app sent or received how many bytes - never addresses or contents - and passes the numbers to this window. It stops when you switch it off or close Hallow Tasks, and never starts by itself.", "Ask Windows", false)) { sw.IsChecked = false; return; }
                Say(on ? "Waiting for the Windows prompt..." : "Precise network is off.");
                c.SetPreciseNetwork(on, err => { if (err != null) { Dialogs.Info(this, "Hallow Tasks", err); } else if (on) Say("Precise network is on until Hallow Tasks closes."); if (page == "settings") BuildPage(); });
            };
            b.Children.Add(sw);
            b.Children.Add(Ui.Muted("Without it, the Network column counts each app's open connections (no admin needed). It's off again the next time Hallow Tasks starts.", 11.5, 4));
            b.Children.Add(Ui.Switch("Let me start and stop Microsoft's own services (off: they're \"leave alone\" in Services)", c.S.ControlMicrosoftServices, on => { c.S.ControlMicrosoftServices = on; c.S.Save(); }));
            var card = Ui.Card(b); card.Margin = new Thickness(0, 12, 0, 0); return card;
        }
    }
}
