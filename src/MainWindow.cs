// Hallow Tasks main window: the page list on the left (Processes, What's slowing my game, Performance, Startup, Settings), the
// open page, and a status line. The window asks for samples only while it's visible and not minimized. The other pages are in
// Pages.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowTasks
{
    public partial class MainWindow : Window
    {
        readonly Controller c;
        string page = "processes";
        readonly Dictionary<string, Button> nav = new Dictionary<string, Button>();
        Border content; TextBlock sayText, meter; Button updateBtn; DispatcherTimer sayTimer;
        public bool Exiting;
        public static readonly string[][] Pages = {
            new[] { "processes", "Processes", "list-check" }, new[] { "game", "What's slowing my game", "gamepad" }, new[] { "boost", "Game boost", "bolt" }, new[] { "performance", "Performance", "wave-square" },
            new[] { "history", "History", "clock-rotate-left" }, new[] { "startup", "Startup apps", "rocket" }, new[] { "services", "Services", "server" }, new[] { "settings", "Settings", "gear" } };
        public static string PageId(string s)
        {
            var v = (s ?? "").Trim().ToLowerInvariant();
            if (v == "perf") v = "performance"; if (v == "apps" || v == "processes" || v == "details") v = "processes"; if (v == "slow" || v == "gaming") v = "game"; if (v == "boost" || v == "gameboost") v = "boost"; if (v == "spikes") v = "history"; if (v == "service") v = "services";
            return Pages.Any(p => p[0] == v) ? v : null;
        }

        public MainWindow(Controller controller)
        {
            c = controller;
            Ui.Dress(this, "Hallow Tasks", 1300);
            SizeToContent = SizeToContent.Manual; Height = 800; MinWidth = 1000; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Rebuild();
            Action sampled = () => Dispatcher.BeginInvoke(new Action(Refresh));
            Action stChanged = () => Dispatcher.BeginInvoke(new Action(() => { if (page == "startup") BuildPage(); }));
            Action svChanged = () => Dispatcher.BeginInvoke(new Action(() => { if (page == "services") BuildPage(); }));
            c.Sampled += sampled; c.StartupChanged += stChanged; c.ServicesChanged += svChanged;
            StateChanged += (s, e) => UpdateActive(); IsVisibleChanged += (s, e) => UpdateActive();
            Closed += (s, e) => { c.Sampled -= sampled; c.StartupChanged -= stChanged; c.ServicesChanged -= svChanged; c.MainClosed(); };
            KeyDown += OnKey;
        }
        void UpdateActive() { if (!Snap.On) c.SetActive(IsVisible && WindowState != WindowState.Minimized); }

        public void Rebuild()
        {
            Ui.Restyle(this);
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            var side = new DockPanel { Background = Ui.R("Side"), LastChildFill = false };
            var head = new StackPanel { Margin = new Thickness(16, 18, 16, 18) };
            head.Children.Add(Ui.Header("TASKS", 18));
            head.Children.Add(Ui.Muted("A better Task Manager", 11.5, 0));
            DockPanel.SetDock(head, Dock.Top); side.Children.Add(head);
            nav.Clear();
            foreach (var p in Pages)
            {
                var id = p[0];
                var b = new Button { Style = Ui.S("Nav"), Content = Icons.WithText(p[2], p[1], 14, Ui.R("Text")), Margin = new Thickness(8, 0, 8, 0) };
                b.Click += (s, e) => Show(id);
                DockPanel.SetDock(b, Dock.Top); side.Children.Add(b); nav[id] = b;
            }
            var foot = new StackPanel { Margin = new Thickness(16, 0, 16, 14) };
            updateBtn = Ui.Btn("Update", () => c.InstallUpdate(), true); updateBtn.Visibility = c.PendingUpdate == null ? Visibility.Collapsed : Visibility.Visible; updateBtn.HorizontalAlignment = HorizontalAlignment.Left; updateBtn.Margin = new Thickness(0, 0, 0, 10);
            if (c.PendingUpdate != null) updateBtn.Content = "Update to v" + c.PendingUpdate.Version.ToString(2);
            foot.Children.Add(updateBtn);
            foot.Children.Add(Ui.Muted("v" + Program.Version + " · no admin needed", 11, 0));
            DockPanel.SetDock(foot, Dock.Bottom); side.Children.Add(foot);
            root.Children.Add(side);
            var sideLine = new Border { Width = 1, Background = Ui.R("Line"), HorizontalAlignment = HorizontalAlignment.Right }; root.Children.Add(sideLine);

            var right = new DockPanel(); Grid.SetColumn(right, 1);
            var bar = new DockPanel { Background = Ui.R("Side"), Height = 28 };
            meter = new TextBlock { FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.R("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            DockPanel.SetDock(meter, Dock.Right); bar.Children.Add(meter);
            sayText = new TextBlock { FontSize = 12, Foreground = Ui.R("TextSoft"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            bar.Children.Add(sayText);
            var barWrap = new Border { BorderBrush = Ui.R("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Child = bar };
            DockPanel.SetDock(barWrap, Dock.Bottom); right.Children.Add(barWrap);
            content = new Border(); right.Children.Add(content);
            root.Children.Add(right);
            Content = root;
            Show(page);
        }
        public void ShowUpdate(string label) { if (updateBtn == null) return; updateBtn.Content = label; updateBtn.Visibility = Visibility.Visible; }
        public void Say(string text)
        {
            if (sayText == null) return; sayText.Text = text ?? "";
            if (sayTimer != null) sayTimer.Stop();
            sayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) }; sayTimer.Tick += (s, e) => { sayTimer.Stop(); sayText.Text = ""; }; sayTimer.Start();
        }

        public void Show(string id)
        {
            id = PageId(id) ?? "processes";
            page = id;
            foreach (var kv in nav) kv.Value.Tag = kv.Key == id ? "on" : null;
            if (id == "startup" && c.StartupItems == null) c.RefreshStartup();
            if (id == "services" && c.ServiceList == null) c.RefreshServices();
            BuildPage();
        }
        void BuildPage()
        {
            procView = null; perfView = null; timeline = null;
            switch (page)
            {
                case "processes": content.Child = ProcessesPage(); break;
                case "game": content.Child = GamePage(); break;
                case "boost": content.Child = BoostPage(); break;
                case "performance": content.Child = PerformancePage(); break;
                case "history": content.Child = HistoryPage(); break;
                case "startup": content.Child = StartupPage(); break;
                case "services": content.Child = ServicesPage(); break;
                default: content.Child = SettingsPage(); break;
            }
            Refresh();
        }
        // a new sample (or --snap): update what's open
        public void Refresh()
        {
            if (meter != null)
            {
                if (c.Last == null) meter.Text = c.Active ? "sampling…" : "paused";
                else meter.Text = (Snap.On || c.Active ? "LIVE · 1/s" : "PAUSED") + (c.Net.On ? "   NET precise" : "") + "   CPU " + Fmt.Pct(c.Last.Sys.Cpu) + "   RAM " + (c.Last.Sys.RamTotal > 0 ? Fmt.Pct(100.0 * c.Last.Sys.RamUsed / c.Last.Sys.RamTotal) : "-") +
                    "   GPU " + (c.Last.Sys.GpuKnown ? Fmt.Pct(c.Last.Sys.Gpu3D) : "-") + "   Tasks itself " + Fmt.Pct(c.OwnCpu) + " CPU";
            }
            if (page == "processes") RefreshProcesses();
            else if (page == "game") { if (gameAt == null || (DateTime.Now - gameAt.Value).TotalSeconds >= 1.9 || Snap.On) { gameAt = DateTime.Now; content.Child = GamePage(); } }
            else if (page == "boost") { if (gameAt == null || (DateTime.Now - gameAt.Value).TotalSeconds >= 1.9 || Snap.On) { gameAt = DateTime.Now; var sv = boostScroll; var off = sv == null ? 0 : sv.VerticalOffset; content.Child = BoostPage(); if (boostScroll != null && off > 0) boostScroll.ScrollToVerticalOffset(off); } }
            else if (page == "performance") RefreshPerformance();
            else if (page == "history") RefreshHistory();
        }
        DateTime? gameAt;
        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && search != null && page == "processes") { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Delete && page == "processes" && selKey != null) { EndSelected(false); e.Handled = true; }
        }

        // ================= Processes
        TextBox search; string searchText = "", filter = "All"; readonly Dictionary<string, ToggleButton> chips = new Dictionary<string, ToggleButton>();
        readonly HashSet<string> expanded = new HashSet<string>(); string selKey, selChild;   // selected group key, selected child process key
        RowSync rows; ItemsControl list; readonly Dictionary<string, TextBlock[]> heads = new Dictionary<string, TextBlock[]>(); Border details; object procView;
        TextBlock shown;

        FrameworkElement ProcessesPage()
        {
            var dock = new DockPanel { Margin = new Thickness(18, 14, 18, 10) }; procView = dock; detailsFor = null;
            // top: title, search, chips
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Ui.T("Processes", 20, true)); shown = Ui.Muted("", 12); shown.Margin = new Thickness(10, 6, 0, 0); shown.FontFamily = Ui.Mono; title.Children.Add(shown);
            DockPanel.SetDock(title, Dock.Left); top.Children.Add(title);
            search = Ui.Field(searchText, 240); search.ToolTip = "Search by name, publisher, path or PID (Ctrl+F)";
            search.TextChanged += (s, e) => { searchText = search.Text; RefreshProcesses(); };
            var sbox = new Grid { Width = 240, HorizontalAlignment = HorizontalAlignment.Right }; sbox.Children.Add(search);
            var hint = Ui.Muted("Search", 12); hint.Margin = new Thickness(11, 0, 0, 0); hint.VerticalAlignment = VerticalAlignment.Center; hint.IsHitTestVisible = false; hint.FontFamily = Ui.Mono;
            hint.Visibility = searchText.Length == 0 ? Visibility.Visible : Visibility.Collapsed; search.TextChanged += (s, e) => hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            sbox.Children.Add(hint);
            DockPanel.SetDock(sbox, Dock.Right); top.Children.Add(sbox);
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 12, 0) };
            chips.Clear();
            foreach (var f in new[] { "All", "Apps", "Background", "Windows", "Hallow" })
            {
                var ff = f; var t = new ToggleButton { Content = f, Style = Ui.S("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = filter == f };
                t.Click += (s, e) => { filter = ff; foreach (var kv in chips) kv.Value.IsChecked = kv.Key == filter; RefreshProcesses(); };
                chips[f] = t; chipRow.Children.Add(t);
            }
            var treeBtn = new ToggleButton { Content = "Tree", Style = Ui.S("Chip"), Margin = new Thickness(6, 0, 0, 0), IsChecked = c.S.TreeView, ToolTip = "Tree view: every process under the one that started it (parent \u2192 children), with End tree. Off: grouped by app, like Task Manager." };
            treeBtn.Click += (s, e) => { c.S.TreeView = treeBtn.IsChecked == true; c.S.Save(); selKey = null; selChild = null; RefreshProcesses(); };
            chipRow.Children.Add(new Border { Width = 1, Background = Ui.R("Line"), Margin = new Thickness(6, 3, 0, 3) }); chipRow.Children.Add(treeBtn);
            top.Children.Add(chipRow);
            DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);

            // bottom: the details strip of the selected row
            details = new Border { Background = Ui.R("Panel"), BorderBrush = Ui.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(details, Dock.Bottom); dock.Children.Add(details);

            // the table: column headers, then the rows
            var table = new Border { Background = Ui.R("Panel"), BorderBrush = Ui.R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
            var tdock = new DockPanel();
            var hg = (Grid)System.Windows.Markup.XamlReader.Parse("<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" + ProcessColumns.ColDefs() + "</Grid>");
            hg.Margin = new Thickness(0, 0, 10, 0);
            heads.Clear();
            var cols = new List<string[]> { new[] { "name", "Name", "" } }; cols.AddRange(ProcessColumns.Cols);
            for (int i = 0; i < cols.Count; i++)
            {
                var id = cols[i][0];
                var big = new TextBlock { FontFamily = Ui.Mono, FontSize = 12.5, Foreground = Ui.R("Text"), HorizontalAlignment = i == 0 || id == "publisher" || id == "path" ? HorizontalAlignment.Left : HorizontalAlignment.Right };
                var small = new TextBlock { Text = cols[i][1], FontSize = 11, Foreground = Ui.R("Muted"), HorizontalAlignment = big.HorizontalAlignment };
                var sp = new StackPanel(); sp.Children.Add(big); sp.Children.Add(small);
                var b = new Button { Style = Ui.S("Head"), Content = sp, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = HeadTip(id) };
                if (i == 0) b.Padding = new Thickness(22, 0, 0, 0);
                if (id != "path") b.Click += (s, e) => SortBy(id); else b.Cursor = Cursors.Arrow;
                Grid.SetColumn(b, i); hg.Children.Add(b); heads[id] = new[] { big, small };
            }
            var hwrap = new Border { Child = hg, BorderBrush = Ui.R("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Background = Ui.R("PanelHi") };
            DockPanel.SetDock(hwrap, Dock.Top); tdock.Children.Add(hwrap);
            if (rows == null) rows = new RowSync();
            list = new ItemsControl { ItemTemplate = ProcessColumns.RowTemplate(), Template = ProcessColumns.ListTemplate(), ItemsSource = rows.Items, Focusable = false };
            list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingStackPanel.SetIsVirtualizing(list, true); VirtualizingStackPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(list, true);
            list.PreviewMouseLeftButtonDown += (s, e) => RowClick(e, false);
            list.PreviewMouseRightButtonDown += (s, e) => RowClick(e, true);
            tdock.Children.Add(list);
            table.Child = tdock; dock.Children.Add(table);
            return dock;
        }
        static string HeadTip(string id)
        {
            switch (id)
            {
                case "name": return "Grouped by app, like Task Manager. Double-click a group (or its arrow) to see its processes. Default order: Impact (CPU + GPU + disk + memory, an estimate).";
                case "cpu": return "Share of the whole CPU (all cores = 100%).";
                case "ram": return "Memory only this process uses (private working set), like Task Manager.";
                case "gpu": return "3D engine use (Windows' \"GPU Engine\" counters, engtype_3D, summed per process).";
                case "gpumem": return "Dedicated graphics memory in use.";
                case "disk": return "File and device reads + writes per second (it can include some network and device I/O, like Process Explorer's I/O column).";
                case "net": return "Open TCP connections. Bytes per process need admin (Windows only gives them to ETW): Settings > \"Precise network (needs admin)\" starts a small helper after Windows asks you (UAC), and this column shows send + receive speed until Hallow Tasks closes.";
                case "power": return "A hint from CPU, GPU and disk use - not a measurement.";
                case "publisher": return "Who signed the program (or the company in its file details when it isn't signed).";
                default: return null;
            }
        }
        string SortCol { get { return c.S.Sort ?? "impact"; } }
        void SortBy(string id)
        {
            // first click: that column (biggest first; names A-Z), second: the other way, third: back to Impact
            bool firstUp = id == "name" || id == "publisher";
            if (SortCol != id) { c.S.Sort = id; c.S.SortUp = firstUp; }
            else if (c.S.SortUp == firstUp) c.S.SortUp = !firstUp;
            else { c.S.Sort = "impact"; c.S.SortUp = false; }
            c.S.Save(); RefreshProcesses();
        }
        RowVm RowAt(RoutedEventArgs e, out bool onChevron)
        {
            onChevron = false; var d = e.OriginalSource as DependencyObject;
            while (d != null && !(d is ItemsControl))
            {
                var fe = d as FrameworkElement;
                if (fe != null && "chev".Equals(fe.Tag)) onChevron = true;
                if (fe != null && fe.DataContext is RowVm) return (RowVm)fe.DataContext;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }
        void RowClick(MouseButtonEventArgs e, bool right)
        {
            bool chev; var r = RowAt(e, out chev); if (r == null) return;
            if (r.Proc == null) { selKey = r.Group.Key; selChild = null; } else { selKey = r.Group.Key; selChild = r.Proc.Key; }
            if (!right && r.Proc == null && (chev || e.ClickCount == 2) && r.Group.Members.Count > 1) { if (!expanded.Remove(r.Group.Key)) expanded.Add(r.Group.Key); }
            if (!right && r.Tree && r.TreeHas && (chev || e.ClickCount == 2)) { if (!treeCollapsed.Remove(r.Proc.Key)) treeCollapsed.Add(r.Proc.Key); }
            RefreshProcesses();
            if (right) { var m = MenuFor(r.Group, r.Proc); m.PlacementTarget = list; m.IsOpen = true; e.Handled = true; }
        }
        // --snap helpers
        public void SnapSelect(string keyPrefix, bool expand)
        {
            var g = c.Groups.FirstOrDefault(x => x.Key.StartsWith(keyPrefix)); if (g == null) return;
            selKey = g.Key; selChild = null; if (expand) expanded.Add(g.Key);
        }
        public void SnapSearch(string s) { searchText = s; if (search != null) search.Text = s; }
        public void SnapFilter(string f) { filter = f; foreach (var kv in chips) kv.Value.IsChecked = kv.Key == f; }

        readonly HashSet<string> treeCollapsed = new HashSet<string>();
        void RefreshTree(List<AppGroup> groups)
        {
            var procs = c.Last == null ? new List<ProcInfo>() : c.Last.Procs;
            var groupOf = new Dictionary<int, AppGroup>(); foreach (var g in groups) foreach (var m in g.Members) groupOf[m.Pid] = g;
            var q = (searchText ?? "").Trim(); bool filtering = filter != "All" || q.Length > 0;
            Func<string, bool> has = a => a != null && a.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
            Func<ProcInfo, bool> show = p =>
            {
                AppGroup g; if (!groupOf.TryGetValue(p.Pid, out g)) return false;
                if (filter != "All" && g.Category != filter) return false;
                return q.Length == 0 || has(p.Name) || has(p.Description) || has(p.Path) || has(g.Title) || p.Pid.ToString() == q;
            };
            var tree = Grouping.Forest(procs, treeCollapsed, filtering ? show : null, p => ImpactCalc.Score(p.Cpu, p.Gpu, p.DiskRead + p.DiskWrite, p.Ram));
            if (shown != null) shown.Text = tree.Count + " processes \u00B7 tree";
            var want = new List<RowVm>();
            foreach (var tr in tree)
            {
                AppGroup g; if (!groupOf.TryGetValue(tr.Proc.Pid, out g)) continue;
                var r = rows.Ensure("t|" + tr.Proc.Key); r.Group = g; r.Proc = tr.Proc; r.Tree = true; r.TreeHas = tr.HasChildren; Fill(r, g, tr.Proc);
                r.Set("Chevron", tr.HasChildren ? (treeCollapsed.Contains(tr.Proc.Key) ? "\u25B8" : "\u25BE") : "");
                r.Set("Indent", new Thickness(4 + 16 * Math.Min(tr.Depth, 12), 0, 0, 0));
                r.Set("Name", tr.Proc.Name); r.Set("Count", tr.Proc.Pid.ToString());
                r.Set("Bold", tr.Proc.HasWindow);
                bool sel = selChild == tr.Proc.Key; r.Set("RowBg", Ui.R(sel ? "RowSel" : "Heat0"));
                want.Add(r);
            }
            rows.Order(want);
            UpdateDetails();
        }
        void RefreshProcesses()
        {
            if (list == null || rows == null) return;
            var s = c.Last; var groups = c.Groups ?? new List<AppGroup>();
            if (s != null)
            {
                var sys = s.Sys;
                SetHead("cpu", Fmt.Pct(sys.Cpu)); SetHead("ram", sys.RamTotal > 0 ? Fmt.Pct(100.0 * sys.RamUsed / sys.RamTotal) : "-"); SetHead("gpu", sys.GpuKnown ? Fmt.Pct(sys.Gpu3D) : "-");
                SetHead("gpumem", sys.VramTotal > 0 ? Fmt.Pct(100.0 * sys.VramUsed / sys.VramTotal) : sys.VramUsed > 0 ? Fmt.Bytes(sys.VramUsed) : "-");
                SetHead("disk", Fmt.Rate(sys.DiskRead + sys.DiskWrite)); SetHead("net", Fmt.Bits(sys.NetUp + sys.NetDown)); SetHead("power", ""); SetHead("publisher", ""); SetHead("path", "");
                SetHead("name", "");
            }
            foreach (var kv in heads) { var col = kv.Key == "name" ? "name" : kv.Key; var arrow = SortCol == col || (SortCol == "impact" && col == "name") ? (SortCol == "impact" ? "  ▾ impact" : c.S.SortUp ? "  ▴" : "  ▾") : ""; kv.Value[1].Text = (kv.Key == "name" ? "Name" : ProcessColumns.Cols.First(x => x[0] == kv.Key)[1]) + arrow; kv.Value[1].Foreground = arrow.Length > 0 ? Ui.R("AccentBrush") : Ui.R("Muted"); }
            if (c.S.TreeView) { RefreshTree(groups); return; }
            var shownGroups = Grouping.Sort(groups.Where(g => Grouping.Matches(g, searchText, filter) && !(g.Members.Count == 1 && g.Members[0].Pid == 0)), SortCol, SortCol == "impact" ? true : !c.S.SortUp);
            if (shown != null) shown.Text = shownGroups.Count + " apps · " + shownGroups.Sum(g => g.Members.Count) + " processes";
            var want = new List<RowVm>();
            foreach (var g in shownGroups)
            {
                var r = rows.Ensure("g|" + g.Key); r.Group = g; r.Proc = null; r.Tree = false; Fill(r, g, null);
                want.Add(r);
                if (expanded.Contains(g.Key) && g.Members.Count > 1)
                    foreach (var p in g.Members.OrderByDescending(x => x.Cpu + x.Gpu * 0.8).ThenBy(x => x.Pid))
                    {
                        var cr = rows.Ensure("p|" + p.Key); cr.Group = g; cr.Proc = p; cr.Tree = false; Fill(cr, g, p); want.Add(cr);
                    }
            }
            rows.Order(want);
            UpdateDetails();
        }
        void SetHead(string id, string text) { TextBlock[] t; if (heads.TryGetValue(id, out t)) t[0].Text = text; }
        void Fill(RowVm r, AppGroup g, ProcInfo p)
        {
            bool child = p != null; var x = p ?? g.Main;
            double cpu = child ? p.Cpu : g.Cpu, gpu = child ? p.Gpu : g.Gpu, disk = child ? p.DiskRead + p.DiskWrite : g.DiskRead + g.DiskWrite; long ram = child ? p.Ram : g.Ram, gmem = child ? p.GpuMem : g.GpuMem;
            int conns = child ? p.Conns : g.Conns;
            r.Set("Chevron", child ? "" : g.Members.Count > 1 ? (expanded.Contains(g.Key) ? "▾" : "▸") : "");
            r.Set("Indent", new Thickness(child ? 22 : 4, 0, 0, 0));
            r.SetIcon(Ui.ProgramIcon(x == null ? null : x.Path ?? g.Path));
            var name = child ? Known.Friendly(p) + "  " : g.Title;
            r.Set("Letter", string.IsNullOrEmpty(g.Title) ? "?" : g.Title.Substring(0, 1).ToUpperInvariant());
            r.Set("Name", child ? p.Name + (p.Description != null && !string.Equals(p.Description, g.Title, StringComparison.OrdinalIgnoreCase) ? "  ·  " + p.Description : "") : name);
            r.Set("Bold", !child && g.Category == "Apps");
            r.Set("Count", child ? "PID " + p.Pid : g.Members.Count > 1 ? "(" + g.Members.Count + ")" : "");
            var lowered = c.Keeper.Lowered.Any(e => child ? e.Pid == p.Pid : g.Members.Any(m => m.Pid == e.Pid));
            if (child ? p.Hung : g.Hung) r.SetTag("Not responding", "Red", "RedBg");   // 1.4
            else if (!child && g.IsGame || child && Known.IsGameName(p.Name, c.ExtraGames) || (child && c.Game != null && c.Game.Key == p.Key)) r.SetTag("Game", "Green", "GreenBg");
            else if (child ? Known.IsAntiCheat(p.Name) : g.IsAntiCheat) r.SetTag("Anti-cheat", "Amber", "AmberBg");
            else if (child ? c.EcoOn(p) : c.EcoOn(g)) r.SetTag("Efficiency", "Green", "GreenBg");
            else if (child ? p.Signed == false && p.Path != null : g.Signed == false && g.Category != "Hallow") r.SetTag("Not signed", "Amber", "AmberBg");
            else r.SetTag(null, null, null);
            r.Set("Cpu", Fmt.Pct(cpu)); r.Set("CpuBg", Ui.R("Heat" + ImpactCalc.Heat(cpu)));
            r.Set("Ram", Fmt.Bytes(ram)); r.Set("RamBg", Ui.R("Heat" + ImpactCalc.HeatRam(ram)));
            r.Set("Gpu", Fmt.Pct(gpu)); r.Set("GpuBg", Ui.R("Heat" + ImpactCalc.Heat(gpu)));
            r.Set("GpuMem", gmem > 0 ? Fmt.Bytes(gmem) : "-");
            r.Set("Disk", Fmt.Rate(disk)); r.Set("DiskBg", Ui.R("Heat" + ImpactCalc.HeatBytes(disk)));
            r.Set("DiskTip", "Read " + Fmt.Rate(child ? p.DiskRead : g.DiskRead) + ", write " + Fmt.Rate(child ? p.DiskWrite : g.DiskWrite));
            double nb = child ? (p.NetSend >= 0 ? p.NetSend + p.NetRecv : -1) : g.NetBytes;
            r.Set("Net", nb >= 0 ? (nb < 1 ? "0 Kbps" : Fmt.Bits(nb)) : conns < 0 ? "-" : conns == 0 ? "-" : conns + " conn");
            var pw = ImpactCalc.Power(cpu, gpu, disk); r.Set("Power", pw);
            r.Set("PowerFg", Ui.R(pw == "Very high" ? "Red" : pw == "High" ? "Amber" : pw == "Moderate" ? "Text" : "Muted"));
            var pub = child ? (p.Signer ?? p.Company) : g.Category == "Hallow" ? "Hallow" : g.Publisher; var signed = child ? p.Signed : g.Signed;
            r.Set("Publisher", pub == null ? (x != null && x.Path == null ? "(protected)" : "-") : pub + (signed == true ? "  ✓" : ""));
            r.Set("PublisherFg", Ui.R(signed == false ? "Amber" : "TextSoft"));
            r.Set("Path", x == null ? "" : x.Path ?? "");
            bool sel = selKey == g.Key && (child ? selChild == p.Key : selChild == null);
            r.Set("RowBg", Ui.R(sel ? "RowSel" : child ? "RowChild" : "Heat0"));
        }

        // the details strip and its buttons
        void UpdateDetails()
        {
            if (details == null) return;
            var g = selKey == null ? null : c.Groups.FirstOrDefault(q => q.Key == selKey);
            var p = g == null || selChild == null ? null : g.Members.FirstOrDefault(m => m.Key == selChild);
            if (g == null || (selChild != null && p == null))
            {
                var tip = new StackPanel();
                tip.Children.Add(Ui.T("Click a row for details and actions. Double-click a group to see its processes. Right-click for the menu.", 12.5, false, Ui.R("TextSoft")));
                tip.Children.Add(Ui.Muted("Sorted by Impact - CPU + 0.8 × GPU + disk + memory, an estimate of what's slowing your PC. Click a column to sort by it. Hallow Tasks never touches games or anti-cheat, and never ends anything without asking.", 11.5));
                details.Child = tip; detailsFor = null; return;
            }
            var key = g.Key + "|" + selChild;
            var x = p ?? g.Main;
            if (detailsFor == key && detailsNums != null)
            {   // same selection: just the numbers
                detailsNums.Text = Nums(g, p); return;
            }
            detailsFor = key;
            var dock = new DockPanel();
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var tl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            tl.Children.Add(Ui.Tile(g.Title, Ui.ProgramIcon(x == null ? null : x.Path), 20));
            var nm = Ui.T(p != null ? p.Name : g.Title, 14.5, true); nm.Margin = new Thickness(8, 0, 0, 0); nm.VerticalAlignment = VerticalAlignment.Center; tl.Children.Add(nm);
            var why = p != null ? c.WhyNot(p) : c.WhyNot(g);
            if (g.IsAntiCheat || (p != null && Known.IsAntiCheat(p.Name))) { var pl = Ui.Pill(Known.AntiCheatTag, "Amber", "AmberBg"); pl.Margin = new Thickness(10, 0, 0, 0); tl.Children.Add(pl); }
            else if (g.IsGame) { var pl = Ui.Pill("Your game — never touched by itself", "Green", "GreenBg"); pl.Margin = new Thickness(10, 0, 0, 0); tl.Children.Add(pl); }
            if (p != null ? p.Hung : g.Hung) { var pl = Ui.Pill("Not responding", "Red", "RedBg"); pl.Margin = new Thickness(10, 0, 0, 0); pl.ToolTip = "Windows says a window of it hasn't read its messages for 5 s or more."; tl.Children.Add(pl); }
            else if (why != null) { var pl = Ui.Pill("Windows — protected", "Lilac", "LilacBg"); pl.Margin = new Thickness(10, 0, 0, 0); tl.Children.Add(pl); }
            left.Children.Add(tl);
            Func<string, string, UIElement> kv = (k, v) => { var t = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }; t.Inlines.Add(new System.Windows.Documents.Run(k + "  ") { Foreground = Ui.R("Muted") }); t.Inlines.Add(new System.Windows.Documents.Run(v) { Foreground = Ui.R("Text") }); return t; };
            if (x != null)
            {
                left.Children.Add(kv(p != null ? "PID" : "Processes", p != null ? p.Pid + (p.ParentPid > 0 ? "   parent " + p.ParentPid : "") : g.Members.Count + "   main PID " + x.Pid));
                if (x.Description != null) left.Children.Add(kv("Description", x.Description));
                left.Children.Add(kv("Publisher", (x.Signer ?? x.Company ?? "unknown") + (x.Signed == true ? " (signed)" : x.Signed == false ? " (not signed)" : "")));
                var path = kv("Path", x.Path ?? "Windows doesn't say (protected or another user's)"); left.Children.Add(path);
                string started = ""; try { started = DateTime.FromFileTime(x.CreateTime).ToString("ddd HH:mm"); } catch { }
                var pr = c.Ctl.GetPriority(x.Pid);
                left.Children.Add(kv("Started", started + "   priority " + (pr == null ? "?" : PriorityKeeper.Word(pr.Value)) + "   " + x.Threads + " threads"));
            }
            detailsNums = new TextBlock { FontFamily = Ui.Mono, FontSize = 11.5, Foreground = Ui.R("Lilac"), Margin = new Thickness(0, 4, 0, 0), Text = Nums(g, p) };
            left.Children.Add(detailsNums);
            var btns = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, MaxWidth = 470 };
            Action<Button> add = b => { b.Margin = new Thickness(6, 0, 0, 6); btns.Children.Add(b); };
            var closeGame = c.CloseGameTarget(g, p);   // v1.3: only for a process tagged Game (never anti-cheat or a launcher)
            if (closeGame != null) add(Ui.Danger("Close game…", () => CloseGame(closeGame)));
            var closeApp = closeGame == null ? c.CloseAppTarget(g, p) : null;   // v1.4: a "Not responding" app (same safe flow)
            if (closeApp != null) add(Ui.Danger("Close (not responding)…", () => CloseApp(closeApp)));
            var end = Ui.Danger("End task", () => EndSelected(false)); end.IsEnabled = why == null; if (why != null) { end.ToolTip = why; ToolTipService.SetShowOnDisabled(end, true); } add(end);
            var tree = Ui.Btn(c.S.TreeView ? "End tree" : "End process tree", () => EndSelected(true)); tree.IsEnabled = why == null; if (why != null) { tree.ToolTip = why; ToolTipService.SetShowOnDisabled(tree, true); } add(tree);
            var prio = Ui.Btn("Priority...", () => PrioritySelected()); prio.IsEnabled = why == null; add(prio);
            var aff = Ui.Btn("Affinity...", () => AffinitySelected()); aff.IsEnabled = why == null; add(aff);
            bool ecoOn = p != null ? (c.EcoOn(p) || c.Ctl.GetEfficiency(p.Pid) == true) : (c.EcoOn(g) || (x != null && c.Ctl.GetEfficiency(x.Pid) == true));
            var eco = Ui.Btn(ecoOn ? "Efficiency off" : "Efficiency mode", () => EfficiencySelected(!ecoOn), false, EcoKeeper.What); eco.IsEnabled = why == null; if (why != null) { eco.ToolTip = why; ToolTipService.SetShowOnDisabled(eco, true); } add(eco);
            add(Ui.Btn("Open file location", () => Done(c.OpenLocation(x == null ? null : x.Path), null)));
            add(Ui.Btn("Search online", () => { if (x != null) { c.SearchOnline(x); Say("Opened a web search for " + x.Name + " in your browser."); } }, false, "Opens your browser with a web search for " + (x == null ? "it" : x.Name) + " - only when you click."));
            var scan = Ui.Btn(c.Scanning ? "Scanning..." : "Scan with Defender", null, false, "Microsoft Defender checks the program's file (through Hallow Guard when it offers that, otherwise Defender's own scanner).");
            scan.Click += (s, e) => { scan.Content = "Scanning..."; scan.IsEnabled = false; c.Scan(x == null ? null : x.Path, m => { scan.Content = "Scan with Defender"; scan.IsEnabled = true; Dialogs.Info(this, "Scan with Defender", m); }); };
            scan.IsEnabled = x != null && x.Path != null && !c.Scanning; add(scan);
            add(Ui.Btn("Copy details", () => { c.CopyDetails(g, p); Say("Copied the details."); }));
            DockPanel.SetDock(btns, Dock.Right); dock.Children.Add(btns); dock.Children.Add(left);
            details.Child = dock;
        }
        string detailsFor; TextBlock detailsNums;
        static string Nums(AppGroup g, ProcInfo p)
        {
            if (p != null) return "CPU " + Fmt.Pct(p.Cpu) + "  ·  RAM " + Fmt.Bytes(p.Ram) + "  ·  GPU " + Fmt.Pct(p.Gpu) + " (" + (p.GpuMem > 0 ? Fmt.Bytes(p.GpuMem) : "0 MB") + ")  ·  disk " + Fmt.Rate(p.DiskRead + p.DiskWrite) + (p.NetSend >= 0 ? "  ·  net \u2191" + Fmt.Bits(p.NetSend) + " \u2193" + Fmt.Bits(p.NetRecv) : "") + "  ·  power " + ImpactCalc.Power(p.Cpu, p.Gpu, p.DiskRead + p.DiskWrite).ToLowerInvariant();
            return "CPU " + Fmt.Pct(g.Cpu) + "  ·  RAM " + Fmt.Bytes(g.Ram) + "  ·  GPU " + Fmt.Pct(g.Gpu) + " (" + (g.GpuMem > 0 ? Fmt.Bytes(g.GpuMem) : "0 MB") + ")  ·  disk " + Fmt.Rate(g.DiskRead + g.DiskWrite) + (g.NetBytes >= 0 ? "  ·  net " + Fmt.Bits(g.NetBytes) : "") + "  ·  impact " + g.Impact.ToString("0.0");
        }
        void EfficiencySelected(bool on)
        {
            AppGroup g; ProcInfo p; Selected(out g, out p); if (g == null) return;
            var err = p != null ? c.SetEfficiency(p, on) : c.SetEfficiency(g, on);
            Done(err, on ? (p != null ? p.Name : g.Title) + " is in Efficiency mode (EcoQoS + Idle priority - what Windows' own Efficiency mode does)." : "Efficiency mode is off for " + (p != null ? p.Name : g.Title) + ".");
        }
        void Selected(out AppGroup g, out ProcInfo p)
        {
            g = selKey == null ? null : c.Groups.FirstOrDefault(x => x.Key == selKey);
            p = g == null || selChild == null ? null : g.Members.FirstOrDefault(m => m.Key == selChild);
        }
        void Done(string err, string ok)
        {
            if (err != null) Dialogs.Info(this, "Hallow Tasks", err);
            else if (ok != null) Say(ok);
            detailsFor = null; RefreshProcesses();
        }
        void EndSelected(bool tree)
        {
            AppGroup g; ProcInfo p; Selected(out g, out p); if (g == null) return;
            var target = p ?? g.Main;
            var why = tree ? c.WhyNot(target) : p != null ? c.WhyNot(p) : c.WhyNot(g);
            if (why != null) { Dialogs.Info(this, "Hallow Tasks won't end that", why); return; }
            string title, text;
            if (tree) { var t = c.Tree(target); title = "End " + target.Name + " and everything it started?"; text = t.Count + " process" + (t.Count == 1 ? "" : "es") + " (" + string.Join(", ", t.Select(x => x.Name).Distinct().Take(6)) + (t.Select(x => x.Name).Distinct().Count() > 6 ? ", ..." : "") + "). Anything unsaved in them is lost."; }
            else if (p != null) { title = "End " + p.Name + " (PID " + p.Pid + ")?"; text = "Anything unsaved in it is lost. If it's part of " + g.Title + ", the rest of " + g.Title + " may stop working."; }
            else { title = "End " + g.Title + "?"; text = (g.Members.Count > 1 ? g.Title + " has " + g.Members.Count + " processes - all of them end. " : "") + "Anything unsaved in it is lost."; }
            if (!Dialogs.Confirm(this, title, text, tree ? "End process tree" : "End task", true)) return;
            var err = tree ? c.EndTree(target) : p != null ? c.EndProcess(p) : c.EndGroup(g);
            if (err == null) { selKey = null; selChild = null; }
            Done(err, err == null ? "Ended " + (p != null ? p.Name : g.Title) + "." : null);
        }
        void PrioritySelected()
        {
            AppGroup g; ProcInfo p; Selected(out g, out p); if (g == null) return;
            var x = p ?? g.Main; var cur = c.Ctl.GetPriority(x.Pid) ?? Prio.Normal;
            var to = Dialogs.Priority(this, p != null ? p.Name : g.Title + (g.Members.Count > 1 ? " (all " + g.Members.Count + " processes)" : ""), cur); if (to == null) return;
            var errs = new List<string>();
            foreach (var m in p != null ? new List<ProcInfo> { p } : g.Members.Where(q => q.Pid != c.SelfPid).ToList()) { var e = c.SetPriority(m, to.Value); if (e != null) errs.Add(e); }
            Done(errs.Count == 0 ? null : errs[0], "Priority set to " + PriorityKeeper.Word(to.Value) + " - until it closes.");
        }
        void AffinitySelected()
        {
            AppGroup g; ProcInfo p; Selected(out g, out p); if (g == null) return;
            var x = p ?? g.Main; int cores = Environment.ProcessorCount; var cur = c.Ctl.Affinity(x.Pid); if (cur == 0) cur = cores >= 64 ? -1 : (1L << cores) - 1;
            var m = Dialogs.Affinity(this, p != null ? p.Name : g.Title, cores, cur); if (m == null) return;
            var errs = new List<string>();
            foreach (var t in p != null ? new List<ProcInfo> { p } : g.Members.Where(q => q.Pid != c.SelfPid).ToList()) { var e = c.SetAffinity(t, m.Value); if (e != null) errs.Add(e); }
            Done(errs.Count == 0 ? null : errs[0], "Affinity set - until it closes.");
        }
        // v1.3: the confirm dialog; nothing happens until you click in it. 1.5: one confirm, then Close now ends it right away (or the two
        // steps, if Settings says "ask first"); with "Don't ask again" the click itself does it - the dialog only shows if Windows refuses
        void CloseGame(ProcInfo game)
        {
            if (c.CloseGameTarget(null, game) == null) return;
            RunClose(c.NewGameClose(game));
        }
        // v1.4: the same dialog in "app" mode for a Not responding app (1.5: always ended right away after the confirm)
        void CloseApp(ProcInfo app)
        {
            if (c.CloseAppTarget(null, app) == null) return;
            RunClose(c.NewAppClose(app));
        }
        void RunClose(GameCloseRun run)
        {
            if (run.Begin(c.S.CloseNoConfirm))
            {
                var w = GameCloseWindow.Make(run, null);
                if (IsVisible) { w.Owner = this; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
                w.ShowDialog();
            }
            if (run.Now == GameCloseRun.State.Closed || run.Now == GameCloseRun.State.Forced) { selKey = null; selChild = null; Done(null, run.DoneText); }
            else { detailsFor = null; RefreshProcesses(); }
        }
        ContextMenu MenuFor(AppGroup g, ProcInfo p)
        {
            var m = new ContextMenu(); var why = p != null ? c.WhyNot(p) : c.WhyNot(g); var x = p ?? g.Main;
            Action<string, string, Action, bool> item = (label, icon, act, on) => { var mi = new MenuItem { Header = label, IsEnabled = on, Icon = icon == null ? null : Icons.Make(icon, 13, Ui.R("Text")) }; mi.Click += (s, e) => act(); m.Items.Add(mi); };
            var closeGame = c.CloseGameTarget(g, p);   // v1.3: only for a process tagged Game (never anti-cheat or a launcher)
            if (closeGame != null) { item("Close game…", "power-off", () => CloseGame(closeGame), true); m.Items.Add(new Separator()); }
            var closeApp = closeGame == null ? c.CloseAppTarget(g, p) : null;
            if (closeApp != null) { item("Close (not responding)…", "triangle-exclamation", () => CloseApp(closeApp), true); m.Items.Add(new Separator()); }
            item("End task", "circle-xmark", () => EndSelected(false), why == null);
            item(c.S.TreeView ? "End tree" : "End process tree", "skull-crossbones", () => EndSelected(true), why == null);
            m.Items.Add(new Separator());
            item("Set priority...", "sliders", PrioritySelected, why == null);
            item("Set affinity...", "microchip", AffinitySelected, why == null);
            bool eco = p != null ? c.EcoOn(p) : c.EcoOn(g);
            item(eco ? "Efficiency mode: turn off" : "Efficiency mode (like Windows')", "leaf", () => EfficiencySelected(!eco), why == null);
            m.Items.Add(new Separator());
            item("Open file location", "folder-open", () => Done(c.OpenLocation(x == null ? null : x.Path), null), x != null && x.Path != null);
            item("Search online", "globe", () => { c.SearchOnline(x); Say("Opened a web search for " + x.Name + " in your browser."); }, x != null);
            item("Scan with Defender", "shield-halved", () => c.Scan(x.Path, msg => Dialogs.Info(this, "Scan with Defender", msg)), x != null && x.Path != null && !c.Scanning);
            item("Copy details", "copy", () => { c.CopyDetails(g, p); Say("Copied the details."); }, true);
            if (p == null && g.Members.Count > 1) { m.Items.Add(new Separator()); item(expanded.Contains(g.Key) ? "Hide its processes" : "Show its processes", "list-check", () => { if (!expanded.Remove(g.Key)) expanded.Add(g.Key); RefreshProcesses(); }, true); }
            return m;
        }
    }
}
