// Hallow Tasks 1.4 - apps that are "Not responding". The sampler marks a process Hung when one of its visible top-level windows is
// hung (IsHungAppWindow - Windows' own "Not responding": it hasn't read its messages for 5 s). The Processes list tags it, and Close
// (in the details strip and the menu) runs Close game's safe flow in "app" mode: ask nicely (WM_CLOSE to its windows), wait 5 s,
// then Force close only if you click it. Never a game (those keep Close game...), anti-cheat, a launcher, Windows or Hallow.
// An optional quiet note (through Hallow Hub, never a pop-up) when an app has been hung for 15 s - held while you play.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HallowTasks
{
    public static class HungClose
    {
        public const int WaitSeconds = 5, NoteSeconds = 15;
        // null = Close may be offered for it; otherwise why not, in plain words
        public static string WhyNot(ProcInfo p, ProcInfo game, IEnumerable<string> extraGames, string windowsDir, int selfPid)
        {
            if (p == null) return "That app has closed.";
            if (p.Pid <= 4 || p.Pid == selfPid) return "Hallow Tasks never closes that one.";
            if (Known.IsAntiCheat(p.Name)) return "That's anti-cheat - Hallow Tasks never touches it.";
            if ((game != null && game.Key == p.Key) || Known.IsGameName(p.Name, extraGames)) return "That's your game - use Close game…";
            if (GameClose.IsLauncher(p.Name)) return "That's a game launcher - close it from its own window or tray icon (closing it can take your game with it).";
            if (Known.IsCritical(p.Name) || Known.IsProtected(p, windowsDir)) return p.Name + " is part of Windows - Hallow Tasks leaves it alone.";
            if (Known.IsHallow(p)) return "That's a Hallow app - quit it from its tray menu (or Hallow Hub).";
            return null;
        }
        // the hung process behind a row (a group or one of its processes), or null when Close isn't offered for it
        public static ProcInfo Pick(AppGroup g, ProcInfo p, ProcInfo game, IEnumerable<string> extraGames, string windowsDir, int selfPid)
        {
            if (p != null) return p.Hung && WhyNot(p, game, extraGames, windowsDir, selfPid) == null ? p : null;
            if (g == null || g.IsGame || g.IsAntiCheat) return null;
            var hit = g.Members.Where(m => m.Hung).OrderByDescending(m => m.HasWindow).ThenBy(m => m.CreateTime).FirstOrDefault();
            return hit != null && WhyNot(hit, game, extraGames, windowsDir, selfPid) == null ? hit : null;
        }
    }

    // "has it been hung for 15 s?" - one quiet note per process, held while a game runs (noted after the game if it's still hung)
    public class HungWatch
    {
        readonly Dictionary<string, DateTime> since = new Dictionary<string, DateTime>();
        readonly HashSet<string> noted = new HashSet<string>();
        public int Count { get; private set; }
        public List<ProcInfo> Due(IList<ProcInfo> procs, ProcInfo game, IEnumerable<string> extraGames, string windowsDir, int selfPid, DateTime now)
        {
            var due = new List<ProcInfo>(); var seen = new HashSet<string>(); int n = 0;
            foreach (var p in procs)
            {
                if (!p.Hung) continue;
                n++; seen.Add(p.Key);
                DateTime t; if (!since.TryGetValue(p.Key, out t)) since[p.Key] = t = now;
                if (game != null || noted.Contains(p.Key) || (now - t).TotalSeconds < HungClose.NoteSeconds) continue;
                if (HungClose.WhyNot(p, game, extraGames, windowsDir, selfPid) != null) continue;
                noted.Add(p.Key); due.Add(p);
            }
            foreach (var k in since.Keys.Where(k => !seen.Contains(k)).ToList()) { since.Remove(k); noted.Remove(k); }   // it recovered or closed
            Count = n;
            return due;
        }
    }
}
