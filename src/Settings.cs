// Hallow Tasks settings: %APPDATA%\HallowTasks\settings.json. Nothing secret is kept here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace HallowTasks
{
    [DataContract]
    public class TaskSettings
    {
        // developer tests: HALLOW_TASKS_TESTDIR = data folder (and a separate pipe); HALLOW_TASKS_FAKE=1 = made-up processes and
        // counters (and a fake process control - nothing real is ever ended or changed); HALLOW_TASKS_HEADLESS=1 = no windows or tray.
        public static readonly string TestDir = Env("HALLOW_TASKS_TESTDIR");
        public static readonly bool Fake = TestDir != null && Env("HALLOW_TASKS_FAKE") == "1";
        public static readonly bool Headless = TestDir != null && Env("HALLOW_TASKS_HEADLESS") == "1";
        public static bool Frozen;   // --snap: never saved
        static string Env(string n) { var v = Environment.GetEnvironmentVariable(n); return string.IsNullOrWhiteSpace(v) ? null : v; }
        public static string Dir { get { return TestDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowTasks"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        [DataMember] public bool UseHallowIcon = true;
        [DataMember] public bool UseHallowAccent = true;
        [DataMember] public bool Hotkey;                    // Ctrl+Alt+T opens Hallow Tasks - off by default
        [DataMember] public bool PerCore;                   // Performance: one graph per core
        [DataMember] public List<string> ExtraGames;        // more exe names treated as games
        [DataMember] public string Sort = "impact";
        [DataMember] public bool SortUp;
        [DataMember] public string LastUpdateCheck;
        [DataMember] public string LastCrashShown;
        [DataMember] public bool TrayHintShown;
        // 1.1
        [DataMember] public string Look = "hallow";        // "hallow" (follow theme.json's mode), "light", "dark"
        [DataMember] public bool HistoryInTray = true;      // keep the rolling hour while the window is closed (a sample every 5 s)
        [DataMember] public bool HistoryLog;                // the 24-hour log on disk (one line a minute, at most ~1 MB) - off by default
        [DataMember] public bool Alerts;                    // a quiet note through Hallow Hub while you play - off by default
        [DataMember] public int AlertCpu = 25, AlertGpu = 25, AlertSeconds = 20;
        [DataMember] public bool ControlMicrosoftServices;  // Services: Microsoft's are "leave alone" unless this is on
        [DataMember] public bool TreeView;                  // Processes: parent -> children instead of app groups
        // 1.4
        [DataMember] public bool HungNotes = true;          // a quiet note (through Hub) when an app has been "Not responding" for 15 s - held while you play
        [DataMember] public List<string> BoostSkip;         // exe names Game boost never lists
        [DataMember] public List<BoostItem> BoostProfile;   // a saved Boost profile (what to lower / put in Efficiency mode / close)
        [DataMember] public bool BoostAuto;                 // apply the Boost profile when a game starts - off until you tick it
        // 1.5
        [DataMember] public bool CloseAskFirst;             // "When closing a game": false = end it right away (default), true = ask it to close first (10 s)
        [DataMember] public bool CloseNoConfirm;            // "Don't ask again": Close game / Close (not responding) / Game boost's Close skip the confirm

        [OnDeserializing] void Defaults(StreamingContext c) { UseHallowIcon = true; UseHallowAccent = true; Sort = "impact"; Look = "hallow"; HistoryInTray = true; AlertCpu = 25; AlertGpu = 25; AlertSeconds = 20; HungNotes = true; }

        public static TaskSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var f = File.OpenRead(FilePath))
                    {
                        var s = (TaskSettings)new DataContractJsonSerializer(typeof(TaskSettings)).ReadObject(f);
                        if (s != null) { s.Fix(); return s; }
                    }
            }
            catch { }
            var d = new TaskSettings(); d.Fix(); return d;
        }
        void Fix()
        {
            if (ExtraGames == null) ExtraGames = new List<string>();
            ExtraGames.RemoveAll(string.IsNullOrWhiteSpace);
            if (BoostSkip == null) BoostSkip = new List<string>(); BoostSkip.RemoveAll(string.IsNullOrWhiteSpace);
            if (BoostProfile == null) BoostProfile = new List<BoostItem>(); BoostProfile.RemoveAll(b => b == null || string.IsNullOrEmpty(b.Exe) || string.IsNullOrEmpty(b.Action));
            if (string.IsNullOrEmpty(Sort)) Sort = "impact";
            if (Look != "light" && Look != "dark") Look = "hallow";
            AlertCpu = Math.Max(5, Math.Min(100, AlertCpu)); AlertGpu = Math.Max(5, Math.Min(100, AlertGpu)); AlertSeconds = Math.Max(5, Math.Min(600, AlertSeconds));
        }

        static readonly object saveLock = new object();
        public void Save()
        {
            if (Frozen) return;
            lock (saveLock)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    var tmp = FilePath + ".tmp";
                    using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(TaskSettings)).WriteObject(f, this);
                    for (int i = 0; ; i++) { try { if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath); break; } catch { if (i >= 5) throw; Thread.Sleep(50); } }
                }
                catch { }
            }
        }
    }
}
