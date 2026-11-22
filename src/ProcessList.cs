// Hallow Tasks - the process list: one row per app group (Discord, Chrome, Hallow...) that opens into its processes, Task Manager
// style. Rows are view models updated in place once a second; the list is virtualised (only the rows on screen exist), so a
// sample costs a few property changes, not a rebuilt window.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace HallowTasks
{
    public class RowVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        readonly Dictionary<string, object> v = new Dictionary<string, object>();
        public string Key; public AppGroup Group; public ProcInfo Proc;   // Proc = a child row (one process of a group)
        public bool Tree, TreeHas;                                         // a tree-view row, and whether it has children
        object G(string n) { object o; return v.TryGetValue(n, out o) ? o : null; }
        public void Set(string n, object value)
        {
            object o; if (v.TryGetValue(n, out o) && Equals(o, value)) return;
            v[n] = value; var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n));
        }
        public string Chevron { get { return (string)G("Chevron"); } }
        public Thickness Indent { get { return G("Indent") is Thickness ? (Thickness)G("Indent") : new Thickness(0); } }
        public ImageSource Icon { get { return (ImageSource)G("Icon"); } }
        public Visibility IconVis { get { return Icon != null ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility LetterVis { get { return Icon == null ? Visibility.Visible : Visibility.Collapsed; } }
        public string Letter { get { return (string)G("Letter"); } }
        public string Name { get { return (string)G("Name"); } }
        public FontWeight NameWeight { get { return G("Bold") is bool && (bool)G("Bold") ? FontWeights.SemiBold : FontWeights.Normal; } }
        public string Count { get { return (string)G("Count"); } }
        public string Tag { get { return (string)G("Tag"); } }
        public Brush TagFg { get { return (Brush)G("TagFg"); } }
        public Brush TagBg { get { return (Brush)G("TagBg"); } }
        public Visibility TagVis { get { return string.IsNullOrEmpty(Tag) ? Visibility.Collapsed : Visibility.Visible; } }
        public string Cpu { get { return (string)G("Cpu"); } }
        public Brush CpuBg { get { return (Brush)G("CpuBg"); } }
        public string Ram { get { return (string)G("Ram"); } }
        public Brush RamBg { get { return (Brush)G("RamBg"); } }
        public string Gpu { get { return (string)G("Gpu"); } }
        public Brush GpuBg { get { return (Brush)G("GpuBg"); } }
        public string GpuMem { get { return (string)G("GpuMem"); } }
        public string Disk { get { return (string)G("Disk"); } }
        public Brush DiskBg { get { return (Brush)G("DiskBg"); } }
        public string DiskTip { get { return (string)G("DiskTip"); } }
        public string Net { get { return (string)G("Net"); } }
        public string Power { get { return (string)G("Power"); } }
        public Brush PowerFg { get { return (Brush)G("PowerFg"); } }
        public string Publisher { get { return (string)G("Publisher"); } }
        public Brush PublisherFg { get { return (Brush)G("PublisherFg"); } }
        public string Path { get { return (string)G("Path"); } }
        public Brush RowBg { get { return (Brush)G("RowBg"); } }
        public void SetIcon(ImageSource s) { if (Icon == s && v.ContainsKey("Icon")) return; Set("Icon", s); Notify("IconVis"); Notify("LetterVis"); }
        public void SetTag(string t, string fg, string bg) { Set("Tag", t); Set("TagFg", fg == null ? null : Ui.R(fg)); Set("TagBg", bg == null ? null : Ui.R(bg)); Notify("TagVis"); }
        void Notify(string n) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n)); }
    }

    public static class ProcessColumns
    {
        // id, header, width (the name column takes the rest), right-aligned
        public static readonly string[][] Cols = {
            new[] { "cpu", "CPU", "62" }, new[] { "ram", "Memory", "80" }, new[] { "gpu", "GPU", "58" }, new[] { "gpumem", "GPU mem", "76" },
            new[] { "disk", "Disk", "80" }, new[] { "net", "Network", "84" }, new[] { "power", "Power", "80" }, new[] { "publisher", "Publisher", "150" }, new[] { "path", "Path", "200" } };
        const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
        public static string ColDefs()
        {
            var s = "<Grid.ColumnDefinitions><ColumnDefinition Width=\"*\" MinWidth=\"220\"/>";
            foreach (var c in Cols) s += "<ColumnDefinition Width=\"" + c[2] + "\"/>";
            return s + "</Grid.ColumnDefinitions>";
        }
        static string Cell(int col, string text, string bg, string extra = "", string fg = null)
        {
            return "<Border Grid.Column=\"" + col + "\"" + (bg != null ? " Background=\"{Binding " + bg + "}\"" : "") + extra + "><TextBlock Text=\"{Binding " + text + "}\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\" Margin=\"0,0,8,0\" FontFamily=\"{DynamicResource MonoFont}\" FontSize=\"11.5\"" +
                (fg != null ? " Foreground=\"{Binding " + fg + "}\"" : "") + "/></Border>";
        }
        public static DataTemplate RowTemplate()
        {
            var x = "<DataTemplate " + Ns + "><Border Background=\"{Binding RowBg}\" Height=\"26\" BorderThickness=\"0,0,0,1\" BorderBrush=\"{DynamicResource LineDim}\" SnapsToDevicePixels=\"True\"><Grid>" + ColDefs() +
                "<DockPanel Margin=\"{Binding Indent}\" LastChildFill=\"True\">" +
                "<TextBlock x:Name=\"chev\" Tag=\"chev\" DockPanel.Dock=\"Left\" Text=\"{Binding Chevron}\" Width=\"16\" Foreground=\"{DynamicResource Muted}\" VerticalAlignment=\"Center\" TextAlignment=\"Center\" FontSize=\"10\" Background=\"Transparent\" Cursor=\"Hand\"/>" +
                "<Grid DockPanel.Dock=\"Left\" Width=\"16\" Height=\"16\" Margin=\"2,0,8,0\"><Image Source=\"{Binding Icon}\" Visibility=\"{Binding IconVis}\" Width=\"16\" Height=\"16\"/>" +
                "<Border Visibility=\"{Binding LetterVis}\" CornerRadius=\"3\" Background=\"{DynamicResource TileBg}\"><TextBlock Text=\"{Binding Letter}\" FontSize=\"10\" FontWeight=\"Bold\" Foreground=\"{DynamicResource TileText}\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Border></Grid>" +
                "<Border DockPanel.Dock=\"Right\" Visibility=\"{Binding TagVis}\" Background=\"{Binding TagBg}\" BorderBrush=\"{Binding TagFg}\" BorderThickness=\"1\" CornerRadius=\"2\" Padding=\"6,0,6,1\" Margin=\"6,0,8,0\" VerticalAlignment=\"Center\"><TextBlock Text=\"{Binding Tag}\" Foreground=\"{Binding TagFg}\" FontSize=\"10.5\" FontWeight=\"SemiBold\"/></Border>" +
                "<TextBlock DockPanel.Dock=\"Right\" Text=\"{Binding Count}\" Foreground=\"{DynamicResource Muted}\" FontFamily=\"{DynamicResource MonoFont}\" FontSize=\"11\" VerticalAlignment=\"Center\" Margin=\"6,0,0,0\"/>" +
                "<TextBlock Text=\"{Binding Name}\" FontWeight=\"{Binding NameWeight}\" VerticalAlignment=\"Center\" TextTrimming=\"CharacterEllipsis\"/>" +
                "</DockPanel>" +
                Cell(1, "Cpu", "CpuBg") + Cell(2, "Ram", "RamBg") + Cell(3, "Gpu", "GpuBg") + Cell(4, "GpuMem", null) + Cell(5, "Disk", "DiskBg", " ToolTip=\"{Binding DiskTip}\"") + Cell(6, "Net", null) +
                Cell(7, "Power", null, "", "PowerFg") +
                "<TextBlock Grid.Column=\"8\" Text=\"{Binding Publisher}\" Foreground=\"{Binding PublisherFg}\" VerticalAlignment=\"Center\" Margin=\"8,0,6,0\" TextTrimming=\"CharacterEllipsis\" FontSize=\"11.5\"/>" +
                "<TextBlock Grid.Column=\"9\" Text=\"{Binding Path}\" Foreground=\"{DynamicResource Muted}\" VerticalAlignment=\"Center\" Margin=\"6,0,6,0\" TextTrimming=\"CharacterEllipsis\" FontSize=\"11\" ToolTip=\"{Binding Path}\"/>" +
                "</Grid></Border></DataTemplate>";
            return (DataTemplate)XamlReader.Parse(x);
        }
        public static ControlTemplate ListTemplate()
        {
            return (ControlTemplate)XamlReader.Parse("<ControlTemplate TargetType=\"ItemsControl\" " + Ns + "><ScrollViewer x:Name=\"sv\" CanContentScroll=\"True\" Focusable=\"False\" HorizontalScrollBarVisibility=\"Disabled\" VerticalScrollBarVisibility=\"Auto\"><ItemsPresenter/></ScrollViewer></ControlTemplate>");
        }
    }

    // keeps the shown list in step with the newest sample: same view model per key, Move/Insert/Remove only where the order changed
    public class RowSync
    {
        public readonly ObservableCollection<RowVm> Items = new ObservableCollection<RowVm>();
        readonly Dictionary<string, RowVm> byKey = new Dictionary<string, RowVm>();
        public RowVm Get(string key) { RowVm r; return byKey.TryGetValue(key, out r) ? r : null; }
        public RowVm Ensure(string key) { RowVm r; if (!byKey.TryGetValue(key, out r)) byKey[key] = r = new RowVm { Key = key }; return r; }
        public void Order(List<RowVm> want)
        {
            var keep = new HashSet<RowVm>(want);
            for (int i = Items.Count - 1; i >= 0; i--) if (!keep.Contains(Items[i])) Items.RemoveAt(i);
            for (int i = 0; i < want.Count; i++)
            {
                if (i < Items.Count && Items[i] == want[i]) continue;
                int j = Items.IndexOf(want[i]);
                if (j >= 0) Items.Move(j, i); else Items.Insert(i, want[i]);
            }
            while (Items.Count > want.Count) Items.RemoveAt(Items.Count - 1);
            if (byKey.Count > 600) { var keys = new HashSet<string>(want.Select(r => r.Key)); foreach (var k in byKey.Keys.Where(k => !keys.Contains(k)).ToList()) byKey.Remove(k); }
        }
    }
}
