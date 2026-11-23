// Hallow Tasks 1.4 pages: Game boost, and its Settings card (with the "Not responding" note switch).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowTasks
{
    public partial class MainWindow
    {
        // ================= Game boost
        readonly HashSet<string> boostSel = new HashSet<string>(), boostReopen = new HashSet<string>(); ScrollViewer boostScroll;
        FrameworkElement BoostPage()
        {
            var sp = PageBody("Game boost", "Before or while you play: what's using the CPU, RAM, GPU or network right now. Lower it, put it in Efficiency mode, or close it - each only when you click. After the game, priorities go back and the apps you ticked \"reopen after\" start again. Never listed: games, anti-cheat, Windows, Hallow apps and the apps you opt out.");
            // where we are
            var head = new DockPanel();
            var gi = new StackPanel { Orientation = Orientation.Horizontal };
            gi.Children.Add(Icons.Make("bolt", 20, Ui.R(c.Game != null ? "Green" : "Lilac")));
            var gt = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            gt.Children.Add(Ui.T(c.Game != null ? "Playing " + c.GameTitle : "No game running", 15, true));
            gt.Children.Add(Ui.Muted(c.Game != null ? (c.Boost.Active ? "Boosted - everything goes back when the game closes." : "Boost now: your game and anti-cheat are never touched.") : (c.Boost.Active ? "Boosted - everything goes back after your next game, or when you click Restore now." : "Boost before you start a game: it stays until that game closes."), 11.5));
            gi.Children.Add(gt); head.Children.Add(gi);
            var hc = Ui.Card(head, 14); hc.BorderBrush = Ui.R(c.Game != null ? "Green" : "Line"); hc.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(hc);

            var rows = c.BoostRows();
            var keys = new HashSet<string>(rows.Select(r => r.Group.Key)); boostSel.RemoveWhere(k => !keys.Contains(k));
            var table = new StackPanel();
            var hdr = BoostGrid();
            Action<int, string, HorizontalAlignment> h = (col, t, al) => { var tb = Ui.Muted(t, 11); tb.HorizontalAlignment = al; tb.Margin = new Thickness(6, 0, 6, 0); Grid.SetColumn(tb, col); hdr.Children.Add(tb); };
            h(1, "App", HorizontalAlignment.Left); h(2, "CPU", HorizontalAlignment.Right); h(3, "RAM", HorizontalAlignment.Right); h(4, "GPU", HorizontalAlignment.Right); h(5, "Network", HorizontalAlignment.Right);
            hdr.Margin = new Thickness(0, 0, 0, 6); table.Children.Add(hdr);
            int i = 0;
            foreach (var r in rows)
            {
                i++; var g = r.Group; var key = g.Key;
                var row = BoostGrid(); row.MinHeight = 38;
                var bg = new Border { Background = i % 2 == 0 ? Ui.R("RowChild") : Ui.R("Heat0"), BorderBrush = Ui.R("LineDim"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
                var sel = new CheckBox { IsChecked = boostSel.Contains(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), IsEnabled = r.CanClose, ToolTip = r.CanClose ? "Pick it for Close selected" : r.Note };
                sel.Click += (s, e) => { if (sel.IsChecked == true) boostSel.Add(key); else boostSel.Remove(key); };
                row.Children.Add(sel);
                var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(Ui.Tile(g.Title, Ui.ProgramIcon(g.Path), 16));
                var nt = Ui.T(g.Title, 12.5, true); nt.Margin = new Thickness(8, 0, 0, 0); nt.TextWrapping = TextWrapping.NoWrap; nt.TextTrimming = TextTrimming.CharacterEllipsis; nt.MaxWidth = 200; name.Children.Add(nt);
                if (g.Members.Count > 1) { var cnt = Ui.Muted("(" + g.Members.Count + ")", 11); cnt.Margin = new Thickness(5, 0, 0, 0); cnt.FontFamily = Ui.Mono; name.Children.Add(cnt); }
                if (r.Note != null) { var pl = Ui.Pill("Launcher", "Lilac", "LilacBg"); pl.Margin = new Thickness(8, 0, 0, 0); pl.ToolTip = r.Note; name.Children.Add(pl); }
                Grid.SetColumn(name, 1); row.Children.Add(name);
                Action<int, string, bool> num = (col, t, hot) => { var tb = Ui.Num(t, 12, Ui.R(hot ? "Amber" : "Text")); tb.HorizontalAlignment = HorizontalAlignment.Right; tb.VerticalAlignment = VerticalAlignment.Center; tb.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(tb, col); row.Children.Add(tb); };
                num(2, Fmt.Pct(r.Cpu), r.Cpu >= 10); num(3, Fmt.Bytes(r.Ram), r.Ram >= 1L << 30); num(4, Fmt.Pct(r.Gpu), r.Gpu >= 10);
                num(5, r.Net >= 0 ? Fmt.Bits(r.Net) : r.Conns > 0 ? r.Conns + " conn" : "-", r.Net >= 1048576);
                var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                Action<Button> add = b => { b.Padding = new Thickness(8, 3, 8, 3); b.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(b); };
                var lowered = c.Boost.Keeper.Lowered.Any(e => g.Members.Any(m => m.Pid == e.Pid && m.CreateTime == e.Created));
                var eco = c.Boost.EcoSet.Any(e => g.Members.Any(m => m.Pid == e.Pid && m.CreateTime == e.Created));
                var closing = c.Boost.Closed.FirstOrDefault(x => x.Key == key);
                if (closing != null) { var done = closing.State == "closed" || closing.State == "forced" || closing.State == "reopened"; var pl = Ui.Pill(closing.State == "asking" ? "Asked to close" : done ? "Closed" : closing.State == "denied" ? "Protected" : "Still running", done ? "Green" : "Amber", done ? "GreenBg" : "AmberBg"); pl.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(pl); }
                else
                {
                    if (lowered) { var pl = Ui.Pill("Below normal", "Lilac", "LilacBg"); pl.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(pl); }
                    else if (!eco) add(Ui.Btn("Below normal", () => Act(c.BoostLower(g, Prio.BelowNormal), g.Title + " is at Below normal until the game closes."), false, "Gets the CPU after your game - put back after the game."));
                    if (eco) { var pl = Ui.Pill("Efficiency", "Green", "GreenBg"); pl.Margin = new Thickness(0, 0, 6, 0); acts.Children.Add(pl); }
                    else add(Ui.Btn("Efficiency", () => Act(c.BoostEco(g), g.Title + " is in Efficiency mode until the game closes."), false, EcoKeeper.What));
                    if (r.CanClose)
                    {
                        add(Ui.Danger("Close", () =>
                        {
                            bool reopen = boostReopen.Contains(key);
                            if (!Dialogs.Confirm(this, "Close " + g.Title + "?", "Windows asks it to close its windows (like Task Manager's End task). If it's still running after " + c.Boost.WaitSeconds + " s, Force close shows here - only if you click it. " + (reopen ? "It starts again after the game." : "Tick \"reopen after\" to start it again after the game.") + " Your game isn't touched.", "Close", false)) return;
                            Act(c.BoostClose(g, reopen), "Asked " + g.Title + " to close.");
                        }));
                        var ro = new CheckBox { Content = Ui.Muted("reopen after", 11.5), IsChecked = boostReopen.Contains(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Start it again after the game (from its own program file, the same way it was started)." };
                        ro.Click += (s, e) => { if (ro.IsChecked == true) boostReopen.Add(key); else boostReopen.Remove(key); };
                        acts.Children.Add(ro);
                    }
                }
                var skip = Ui.Btn("Skip", () => { c.BoostSkip(g); Say(g.Title + " won't be listed in Game boost (Settings can change that)."); gameAt = null; Refresh(); }, false, "Never list it here (Settings > Game boost).");
                skip.Padding = new Thickness(8, 3, 8, 3); skip.Margin = new Thickness(0); acts.Children.Add(skip);
                Grid.SetColumn(acts, 6); row.Children.Add(acts);
                table.Children.Add(bg);
            }
            if (rows.Count == 0) table.Children.Add(Ui.Muted("Nothing else is using much right now.", 12.5));
            sp.Children.Add(Ui.Card(table, 12));
            var bar = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var cs = Ui.Danger("Close selected (" + boostSel.Count + ")", () =>
            {
                var pick = rows.Where(r => boostSel.Contains(r.Group.Key) && r.CanClose).ToList(); if (pick.Count == 0) return;
                if (!Dialogs.Confirm(this, "Close " + pick.Count + " app" + (pick.Count == 1 ? "" : "s") + "?", string.Join(", ", pick.Select(r => r.Group.Title)) + ". Windows asks each to close its windows; Force close shows for any still running after " + c.Boost.WaitSeconds + " s - only if you click it. Ticked \"reopen after\" ones start again after the game.", "Close", false)) return;
                var errs = pick.Select(r => c.BoostClose(r.Group, boostReopen.Contains(r.Group.Key))).Where(e => e != null).ToList(); boostSel.Clear();
                Act(errs.FirstOrDefault(), "Asked " + pick.Count + " app" + (pick.Count == 1 ? "" : "s") + " to close.");
            });
            cs.IsEnabled = boostSel.Count > 0; cs.Margin = new Thickness(0, 0, 8, 0); bar.Children.Add(cs);
            var rn = Ui.Btn("Restore now", () => { var m = c.BoostRestore(); Say(m); gameAt = null; Refresh(); }, false, "Priorities and Efficiency back, and the apps you ticked start again - now instead of after the game.");
            rn.IsEnabled = c.Boost.Active; rn.Margin = new Thickness(0, 0, 8, 0); bar.Children.Add(rn);
            var save = Ui.Btn("Save as Boost profile", () => { c.SaveBoostProfile(); Say("Saved your Boost profile: " + c.S.BoostProfile.Count + " app" + (c.S.BoostProfile.Count == 1 ? "" : "s") + "."); gameAt = null; Refresh(); }, false, "Remembers what you did this time (by exe name) so it can be done again - by itself only if you tick \"apply when a game starts\".");
            save.IsEnabled = c.Boost.Done.Count > 0; bar.Children.Add(save);
            sp.Children.Add(bar);

            // what's boosted now
            var box = new StackPanel();
            box.Children.Add(Ui.Caps("Boosted now", 11));
            if (!c.Boost.Active) box.Children.Add(Ui.Muted("Nothing yet. What you do here is put back after your next game, when you click Restore now, or (priorities and Efficiency) when you quit Hallow Tasks.", 12));
            foreach (var e in c.Boost.Keeper.Lowered.ToList()) box.Children.Add(Line(e.Name + "  PID " + e.Pid + "   " + PriorityKeeper.Word(e.Was) + " → " + PriorityKeeper.Word(e.Now), null));
            foreach (var e in c.Boost.EcoSet.ToList()) box.Children.Add(Line(e.Name + "  PID " + e.Pid + "   Efficiency mode (EcoQoS + Idle)", null));
            foreach (var cl in c.Boost.Closed.ToList())
            {
                var cc = cl; string what;
                switch (cl.State)
                {
                    case "asking": what = "asked to close - waiting " + c.Boost.SecondsLeft(cl, DateTime.Now) + " s"; break;
                    case "still": what = "still running after " + c.Boost.WaitSeconds + " s"; break;
                    case "closed": what = "closed"; break;
                    case "forced": what = "force closed"; break;
                    case "denied": what = "Windows won't let Hallow Tasks end it"; break;
                    default: what = cl.State; break;
                }
                Button fb = null;
                if (cl.State == "still") fb = Ui.Danger("Force close", () =>
                {
                    if (!Dialogs.Confirm(this, "Force close " + cc.Title + "?", "It ends right away - anything unsaved in it is lost. Your game, anti-cheat, launchers, Windows and Hallow are never touched.", "Force close", true)) return;
                    Act(c.BoostForce(cc.Key), cc.Title + " was force closed.");
                });
                box.Children.Add(Line(cl.Title + "   " + what + (cl.Reopen ? "  ·  reopens after the game" : ""), fb));
            }
            var card = Ui.Card(box, 14); card.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(card);

            // the profile
            var pbox = new StackPanel();
            pbox.Children.Add(Ui.Caps("Boost profile", 11));
            if (c.S.BoostProfile.Count == 0) pbox.Children.Add(Ui.Muted("None saved. Boost a few apps above, then Save as Boost profile.", 12));
            else
            {
                pbox.Children.Add(Ui.Muted(string.Join("   ·   ", c.S.BoostProfile.Select(b => (b.Title ?? b.Exe) + ": " + BoostItem.Word(b.Action) + (b.Action == "close" && b.Reopen ? " (reopen after)" : ""))), 12, 6));
                var auto = Ui.Switch("Apply it when a game starts (closing only asks nicely - Force close is always your click)", c.S.BoostAuto, on => { c.S.BoostAuto = on; c.S.Save(); });
                pbox.Children.Add(auto);
                var clr = Ui.Btn("Forget the profile", () => { c.S.BoostProfile.Clear(); c.S.BoostAuto = false; c.S.Save(); gameAt = null; Refresh(); }); clr.HorizontalAlignment = HorizontalAlignment.Left; clr.Margin = new Thickness(0, 6, 0, 0); pbox.Children.Add(clr);
            }
            var pcard = Ui.Card(pbox, 14); pcard.Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(pcard);
            var sv = Scroll(sp); boostScroll = sv; return sv;
        }
        void Act(string err, string ok) { if (err != null) Dialogs.Info(this, "Hallow Tasks", err); else Say(ok); gameAt = null; Refresh(); }
        static UIElement Line(string text, Button right)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (right != null) { right.Padding = new Thickness(8, 3, 8, 3); right.Margin = new Thickness(0); DockPanel.SetDock(right, Dock.Right); row.Children.Add(right); }
            var t = Ui.Num(text, 12, Ui.R("Text")); t.VerticalAlignment = VerticalAlignment.Center; t.TextTrimming = TextTrimming.CharacterEllipsis; row.Children.Add(t);
            return row;
        }
        static Grid BoostGrid()
        {
            var g = new Grid();
            foreach (var w in new[] { 30.0, -1, 64, 80, 64, 90, 470 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            return g;
        }

        // ================= Settings card (1.4)
        FrameworkElement BoostSettingsCard()
        {
            var b = new StackPanel();
            b.Children.Add(Ui.Caps("Not responding and Game boost", 11));
            b.Children.Add(Ui.Switch("A quiet note through Hallow Hub when an app has been \"Not responding\" for 15 s (held while you play)", c.S.HungNotes, on => { c.S.HungNotes = on; c.S.Save(); }));
            b.Children.Add(Ui.Muted("Game boost never lists these apps (exe names, comma between them):", 12, 6));
            var row = new DockPanel();
            var box = Ui.Field(string.Join(", ", c.S.BoostSkip));
            var save = Ui.Btn("Save", () => { c.SetBoostSkip(box.Text); Say("Game boost skips " + c.S.BoostSkip.Count + " app" + (c.S.BoostSkip.Count == 1 ? "" : "s") + "."); }, true);
            save.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(save, Dock.Right); row.Children.Add(save); row.Children.Add(box);
            b.Children.Add(row);
            b.Children.Add(Ui.Muted("Never listed at all: games, anti-cheat, Windows and Hallow apps. Launchers (Steam, Battle.net...) can be lowered but not closed - your game may need them.", 11.5, 0));
            var card = Ui.Card(b); card.Margin = new Thickness(0, 12, 0, 0); return card;
        }
    }
}
