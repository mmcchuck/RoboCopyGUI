using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RoboCopyGUI
{
    public class ProfileSettings
    {
        public string Name = "Default";
        public string WatchFolder = "";
        public string DestinationFolder = "";
        public int PollSeconds = 60;
        public bool WaitForFinish = true;
        public int StableSeconds = 10;
        public bool MoveMode = false;
        public bool Recursive = true;
        public bool AlertWhenDone = true;
        public bool RunSilent = false;
        public string ExtraArgs = "/R:1 /W:1 /NP";
        public bool Paused = false;

        // Transient flag: when true, the next scan treats every file that passes
        // the lock probe as ready (skipping the size-stable wait). The Watcher
        // clears the flag after consuming it; we deliberately do NOT serialize
        // it to disk so it never auto-fires on a future launch.
        public bool RunOnExisting = false;

        public ProfileSettings Clone()
        {
            return (ProfileSettings)MemberwiseClone();
        }

        public void WriteTo(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# RoboCopyGUI profile: " + Name);
            sb.AppendLine("WatchFolder=" + WatchFolder);
            sb.AppendLine("DestinationFolder=" + DestinationFolder);
            sb.AppendLine("PollSeconds=" + PollSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("WaitForFinish=" + WaitForFinish);
            sb.AppendLine("StableSeconds=" + StableSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("MoveMode=" + MoveMode);
            sb.AppendLine("Recursive=" + Recursive);
            sb.AppendLine("AlertWhenDone=" + AlertWhenDone);
            sb.AppendLine("RunSilent=" + RunSilent);
            sb.AppendLine("ExtraArgs=" + ExtraArgs);
            sb.AppendLine("Paused=" + Paused);
            File.WriteAllText(path, sb.ToString());
        }

        public static ProfileSettings ReadFrom(string path, string name)
        {
            var p = new ProfileSettings { Name = name };
            if (!File.Exists(path)) return p;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int idx = line.IndexOf('=');
                if (idx < 1) continue;
                string k = line.Substring(0, idx).Trim();
                string v = line.Substring(idx + 1).Trim();
                switch (k)
                {
                    case "WatchFolder": p.WatchFolder = v; break;
                    case "DestinationFolder": p.DestinationFolder = v; break;
                    case "PollSeconds": p.PollSeconds = ParseInt(v, 60); break;
                    case "WaitForFinish": p.WaitForFinish = ParseBool(v, true); break;
                    case "StableSeconds": p.StableSeconds = ParseInt(v, 10); break;
                    case "MoveMode": p.MoveMode = ParseBool(v, false); break;
                    case "Recursive": p.Recursive = ParseBool(v, true); break;
                    case "AlertWhenDone": p.AlertWhenDone = ParseBool(v, true); break;
                    case "RunSilent": p.RunSilent = ParseBool(v, false); break;
                    case "ExtraArgs": p.ExtraArgs = v; break;
                    case "Paused": p.Paused = ParseBool(v, false); break;
                }
            }
            return p;
        }

        static int ParseInt(string s, int dflt)
        {
            int n;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : dflt;
        }

        static bool ParseBool(string s, bool dflt)
        {
            bool b;
            return bool.TryParse(s, out b) ? b : dflt;
        }
    }
}
