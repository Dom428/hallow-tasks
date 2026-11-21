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

        [OnDeserializing] void Defaults(StreamingContext c) { UseHallowIcon = true; UseHallowAccent = true; Sort = "impact"; }

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
            if (string.IsNullOrEmpty(Sort)) Sort = "impact";
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
