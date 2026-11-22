// Hallow Tasks - the other pages: What's slowing my game, Performance (live graphs), Startup apps and Settings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowTasks
{
    public partial class MainWindow
    {
        static ScrollViewer Scroll(UIElement child) { return new ScrollViewer { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
        static StackPanel PageBody(string title, string sub)
        {
            var sp = new StackPanel { Margin = new Thickness(22, 16, 22, 20) };
            sp.Children.Add(Ui.T(title, 20, true, null, 2));
            if (sub != null) sp.Children.Add(Ui.Muted(sub, 12, 14));
            return sp;
        }

        // ================= What's slowing my game
        FrameworkElement GamePage()
        {
            var sp = PageBody("What's slowing my game", "While a game runs, the other apps ranked by the CPU, GPU and disk they took over the last 10 seconds. Lower one for as long as the game runs, or end it.");
            if (c.Game == null)
            {
                var box = new StackPanel();
                box.Children.Add(Ui.Caps("No game running", 12, Ui.R("Lilac")));
                box.Children.Add(Ui.T("Start a game and come back - this page ranks what else is using your PC while you play.", 13, false, Ui.R("Text"), 8));
                var games = Known.Games.Concat(c.S.ExtraGames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                box.Children.Add(Ui.Muted("Games it knows: " + string.Join(", ", games.Select(g => g.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? g : g + ".exe")) + ", and whatever Hallow Hub says you're playing. Add more in Settings.", 12, 8));
                box.Children.Add(Ui.Muted("It never touches the game itself or anti-cheat (SGuard, EasyAntiCheat, BattlEye, Vanguard and others) - those are marked \"Anti-cheat — leave alone\".", 12));
                sp.Children.Add(Ui.Card(box, 18));
                AddLowered(sp);
                return Scroll(sp);
            }
            // the game
            var head = new DockPanel();
            var gi = new StackPanel { Orientation = Orientation.Horizontal };
            gi.Children.Add(Ui.Tile(c.GameTitle, Ui.ProgramIcon(c.Game.Path), 28));
            var gt = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            gt.Children.Add(Ui.T(c.GameTitle, 16, true));
            var mins = c.GameSince == null ? 0 : (int)(DateTime.Now - c.GameSince.Value).TotalMinutes;
            gt.Children.Add(Ui.Muted(c.Game.Name + "  ·  " + (mins < 1 ? "just started" : "running for " + mins + " min") + "  ·  never touched", 11.5));
            gi.Children.Add(gt); head.Children.Add(gi);
            var gg = c.Groups.FirstOrDefault(g => g.Members.Any(m => m.Key == c.Game.Key));
            var nums = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Action<string, string> stat = (k, v) => { var s = new StackPanel { Margin = new Thickness(18, 0, 0, 0) }; s.Children.Add(Ui.Num(v, 16, Ui.R("Green"))); s.Children.Add(Ui.Muted(k, 10.5)); nums.Children.Add(s); };
            stat("GAME CPU", Fmt.Pct(gg != null ? gg.Cpu : c.Game.Cpu)); stat("GAME GPU", Fmt.Pct(gg != null ? gg.Gpu : c.Game.Gpu)); stat("GAME RAM", Fmt.Bytes(gg != null ? gg.Ram : c.Game.Ram));
            if (c.Last != null) { stat("PC CPU", Fmt.Pct(c.Last.Sys.Cpu)); stat("PC GPU", c.Last.Sys.GpuKnown ? Fmt.Pct(c.Last.Sys.Gpu3D) : "-"); }
            DockPanel.SetDock(nums, Dock.Right); head.Children.Insert(0, nums);
            var hc = Ui.Card(head, 14); hc.BorderBrush = Ui.R("Green"); hc.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(hc);

            var rows = c.Watch.Rank(c.Groups, c.Game, 12);
            var table = new StackPanel();
            var hdr = GameGrid();
            Action<int, string, HorizontalAlignment> h = (col, t, al) => { var tb = Ui.Muted(t, 11); tb.HorizontalAlignment = al; tb.Margin = new Thickness(6, 0, 6, 0); Grid.SetColumn(tb, col); hdr.Children.Add(tb); };
            h(0, "#", HorizontalAlignment.Left); h(1, "App", HorizontalAlignment.Left); h(2, "CPU", HorizontalAlignment.Right); h(3, "GPU", HorizontalAlignment.Right); h(4, "Disk", HorizontalAlignment.Right); h(5, "How much", HorizontalAlignment.Left); h(6, "", HorizontalAlignment.Left);
            hdr.Margin = new Thickness(0, 0, 0, 6); table.Children.Add(hdr);
            double top = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => r.Score));
            int i = 0;
            foreach (var r in rows)
            {
                i++; var row = GameGrid(); row.Margin = new Thickness(0, 0, 0, 0); row.MinHeight = 36;
                var bg = new Border { Background = i % 2 == 0 ? Ui.R("RowChild") : Ui.R("Heat0"), BorderBrush = Ui.R("LineDim"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
                var n = Ui.Num(i.ToString(), 12, Ui.R("Muted")); n.VerticalAlignment = VerticalAlignment.Center; n.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(n);
                var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(Ui.Tile(r.Group.Title, Ui.ProgramIcon(r.Group.Path), 16));
                var nt = Ui.T(r.Group.Title, 12.5, true); nt.Margin = new Thickness(8, 0, 0, 0); nt.TextWrapping = TextWrapping.NoWrap; nt.TextTrimming = TextTrimming.CharacterEllipsis; nt.MaxWidth = 210; name.Children.Add(nt);
                if (r.Group.Members.Count > 1) { var cnt = Ui.Muted("(" + r.Group.Members.Count + ")", 11); cnt.Margin = new Thickness(5, 0, 0, 0); cnt.FontFamily = Ui.Mono; name.Children.Add(cnt); }
                Grid.SetColumn(name, 1); row.Children.Add(name);
                Action<int, string, double> num = (col, t, pct) => { var tb = Ui.Num(t, 12, Ui.R(pct >= 15 ? "Amber" : "Text")); tb.HorizontalAlignment = HorizontalAlignment.Right; tb.VerticalAlignment = VerticalAlignment.Center; tb.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(tb, col); row.Children.Add(tb); };
                num(2, Fmt.Pct(r.Cpu), r.Cpu); num(3, Fmt.Pct(r.Gpu), r.Gpu); num(4, Fmt.Rate(r.Disk), r.Disk / 1048576 * 5);
                var barBack = new Border { Height = 6, Background = Ui.R("GraphBg"), CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) };
                var barFill = new Border { Height = 6, Background = Ui.R(r.Tag == Known.AntiCheatTag ? "Amber" : "AccentBrush"), CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(2, 100 * r.Score / top) };
                var barGrid = new Grid(); barGrid.Children.Add(barBack); barFill.Margin = new Thickness(6, 0, 0, 0); barGrid.Children.Add(barFill); Grid.SetColumn(barGrid, 5); row.Children.Add(barGrid);
                var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                var lowered = c.Keeper.Lowered.Where(e => r.Group.Members.Any(m => m.Pid == e.Pid && m.CreateTime == e.Created)).ToList();
                if (r.Tag != null) acts.Children.Add(Ui.Pill(r.Tag, r.Tag == Known.AntiCheatTag ? "Amber" : "Lilac", r.Tag == Known.AntiCheatTag ? "AmberBg" : "LilacBg"));
                else
                {
                    var g = r.Group;
                    bool eco = c.EcoOn(g);
                    if (eco)
                    {
                        var pl = Ui.Pill("Efficiency", "Green", "GreenBg"); pl.ToolTip = EcoKeeper.What; pl.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(pl);
                        var off = Ui.Btn("Efficiency off", () => { var e = c.SetEfficiency(g, false); if (e != null) Dialogs.Info(this, "Hallow Tasks", e); else Say("Efficiency mode is off for " + g.Title + "."); gameAt = null; Refresh(); }, false, "Back to normal (its priority too).");
                        off.Padding = new Thickness(8, 3, 8, 3); off.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(off);
                    }
                    else if (lowered.Count == 0)
                    {
                        var eb = Ui.Btn("Efficiency", () => { var e = c.SetEfficiency(g, true); if (e != null) Dialogs.Info(this, "Hallow Tasks", e); else Say(g.Title + " is in Efficiency mode - what Windows 11's own Efficiency mode does (EcoQoS + Idle priority)."); gameAt = null; Refresh(); }, false, EcoKeeper.What);
                        eb.Padding = new Thickness(8, 3, 8, 3); eb.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(eb);
                    }
                    if (lowered.Count > 0) { var pl = Ui.Pill("Lowered", "Lilac", "LilacBg"); pl.ToolTip = PriorityKeeper.Word(lowered[0].Now) + " until the game closes"; pl.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(pl); }
                    foreach (var pr in new[] { Prio.BelowNormal, Prio.Idle })
                    {
                        var to = pr; if (lowered.Count > 0 || eco) continue;
                        var b = Ui.Btn(PriorityKeeper.Word(pr), () => { var e = c.LowerGroup(g, to); if (e != null) Dialogs.Info(this, "Hallow Tasks", e); else Say(g.Title + " is at " + PriorityKeeper.Word(to) + " priority until the game closes."); gameAt = null; Refresh(); },
                            false, to == Prio.Idle ? "Only runs when nothing else wants the CPU - put back when the game closes." : "Gets the CPU after your game - put back when the game closes.");
                        b.Padding = new Thickness(8, 3, 8, 3); b.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(b);
                    }
                    if (lowered.Count > 0) { var rb = Ui.Btn("Restore", () => { c.RestoreGroup(lowered[0].Name); foreach (var e in lowered) c.Keeper.Restore(e.Pid); Say(g.Title + " is back to its normal priority."); gameAt = null; Refresh(); }); rb.Padding = new Thickness(8, 3, 8, 3); rb.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(rb); }
                    var end = Ui.Danger("End task", () =>
                    {
                        if (!Dialogs.Confirm(this, "End " + g.Title + "?", (g.Members.Count > 1 ? g.Title + " has " + g.Members.Count + " processes - all of them end. " : "") + "Anything unsaved in it is lost. Your game isn't touched.", "End task", true)) return;
                        var e = c.EndGroup(g); if (e != null) Dialogs.Info(this, "Hallow Tasks", e); else Say("Ended " + g.Title + ".");
                        gameAt = null; Refresh();
                    });
                    end.Padding = new Thickness(8, 3, 8, 3); end.Margin = new Thickness(0); acts.Children.Add(end);
                }
                Grid.SetColumn(acts, 6); row.Children.Add(acts);
                table.Children.Add(bg);
            }
            if (rows.Count == 0) table.Children.Add(Ui.Muted("Nothing else is using much right now - collecting a few seconds of numbers...", 12.5));
            sp.Children.Add(Ui.Card(table, 12));
            AddLowered(sp);
            return Scroll(sp);
        }
        static Grid GameGrid()
        {
            var g = new Grid();
            foreach (var w in new[] { 30.0, -1, 70, 70, 90, 120, 400 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            return g;
        }
        void AddLowered(StackPanel sp)
        {
            var l = c.Keeper.Lowered;
            var box = new StackPanel();
            box.Children.Add(Ui.Caps("Lowered while you play", 11));
            if (l.Count == 0) box.Children.Add(Ui.Muted("Nothing is lowered. Apps you lower here go back to their own priority when the game closes, when you click Restore, or when you quit Hallow Tasks.", 12));
            else
            {
                foreach (var e in l.ToList())
                {
                    var ee = e; var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                    var rb = Ui.Btn("Restore", () => { c.Keeper.Restore(ee.Pid); gameAt = null; Refresh(); }); rb.Padding = new Thickness(8, 3, 8, 3); rb.Margin = new Thickness(0); DockPanel.SetDock(rb, Dock.Right); row.Children.Add(rb);
                    var t = Ui.Num(ee.Name + "  PID " + ee.Pid + "   " + PriorityKeeper.Word(ee.Was) + " → " + PriorityKeeper.Word(ee.Now), 12, Ui.R("Text")); t.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(t);
                    box.Children.Add(row);
                }
                box.Children.Add(Ui.Muted("They go back by themselves when the game closes or when you quit Hallow Tasks.", 11.5));
            }
            if (c.Eco.On.Count > 0)
            {
                var eh = Ui.Caps("In Efficiency mode", 11); eh.Margin = new Thickness(0, 12, 0, 0); box.Children.Add(eh);
                foreach (var e in c.Eco.On.ToList())
                {
                    var ee = e; var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                    var ob = Ui.Btn("Efficiency off", () => { var pi = c.Last == null ? null : c.Last.Procs.FirstOrDefault(q => q.Pid == ee.Pid && q.CreateTime == ee.Created); var err = pi == null ? null : c.SetEfficiency(pi, false); if (pi == null) c.Eco.On.Remove(ee); if (err != null) Dialogs.Info(this, "Hallow Tasks", err); gameAt = null; Refresh(); });
                    ob.Padding = new Thickness(8, 3, 8, 3); ob.Margin = new Thickness(0); DockPanel.SetDock(ob, Dock.Right); row.Children.Add(ob);
                    var t = Ui.Num(ee.Name + "  PID " + ee.Pid + "   EcoQoS + Idle (was " + PriorityKeeper.Word(ee.Was) + ")", 12, Ui.R("Text")); t.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(t);
                    box.Children.Add(row);
                }
                box.Children.Add(Ui.Muted("Like Windows' own Efficiency mode, it stays until you turn it off or the app closes.", 11.5));
            }
            var card = Ui.Card(box, 14); card.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(card);
        }

        // ================= Performance
        object perfView; readonly List<Graph> graphs = new List<Graph>(); readonly Dictionary<string, TextBlock> perfText = new Dictionary<string, TextBlock>();
        FrameworkElement PerformancePage()
        {
            graphs.Clear(); perfText.Clear();
            var sp = PageBody("Performance", null); perfView = sp;
            var sub = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var pc = Ui.Switch("One graph per core", c.S.PerCore, on => { c.S.PerCore = on; c.S.Save(); BuildPage(); }); pc.Margin = new Thickness(0); DockPanel.SetDock(pc, Dock.Right); sub.Children.Add(pc);
            sub.Children.Add(Ui.Muted("The last 60 seconds, once a second while this window is open. CPU temperature isn't shown: Windows only gives it to programs with a kernel driver, and Hallow Tasks (like Hallow Overlay) doesn't install one.", 12));
            sp.Children.Add(sub);
            var grid = new UniformGrid2(2);
            grid.Add(PerfCard("CPU", "cpu", g => { if (!c.S.PerCore) g.Add(Ring("cpu"), "GraphCpu", "GraphCpuFill"); }, true));
            grid.Add(PerfCard("GPU", "gpu", g => { g.Add(Ring("gpu3d"), "Graph3D", "Graph3DFill"); g.Add(Ring("gpuenc"), "GraphEncode"); g.Add(Ring("gpudec"), "GraphDecode"); g.Add(Ring("gpucopy"), "GraphCopy"); }, false,
                new[] { new[] { "3D", "Graph3D" }, new[] { "Video encode", "GraphEncode" }, new[] { "Video decode", "GraphDecode" }, new[] { "Copy", "GraphCopy" } }));
            grid.Add(PerfCard("Memory", "ram", g => g.Add(Ring("ram"), "GraphRam", "GraphRamFill"), false));
            grid.Add(PerfCard("GPU memory", "vram", g => g.Add(Ring("vram"), "GraphVram", "GraphVramFill"), false));
            grid.Add(PerfCard("Disk", "disk", g => { g.Max = 0; g.MinScale = 1048576; g.Add(Ring("diskr"), "GraphRead", "GraphReadFill"); g.Add(Ring("diskw"), "GraphWrite"); }, false, new[] { new[] { "Read", "GraphRead" }, new[] { "Write", "GraphWrite" } }));
            grid.Add(PerfCard("Network", "net", g => { g.Max = 0; g.MinScale = 125000; g.Add(Ring("netdown"), "GraphDown", "GraphDownFill"); g.Add(Ring("netup"), "GraphUp"); }, false, new[] { new[] { "Receive", "GraphDown" }, new[] { "Send", "GraphUp" } }));
            sp.Children.Add(grid.Panel);
            return Scroll(sp);
        }
        Ring Ring(string k) { Ring r; if (!c.History.TryGetValue(k, out r)) c.History[k] = r = new Ring(60); return r; }
        FrameworkElement PerfCard(string title, string id, Action<Graph> setup, bool cpu, string[][] legend = null)
        {
            var box = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var big = Ui.Num("", 20, Ui.R("Text")); DockPanel.SetDock(big, Dock.Right); head.Children.Add(big); perfText[id + "-big"] = big;
            head.Children.Add(Ui.Caps(title, 12));
            box.Children.Add(head);
            var line = Ui.Num("", 11, Ui.R("Muted")); line.TextWrapping = TextWrapping.Wrap; line.Margin = new Thickness(0, 0, 0, 8); box.Children.Add(line); perfText[id + "-line"] = line;
            if (cpu && c.S.PerCore)
            {
                int n = c.Last == null ? Environment.ProcessorCount : Math.Max(1, c.Last.Sys.Cores.Length);
                int cols = n <= 4 ? 2 : n <= 9 ? 3 : n <= 16 ? 4 : n <= 25 ? 5 : 8;
                var ug = new System.Windows.Controls.Primitives.UniformGrid { Columns = cols };
                for (int i = 0; i < n; i++)
                {
                    var g = new Graph { Height = n > 16 ? 34 : 44, Margin = new Thickness(0, 0, 4, 4) }; g.Add(Ring("core" + i), "GraphCpu", "GraphCpuFill"); graphs.Add(g); ug.Children.Add(g);
                }
                box.Children.Add(ug);
            }
            else { var g = new Graph { Height = 150 }; setup(g); graphs.Add(g); box.Children.Add(g); }
            if (legend != null)
            {
                var lg = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var l in legend) { var sw = new Border { Width = 10, Height = 3, Background = Ui.R(l[1]), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center }; var t = Ui.Muted(l[0], 11); t.Margin = new Thickness(0, 0, 14, 0); lg.Children.Add(Ui.Row(sw, t)); }
                box.Children.Add(lg);
            }
            var card = Ui.Card(box, 14); card.Margin = new Thickness(0, 0, 12, 12);
            return card;
        }
        void RefreshPerformance()
        {
            if (perfView == null) return;
            foreach (var g in graphs) g.InvalidateVisual();
            var s = c.Last; if (s == null) return; var y = s.Sys;
            Action<string, string, string> set = (id, big, line) => { TextBlock t; if (perfText.TryGetValue(id + "-big", out t)) t.Text = big; if (perfText.TryGetValue(id + "-line", out t)) t.Text = line; };
            set("cpu", Fmt.Pct(y.Cpu), y.Cores.Length + " logical processors · " + y.Processes + " processes · " + y.Threads.ToString("N0") + " threads" + (y.Cores.Length > 0 ? " · busiest core " + Fmt.Pct(y.Cores.Max()) : ""));
            set("gpu", y.GpuKnown ? Fmt.Pct(y.Gpu3D) : "-", (y.GpuName ?? "Graphics card") + (y.GpuKnown ? " · encode " + Fmt.Pct(y.GpuEncode) + " · decode " + Fmt.Pct(y.GpuDecode) + " · copy " + Fmt.Pct(y.GpuCopy) : " · Windows' GPU counters aren't available"));
            set("ram", Fmt.Bytes(y.RamUsed), "of " + Fmt.Bytes(y.RamTotal) + " (" + (y.RamTotal > 0 ? Fmt.Pct(100.0 * y.RamUsed / y.RamTotal) : "-") + ") · committed " + Fmt.Bytes(y.Committed) + " of " + Fmt.Bytes(y.CommitLimit));
            set("vram", y.VramUsed > 0 ? Fmt.Bytes(y.VramUsed) : "-", y.VramTotal > 0 ? "of " + Fmt.Bytes(y.VramTotal) + " dedicated (" + Fmt.Pct(100.0 * y.VramUsed / y.VramTotal) + ")" : "dedicated graphics memory in use");
            set("disk", Fmt.Rate(y.DiskRead + y.DiskWrite), "read " + Fmt.Rate(y.DiskRead) + " · write " + Fmt.Rate(y.DiskWrite) + (y.DiskActive >= 0 ? " · active " + Fmt.Pct(y.DiskActive) : ""));
            set("net", Fmt.Bits(y.NetDown + y.NetUp), "receive " + Fmt.Bits(y.NetDown) + " · send " + Fmt.Bits(y.NetUp) + " · all adapters");
        }

        // ================= Startup apps
        FrameworkElement StartupPage()
        {
            var sp = PageBody("Startup apps", "What starts with Windows, and how much it costs. Impact is an estimate (Hallow Toolkit's): the RAM and CPU time it uses now, plus Windows' \"slowed down startup\" events when Windows shows them to you.");
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            bar.Children.Add(Ui.Btn(c.StartupReading ? "Loading..." : "Refresh", () => c.RefreshStartup()));
            bar.Children.Add(Ui.Btn("Open Startup apps", () => c.OpenWindowsStartup(), false, "Windows' own Startup apps page - for entries that are for all users (they need admin) and for switching any of them off Windows' way."));
            sp.Children.Add(bar);
            if (c.StartupItems == null) { sp.Children.Add(Ui.Muted("Reading the startup entries...", 12.5)); return Scroll(sp); }
            var table = new StackPanel();
            var hdr = StartGrid();
            Action<int, string> h = (col, t) => { var tb = Ui.Muted(t, 11); tb.Margin = new Thickness(6, 0, 6, 6); Grid.SetColumn(tb, col); hdr.Children.Add(tb); };
            h(0, "Name"); h(1, "Impact"); h(2, "Status"); h(3, "Where"); h(4, "RAM now"); h(5, "Command"); table.Children.Add(hdr);
            int i = 0;
            foreach (var it in c.StartupItems)
            {
                var item = it; i++;
                var row = StartGrid(); row.MinHeight = 34;
                var bg = new Border { Background = i % 2 == 0 ? Ui.R("RowChild") : Ui.R("Heat0"), BorderBrush = Ui.R("LineDim"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
                var nm = Ui.T(it.Name, 12.5, true); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.TextWrapping = TextWrapping.NoWrap; nm.VerticalAlignment = VerticalAlignment.Center; nm.Margin = new Thickness(6, 0, 6, 0); row.Children.Add(nm);
                var imp = Ui.Pill(it.Impact, it.Impact == "High" ? "Red" : it.Impact == "Medium" ? "Amber" : it.Impact == "Low" ? "Green" : "Lilac", it.Impact == "High" ? "RedBg" : it.Impact == "Medium" ? "AmberBg" : it.Impact == "Low" ? "GreenBg" : "LilacBg");
                imp.HorizontalAlignment = HorizontalAlignment.Left; imp.Margin = new Thickness(6, 0, 0, 0); if (it.Why.Length > 0) imp.ToolTip = it.Why; Grid.SetColumn(imp, 1); row.Children.Add(imp);
                var st = Ui.T(it.Enabled ? "On" : "Off (Windows)", 12, false, Ui.R(it.Enabled ? "Text" : "Muted")); st.VerticalAlignment = VerticalAlignment.Center; st.Margin = new Thickness(6, 0, 6, 0); Grid.SetColumn(st, 2); row.Children.Add(st);
                var wh = Ui.Muted(it.Where, 11.5); wh.VerticalAlignment = VerticalAlignment.Center; wh.Margin = new Thickness(6, 0, 6, 0); Grid.SetColumn(wh, 3); row.Children.Add(wh);
                var ram = Ui.Num(it.Running ? Fmt.Bytes(it.Ram) : "-", 11.5, Ui.R("Text")); ram.VerticalAlignment = VerticalAlignment.Center; ram.HorizontalAlignment = HorizontalAlignment.Right; ram.Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(ram, 4); row.Children.Add(ram);
                var cmd = Ui.Muted(it.Command, 11); cmd.TextWrapping = TextWrapping.NoWrap; cmd.TextTrimming = TextTrimming.CharacterEllipsis; cmd.VerticalAlignment = VerticalAlignment.Center; cmd.ToolTip = it.Command; cmd.Margin = new Thickness(6, 0, 6, 0); Grid.SetColumn(cmd, 5); row.Children.Add(cmd);
                FrameworkElement act;
                if (it.Mine)
                {
                    var b = Ui.Btn("Disable", () =>
                    {
                        if (!Dialogs.Confirm(this, "Stop " + item.Name + " starting with Windows?", "It's moved to Hallow Tasks' backup (nothing is deleted) - Restore puts it back. It stays running now; it just won't start next time.", "Disable", false)) return;
                        var e = c.DisableStartup(item); if (e != null) Dialogs.Info(this, "Hallow Tasks", e); else Say(item.Name + " won't start with Windows. Restore is below.");
                    }, false, "Your own startup entry (HKCU Run): moved to a backup, with Restore.");
                    b.Padding = new Thickness(8, 3, 8, 3); b.Margin = new Thickness(0); act = b;
                }
                else act = Ui.Muted(it.Where.StartsWith("Registry (all") ? "all users - Windows' page" : "Windows' page", 11);
                act.HorizontalAlignment = HorizontalAlignment.Right; act.VerticalAlignment = VerticalAlignment.Center; act.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(act, 6); row.Children.Add(act);
                table.Children.Add(bg);
            }
            if (c.StartupItems.Count == 0) table.Children.Add(Ui.Muted("Nothing starts with Windows from the Run keys or the Startup folders.", 12.5));
            sp.Children.Add(Ui.Card(table, 12));
            // disabled by Hallow Tasks (undo)
            var dbox = new StackPanel();
            dbox.Children.Add(Ui.Caps("Disabled by Hallow Tasks", 11));
            if (c.Disabled.Count == 0) dbox.Children.Add(Ui.Muted("Nothing yet. Entries you disable here wait in Hallow Tasks' backup (HKCU\\Software\\HallowTasks) until you restore them.", 12));
            foreach (var d in c.Disabled)
            {
                var dd = d; var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var rb = Ui.Btn("Restore", () => { var e = c.RestoreStartup(dd); if (e != null) Dialogs.Info(this, "Hallow Tasks", e); else Say(dd.Name + " starts with Windows again."); }); rb.Padding = new Thickness(8, 3, 8, 3); rb.Margin = new Thickness(0);
                DockPanel.SetDock(rb, Dock.Right); row.Children.Add(rb);
                DateTime when; var w = DateTime.TryParse(d.When, out when) ? "  ·  disabled " + when.ToString("d MMM HH:mm") : "";
                var t = new StackPanel(); t.Children.Add(Ui.T(d.Name, 12.5, true)); var dc = Ui.Muted(d.Data + w, 11); dc.TextTrimming = TextTrimming.CharacterEllipsis; dc.TextWrapping = TextWrapping.NoWrap; t.Children.Add(dc); row.Children.Add(t);
                dbox.Children.Add(row);
            }
            var dcard = Ui.Card(dbox, 14); dcard.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(dcard);
            return Scroll(sp);
        }
        static Grid StartGrid()
        {
            var g = new Grid();
            foreach (var w in new[] { 190.0, 110, 110, 170, 80, -1, 150 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            return g;
        }

        // ================= Settings
        FrameworkElement SettingsPage()
        {
            var sp = PageBody("Settings", null);
            var a = new StackPanel();
            a.Children.Add(Ui.Caps("Hallow Tasks", 11));
            a.Children.Add(Ui.Switch("Start with Windows (in the tray - quiet: only History's sample every 5 s, if that's on)", c.StartsWithWindows(), on => c.SetStartWithWindows(on)));
            var hk = Ui.Switch("Ctrl+Alt+T opens Hallow Tasks", c.S.Hotkey, null);
            hk.Click += (s, e) => { var err = c.SetHotkey(hk.IsChecked == true); if (err != null) { hk.IsChecked = false; Dialogs.Info(this, "Hallow Tasks", err); } };
            a.Children.Add(hk);
            a.Children.Add(Ui.Muted("Ctrl+Shift+Esc always opens Windows' Task Manager - Windows doesn't let another app take it over safely, so Hallow Tasks offers Ctrl+Alt+T instead (off until you turn it on).", 11.5, 6));
            sp.Children.Add(Ui.Card(a));
            sp.Children.Add(LookCard());
            sp.Children.Add(HistoryCard());
            sp.Children.Add(NetworkCard());
            var f = new StackPanel();
            f.Children.Add(Ui.Caps("Hallow family", 11));
            f.Children.Add(Ui.Switch("Use the Hallow icon (Hallow Hub's tray menu lists Hallow Tasks)", c.S.UseHallowIcon, on => { c.S.UseHallowIcon = on; c.S.Save(); c.CheckHost(); }));
            f.Children.Add(Ui.Switch("Use the Hallow accent colour (from Hallow Toolkit)", c.S.UseHallowAccent, on => { c.S.UseHallowAccent = on; c.S.Save(); c.ApplyAccent(); }));
            var fc = Ui.Card(f); fc.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(fc);
            var g = new StackPanel();
            g.Children.Add(Ui.Caps("Games", 11));
            g.Children.Add(Ui.Muted("\"What's slowing my game\" knows " + string.Join(", ", Known.Games.Select(x => x + ".exe")) + " and what Hallow Hub says you're playing. Add more exe names (comma between them):", 12, 6));
            var row = new DockPanel();
            var box = Ui.Field(string.Join(", ", c.S.ExtraGames));
            var save = Ui.Btn("Save", () => { c.SetExtraGames(box.Text); Say("Saved " + c.S.ExtraGames.Count + " more game" + (c.S.ExtraGames.Count == 1 ? "" : "s") + "."); }, true);
            save.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(save, Dock.Right); row.Children.Add(save); row.Children.Add(box);
            g.Children.Add(row);
            g.Children.Add(Ui.Muted("Never touched: the game itself and anti-cheat - " + string.Join(", ", new[] { "SGuard", "ACE", "EasyAntiCheat", "BattlEye (BEService)", "Vanguard (vgc)", "FACEIT", "nProtect", "XIGNCODE", "PunkBuster" }) + " and similar.", 11.5, 0));
            var gc = Ui.Card(g); gc.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(gc);
            var b = Ui.Row(Ui.Btn("Check for updates", () => c.CheckForUpdate(true)), Ui.Btn("Open data folder", () => { if (Snap.On) return; try { Directory.CreateDirectory(TaskSettings.Dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + TaskSettings.Dir + "\""); } catch { } }));
            b.Margin = new Thickness(0, 12, 0, 12); sp.Children.Add(b);
            sp.Children.Add(Ui.Muted("Hallow Tasks " + Program.Version + " - no admin needed for normal use. It samples once a second while this window is open, and every 5 s in the tray for History (Settings above). Per-process network bytes need admin (Windows only gives them to ETW): \"Precise network\" asks Windows (UAC) each time you switch it on; otherwise the Network column counts each app's connections. CPU temperature needs a kernel driver, so it isn't shown. Nothing is sent anywhere - Search online opens your browser only when you click it.", 11.5, 6));
            sp.Children.Add(Ui.Muted("Icons: Font Awesome Free (CC BY 4.0).", 11, 0));
            return Scroll(sp);
        }
    }

    // two-column grid of cards (a Grid with as many rows as needed)
    public class UniformGrid2
    {
        public readonly Grid Panel = new Grid(); readonly int cols; int n;
        public UniformGrid2(int columns) { cols = columns; for (int i = 0; i < cols; i++) Panel.ColumnDefinitions.Add(new ColumnDefinition()); }
        public void Add(UIElement e)
        {
            if (n % cols == 0) Panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(e, n / cols); Grid.SetColumn(e, n % cols); Panel.Children.Add(e); n++;
        }
    }
}
