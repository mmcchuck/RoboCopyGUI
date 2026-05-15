using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RoboCopyGUI
{
    public static class ProfileStore
    {
        public static string ProfilesDir
        {
            get
            {
                var dir = Path.Combine(AppSettings.AppDataDir, "profiles");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        static readonly Regex InvalidNameChars = new Regex("[\\\\/:*?\"<>|]");

        public static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (InvalidNameChars.IsMatch(name)) return false;
            if (name.Length > 64) return false;
            // Reserved Windows names (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
            string upper = name.ToUpperInvariant();
            string[] reserved = { "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9" };
            return Array.IndexOf(reserved, upper) < 0;
        }

        public static string PathFor(string name)
        {
            return Path.Combine(ProfilesDir, name + ".ini");
        }

        public static bool Exists(string name)
        {
            return File.Exists(PathFor(name));
        }

        public static List<string> ListNames()
        {
            var names = Directory.GetFiles(ProfilesDir, "*.ini")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return names;
        }

        public static ProfileSettings Load(string name)
        {
            return ProfileSettings.ReadFrom(PathFor(name), name);
        }

        public static void Save(ProfileSettings p)
        {
            if (!IsValidName(p.Name))
                throw new ArgumentException("Invalid profile name: " + p.Name);
            p.WriteTo(PathFor(p.Name));
        }

        public static void Delete(string name)
        {
            try { File.Delete(PathFor(name)); }
            catch (Exception ex) { Logger.Log("delete profile failed (" + name + "): " + ex.Message); }
        }

        public static void Rename(string oldName, string newName)
        {
            if (!IsValidName(newName))
                throw new ArgumentException("Invalid profile name: " + newName);
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return;
            if (Exists(newName))
                throw new IOException("A profile named '" + newName + "' already exists.");
            File.Move(PathFor(oldName), PathFor(newName));
        }

        public static void EnsureInitialized()
        {
            EnsureLegacyMigrated();
            if (ListNames().Count == 0)
            {
                var p = new ProfileSettings { Name = "Default" };
                Save(p);
            }
        }

        static void EnsureLegacyMigrated()
        {
            string legacy = Path.Combine(AppSettings.AppDataDir, "settings.ini");
            if (!File.Exists(legacy)) return;
            // Skip if anything already exists in profiles dir.
            if (Directory.GetFiles(ProfilesDir, "*.ini").Length > 0) return;

            // Read legacy as a profile (it has the per-profile keys) and also harvest app-wide keys.
            var prof = ProfileSettings.ReadFrom(legacy, "Default");
            Save(prof);

            // App-wide pull-over (StartMinimized / StartWithWindows lived in the same file).
            try
            {
                var app = new AppSettings();
                foreach (var raw in File.ReadAllLines(legacy))
                {
                    var line = raw.Trim();
                    int idx = line.IndexOf('=');
                    if (idx < 1) continue;
                    string k = line.Substring(0, idx).Trim();
                    string v = line.Substring(idx + 1).Trim();
                    bool b;
                    if (k == "StartMinimized" && bool.TryParse(v, out b)) app.StartMinimized = b;
                    else if (k == "StartWithWindows" && bool.TryParse(v, out b)) app.StartWithWindows = b;
                }
                app.LastProfile = "Default";
                app.Save();
            }
            catch (Exception ex) { Logger.Log("legacy app-settings migration failed: " + ex.Message); }

            // Rename legacy file as backup rather than deleting outright.
            try { File.Move(legacy, legacy + ".bak"); }
            catch (Exception ex) { Logger.Log("legacy rename failed: " + ex.Message); }
        }
    }
}
