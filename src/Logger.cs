using System;
using System.IO;

namespace RoboCopyGUI
{
    public static class Logger
    {
        static readonly object _lock = new object();
        public static readonly string LogPath = Path.Combine(AppSettings.AppDataDir, "log.txt");
        const long MaxBytes = 2 * 1024 * 1024;

        public static void Log(string msg)
        {
            WriteLine(null, msg);
        }

        public static void Log(string profile, string msg)
        {
            WriteLine(profile, msg);
        }

        static void WriteLine(string profile, string msg)
        {
            lock (_lock)
            {
                try
                {
                    RotateIfNeeded();
                    string prefix = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string tag = string.IsNullOrEmpty(profile) ? "" : " [" + profile + "]";
                    File.AppendAllText(LogPath, prefix + tag + "  " + msg + Environment.NewLine);
                }
                catch { }
            }
        }

        static void RotateIfNeeded()
        {
            try
            {
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var bak = LogPath + ".1";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(LogPath, bak);
                }
            }
            catch { }
        }
    }
}
