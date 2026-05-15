using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RoboCopyGUI
{
    public class AppSettings
    {
        public bool StartMinimized = true;
        public bool StartWithWindows = false;
        public string LastProfile = "";

        public static string AppDataDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RoboCopyGUI");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(AppDataDir, "app.ini"); }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            if (!File.Exists(FilePath)) return s;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int idx = line.IndexOf('=');
                if (idx < 1) continue;
                string k = line.Substring(0, idx).Trim();
                string v = line.Substring(idx + 1).Trim();
                switch (k)
                {
                    case "StartMinimized": s.StartMinimized = ParseBool(v, true); break;
                    case "StartWithWindows": s.StartWithWindows = ParseBool(v, false); break;
                    case "LastProfile": s.LastProfile = v; break;
                }
            }
            return s;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# RoboCopyGUI app-wide settings");
            sb.AppendLine("StartMinimized=" + StartMinimized);
            sb.AppendLine("StartWithWindows=" + StartWithWindows);
            sb.AppendLine("LastProfile=" + LastProfile);
            File.WriteAllText(FilePath, sb.ToString());
        }

        static bool ParseBool(string s, bool dflt)
        {
            bool b;
            return bool.TryParse(s, out b) ? b : dflt;
        }
    }
}
