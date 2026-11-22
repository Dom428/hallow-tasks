// Hallow Tasks - light and dark mode (the Hallow family spec). %APPDATA%\Hallow\theme.json "mode" = "dark" | "light" | "system"
// (missing = dark, today's look); Settings > Look can override it for Hallow Tasks only (Follow Hallow / Light / Dark). "system"
// follows Windows' "apps use light theme" (HKCU ...\Themes\Personalize AppsUseLightTheme), checked every few seconds. The dark
// palette is Theme.xaml as it is; light swaps only the values of the same resource keys. On light backgrounds the accent is
// darkened (HSL) until text in it reads at 4.5:1 on white; the status colours use the family's text-safe values.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace HallowTasks
{
    public static class Look
    {
        // ---- the switch
        // theme.json "mode": "dark" (also when missing or unknown), "light" or "system"
        public static string ReadMode()
        {
            try
            {
                if (!File.Exists(Accent.FilePath)) return "dark";
                string text = null;
                for (int i = 0; i < 3 && text == null; i++) { try { text = File.ReadAllText(Accent.FilePath); } catch (IOException) { System.Threading.Thread.Sleep(50); } }
                return ModeOf(text);
            }
            catch { return "dark"; }
        }
        public static string ModeOf(string json)
        {
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json ?? ""); object m;
                var s = d != null && d.TryGetValue("mode", out m) ? (m as string ?? "").Trim().ToLowerInvariant() : "";
                return s == "light" || s == "system" ? s : "dark";
            }
            catch { return "dark"; }
        }
        // Windows' own app mode: true = light, false = dark, null = not known (treated as dark)
        public static bool? WindowsLight()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) return (int)v == 1;
                }
            }
            catch { }
            return null;
        }
        // per-app Look ("hallow" = follow Hallow, "light", "dark") wins; then theme.json's mode; "system" asks Windows
        public static bool Resolve(string hallowMode, string appLook, Func<bool?> windowsLight)
        {
            var a = (appLook ?? "").Trim().ToLowerInvariant();
            if (a == "light") return true;
            if (a == "dark") return false;
            var m = (hallowMode ?? "").Trim().ToLowerInvariant();
            if (m == "light") return true;
            if (m == "system") { var w = windowsLight == null ? null : windowsLight(); return w == true; }
            return false;
        }
        public static bool NeedsWindows(string hallowMode, string appLook)
        {
            var a = (appLook ?? "").Trim().ToLowerInvariant();
            return a != "light" && a != "dark" && (hallowMode ?? "").Trim().ToLowerInvariant() == "system";
        }
        public static bool Current(TaskSettings s) { return Resolve(ReadMode(), s.Look, WindowsLight); }

        // ---- the light palette (same keys as Theme.xaml; #F07820 / its alpha forms are swapped for the darkened accent later)
        public static readonly Dictionary<string, string> Light = new Dictionary<string, string> {
            { "Bg", "#F6F4FA" }, { "Side", "#EFECF5" }, { "Panel", "#FFFFFF" }, { "PanelHi", "#EEEAF5" }, { "Well", "#FFFFFF" },
            { "Line", "#DAD3E6" }, { "LineDim", "#E9E5F0" }, { "BtnBg", "#FFFFFF" }, { "BtnLine", "#C8BFD8" },
            { "Text", "#1E1A26" }, { "TextSoft", "#3A3446" }, { "Muted", "#6B6478" }, { "Faint", "#9C95A8" },
            { "AccentGlow", "#26F07820" }, { "Lilac", "#6E50B5" }, { "LilacDim", "#B5A3DD" },
            { "Green", "#1B7A44" }, { "Amber", "#8F5B00" }, { "Red", "#C2333F" },
            { "RedBg", "#1AC2333F" }, { "AmberBg", "#1FA66A00" }, { "GreenBg", "#1A1E8E4F" }, { "LilacBg", "#1A7A5CC0" },
            { "RowSel", "#2EF07820" }, { "RowHover", "#147A5CC0" }, { "RowChild", "#081E1A26" },
            { "Heat1", "#1AF07820" }, { "Heat2", "#30F07820" }, { "Heat3", "#4DF07820" },
            { "GraphBg", "#FBFAFD" }, { "GraphGrid", "#E9E5F0" }, { "GraphCpuFill", "#26F07820" }, { "Graph3D", "#7A5CC0" }, { "Graph3DFill", "#267A5CC0" },
            { "GraphEncode", "#1E8E4F" }, { "GraphDecode", "#A66A00" }, { "GraphCopy", "#1F6FB4" }, { "GraphRam", "#1E8E4F" }, { "GraphRamFill", "#221E8E4F" },
            { "GraphVram", "#7A5CC0" }, { "GraphVramFill", "#267A5CC0" }, { "GraphRead", "#1F6FB4" }, { "GraphReadFill", "#221F6FB4" },
            { "GraphDown", "#1E8E4F" }, { "GraphDownFill", "#221E8E4F" }, { "GraphUp", "#A66A00" },
            { "TileBg", "#E6E0F0" }, { "TileText", "#5B3FA8" }, { "CheckedBg", "#26F07820" },
            { "SpikeMark", "#A66A00" }, { "StutterMark", "#C2333F" }, { "GameBand", "#141E8E4F" } };
        public static string ToLight(string xaml)
        {
            foreach (var kv in Light)
                xaml = Regex.Replace(xaml, "(x:Key=\"" + kv.Key + "\" Color=\")#[0-9A-Fa-f]{6,8}(\")", "${1}" + kv.Value + "${2}");
            // the Accent button's text: dark text on the light accent tint (better than accent-on-accent); the soft accent = the text-safe one
            xaml = xaml.Replace("<Setter Property=\"Foreground\" Value=\"#F59A50\"/>", "<Setter Property=\"Foreground\" Value=\"{StaticResource Text}\"/>");
            return Regex.Replace(xaml, "#F59A50", "#F07820", RegexOptions.IgnoreCase);
        }

        // ---- contrast (WCAG 2)
        public static double[] Rgba(string hex)
        {
            var h = hex.TrimStart('#'); double a = 1;
            if (h.Length == 8) { a = int.Parse(h.Substring(0, 2), NumberStyles.HexNumber) / 255.0; h = h.Substring(2); }
            return new[] { int.Parse(h.Substring(0, 2), NumberStyles.HexNumber) / 255.0, int.Parse(h.Substring(2, 2), NumberStyles.HexNumber) / 255.0, int.Parse(h.Substring(4, 2), NumberStyles.HexNumber) / 255.0, a };
        }
        // a see-through colour over an opaque one
        public static string Over(string top, string under)
        {
            var t = Rgba(top); var u = Rgba(under);
            Func<int, string> ch = i => ((int)Math.Round((t[i] * t[3] + u[i] * (1 - t[3])) * 255)).ToString("X2");
            return "#" + ch(0) + ch(1) + ch(2);
        }
        public static double Luminance(string hex)
        {
            var c = Rgba(hex); Func<double, double> f = v => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]);
        }
        public static double Contrast(string a, string b)
        {
            double x = Luminance(a), y = Luminance(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }
        // the accent for text and thin lines on light backgrounds: darkened in HSL until it reads at 4.5:1 on white - and on the
        // darkest light surface it sits on (the side bar, #EFECF5), so the "HALLOW" title and sort arrows pass too
        public static string DarkAccent(string accent)
        {
            try
            {
                var c = Rgba(accent); double h, s, l; ToHsl(c[0], c[1], c[2], out h, out s, out l);
                var hex = accent.ToUpperInvariant();
                while ((Contrast(hex, "#FFFFFF") < 4.5 || Contrast(hex, "#EFECF5") < 4.5) && l > 0) { l = Math.Max(0, l - 0.01); hex = FromHsl(h, s, l); }
                return hex;
            }
            catch { return "#B85410"; }
        }
        static void ToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)); l = (max + min) / 2;
            if (max == min) { h = 0; s = 0; return; }
            double d = max - min; s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0); else if (max == g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
            h /= 6;
        }
        static string FromHsl(double h, double s, double l)
        {
            Func<double, double, double, double> hue = (p, q, t) => { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1.0 / 6) return p + (q - p) * 6 * t; if (t < 0.5) return q; if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6; return p; };
            double r, g, b;
            if (s == 0) r = g = b = l;
            else { double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q; r = hue(p, q, h + 1.0 / 3); g = hue(p, q, h); b = hue(p, q, h - 1.0 / 3); }
            Func<double, string> x = v => ((int)Math.Round(Math.Max(0, Math.Min(1, v)) * 255)).ToString("X2");
            return "#" + x(r) + x(g) + x(b);
        }

        // every text / background pair the light palette uses: name, text colour, background, minimum (4.5 for text, 3 for icons and big text)
        public static List<object[]> LightPairs(string accent)
        {
            var A = DarkAccent(accent); Func<string, string> L = k => Light[k].Replace("F07820", A.Substring(1));
            var panel = L("Panel"); var bg = L("Bg"); var hi = L("PanelHi"); var side = L("Side");
            var list = new List<object[]>();
            foreach (var surface in new[] { new[] { "Bg", bg }, new[] { "Panel", panel }, new[] { "PanelHi", hi }, new[] { "Side", side }, new[] { "RowChild", Over(L("RowChild"), panel) } })
                foreach (var t in new[] { "Text", "TextSoft", "Muted", "Lilac", "Green", "Amber", "Red" })
                    list.Add(new object[] { t + " on " + surface[0], L(t), surface[1], 4.5 });
            list.Add(new object[] { "accent text (HALLOW, sort arrow) on Panel", A, panel, 4.5 });
            list.Add(new object[] { "accent text on Bg", A, bg, 4.5 });
            list.Add(new object[] { "accent text on Side", A, side, 4.5 });
            list.Add(new object[] { "Accent button text on its tint", L("Text"), Over("#24" + A.Substring(1), Over("#47" + A.Substring(1), panel)), 4.5 });
            list.Add(new object[] { "text on the selected row", L("Text"), Over(L("RowSel"), panel), 4.5 });
            list.Add(new object[] { "text on the busiest cell tint", L("Text"), Over(L("Heat3"), panel), 4.5 });
            foreach (var p in new[] { new[] { "Green", "GreenBg" }, new[] { "Amber", "AmberBg" }, new[] { "Red", "RedBg" }, new[] { "Lilac", "LilacBg" } })
                list.Add(new object[] { p[0] + " pill text on " + p[1], L(p[0]), Over(L(p[1]), panel), 4.5 });
            list.Add(new object[] { "text on the Danger button", L("Text"), Over(L("RedBg"), panel), 4.5 });
            list.Add(new object[] { "letter tile", L("TileText"), L("TileBg"), 4.5 });
            list.Add(new object[] { "on-accent text on a filled accent", Accent.OnAccent(A), A, 4.5 });
            foreach (var g in new[] { "GraphEncode", "GraphDecode", "GraphCopy", "GraphRam", "Graph3D", "SpikeMark", "StutterMark" })
                list.Add(new object[] { g + " line on the graph", L(g), L("GraphBg"), 3.0 });
            list.Add(new object[] { "CPU graph line (accent)", A, L("GraphBg"), 3.0 });
            return list;
        }
    }
}
