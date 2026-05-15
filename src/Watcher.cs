using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace RoboCopyGUI
{
    public class Watcher
    {
        readonly Timer _timer = new Timer();
        ProfileSettings _settings;
        readonly Dictionary<string, FileState> _state =
            new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        bool _running;

        public event Action<string, int, bool> RunCompleted; // profile, exitCode, success
        public event Action<string, string> StatusChanged;   // profile, status

        public string ProfileName { get { return _settings.Name; } }

        class FileState
        {
            public long Size;
            public DateTime LastChangeUtc;
        }

        public Watcher(ProfileSettings s)
        {
            _settings = s;
            _timer.Tick += (a, b) => SafeScan();
            ApplyInterval();
        }

        public void UpdateSettings(ProfileSettings s)
        {
            // If the name changed (rename), forget the old size-stable state.
            if (!string.Equals(_settings.Name, s.Name, StringComparison.OrdinalIgnoreCase))
                _state.Clear();
            _settings = s;
            ApplyInterval();
            if (_settings.Paused) Stop(); else Start();
        }

        void ApplyInterval()
        {
            _timer.Interval = Math.Max(5, _settings.PollSeconds) * 1000;
        }

        public bool IsRunning { get { return _running; } }
        public bool IsPaused { get { return _settings.Paused; } }

        public void Start()
        {
            if (_settings.Paused) return;
            _timer.Start();
            _running = true;
            RaiseStatus("Running");
        }

        public void Stop()
        {
            _timer.Stop();
            _running = false;
            RaiseStatus(_settings.Paused ? "Paused" : "Stopped");
        }

        public void RunOnce()
        {
            SafeScan();
        }

        void RaiseStatus(string s)
        {
            var h = StatusChanged;
            if (h != null) h(_settings.Name, s);
        }

        void SafeScan()
        {
            try { Scan(); }
            catch (Exception ex) { Logger.Log(_settings.Name, "scan error: " + ex); }
        }

        void Scan()
        {
            string src = _settings.WatchFolder;
            string dst = _settings.DestinationFolder;
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) return;

            if (!Directory.Exists(src))
            {
                Logger.Log(_settings.Name, "watch folder missing: " + src);
                return;
            }
            if (!Directory.Exists(dst))
            {
                try { Directory.CreateDirectory(dst); }
                catch (Exception ex) { Logger.Log(_settings.Name, "create dest failed: " + ex.Message); return; }
            }

            var searchOpt = _settings.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] files;
            try { files = Directory.GetFiles(src, "*", searchOpt); }
            catch (Exception ex) { Logger.Log(_settings.Name, "enumerate failed: " + ex.Message); return; }

            if (files.Length == 0)
            {
                _state.Clear();
                return;
            }

            var busy = new List<string>();
            if (_settings.WaitForFinish)
            {
                foreach (var f in files)
                {
                    if (IsBusy(f)) busy.Add(f);
                }
            }

            if (_settings.WaitForFinish && busy.Count == files.Length)
            {
                Logger.Log(_settings.Name, "all " + files.Length + " file(s) busy; skipping cycle");
                return;
            }

            RunRobocopy(src, dst, busy);
            PruneState(src);
        }

        bool IsBusy(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                }
            }
            catch (IOException)
            {
                Touch(path, -1);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Touch(path, -1);
                return true;
            }
            catch
            {
                return true;
            }

            long size;
            try { size = new FileInfo(path).Length; }
            catch { return true; }

            FileState st;
            if (!_state.TryGetValue(path, out st) || st.Size != size)
            {
                Touch(path, size);
                return true;
            }

            double stableFor = (DateTime.UtcNow - st.LastChangeUtc).TotalSeconds;
            return stableFor < _settings.StableSeconds;
        }

        void Touch(string path, long size)
        {
            _state[path] = new FileState { Size = size, LastChangeUtc = DateTime.UtcNow };
        }

        void PruneState(string src)
        {
            try
            {
                var searchOpt = _settings.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var existing = new HashSet<string>(Directory.GetFiles(src, "*", searchOpt), StringComparer.OrdinalIgnoreCase);
                var stale = _state.Keys.Where(k => !existing.Contains(k)).ToList();
                foreach (var k in stale) _state.Remove(k);
            }
            catch { }
        }

        void RunRobocopy(string src, string dst, List<string> exclude)
        {
            var args = new StringBuilder();
            args.Append("\"").Append(src.TrimEnd('\\')).Append("\" ");
            args.Append("\"").Append(dst.TrimEnd('\\')).Append("\" ");
            if (_settings.Recursive) args.Append("/E ");
            // Note: we deliberately do NOT pass /MOV. Robocopy's /MOV only deletes
            // source files it actually copied, which leaves "Same" files behind
            // (e.g. when a previous Copy-mode run already populated the destination).
            // We handle source deletion ourselves after a successful run — see
            // DeleteMovedSourceFiles below.
            if (!string.IsNullOrWhiteSpace(_settings.ExtraArgs))
                args.Append(_settings.ExtraArgs.Trim()).Append(' ');
            if (exclude.Count > 0)
            {
                args.Append("/XF ");
                foreach (var f in exclude)
                    args.Append("\"").Append(f).Append("\" ");
            }

            string cmdLine = args.ToString().Trim();
            Logger.Log(_settings.Name, "robocopy " + cmdLine);

            var psi = new ProcessStartInfo("robocopy.exe", cmdLine)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var output = new StringBuilder();
            int code;
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine("ERR: " + e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    code = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(_settings.Name, "robocopy launch failed: " + ex.Message);
                var h = RunCompleted;
                if (h != null) h(_settings.Name, -1, false);
                return;
            }

            // Robocopy exit codes: bit 0 = files copied, bit 1 = extras detected,
            // bits 2-3 = mismatches, bits 4-5 = errors. >=8 means at least one failure.
            bool success = code < 8;
            bool filesCopied = (code & 1) != 0;
            Logger.Log(_settings.Name, "robocopy exit=" + code + (filesCopied ? " (files copied)" : " (no copies)"));
            if (output.Length > 0) Logger.Log(_settings.Name, "robocopy output:\r\n" + output);

            int moved = 0;
            if (_settings.MoveMode && success)
                moved = DeleteMovedSourceFiles(src, dst, exclude);

            var handler = RunCompleted;
            if (handler != null && (filesCopied || moved > 0)) handler(_settings.Name, code, success);
        }

        int DeleteMovedSourceFiles(string src, string dst, List<string> excludeAbsolute)
        {
            var excludeSet = new HashSet<string>(excludeAbsolute, StringComparer.OrdinalIgnoreCase);
            var searchOpt = _settings.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] files;
            try { files = Directory.GetFiles(src, "*", searchOpt); }
            catch (Exception ex) { Logger.Log(_settings.Name, "move cleanup: enumerate failed: " + ex.Message); return 0; }

            int deleted = 0;
            int mismatched = 0;
            foreach (var f in files)
            {
                if (excludeSet.Contains(f)) continue;
                string rel = MakeRelative(src, f);
                if (rel == null) continue;
                string destPath = Path.Combine(dst, rel);
                if (!File.Exists(destPath)) continue;

                long srcLen, dstLen;
                try { srcLen = new FileInfo(f).Length; dstLen = new FileInfo(destPath).Length; }
                catch { continue; }
                if (srcLen != dstLen) { mismatched++; continue; }

                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    _state.Remove(f);
                    deleted++;
                }
                catch (Exception ex)
                {
                    Logger.Log(_settings.Name, "move cleanup: delete failed (" + f + "): " + ex.Message);
                }
            }

            if (_settings.Recursive && deleted > 0)
                PruneEmptySubdirs(src);

            if (deleted > 0 || mismatched > 0)
            {
                Logger.Log(_settings.Name,
                    "move cleanup: deleted " + deleted + " source file(s)" +
                    (mismatched > 0 ? ", " + mismatched + " size-mismatch (kept)" : ""));
            }
            return deleted;
        }

        static string MakeRelative(string baseDir, string fullPath)
        {
            string b;
            string f;
            try
            {
                b = Path.GetFullPath(baseDir).TrimEnd('\\') + "\\";
                f = Path.GetFullPath(fullPath);
            }
            catch { return null; }
            if (f.StartsWith(b, StringComparison.OrdinalIgnoreCase))
                return f.Substring(b.Length);
            return null;
        }

        void PruneEmptySubdirs(string root)
        {
            try
            {
                var dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
                Array.Sort(dirs, (a, b) => b.Length.CompareTo(a.Length)); // deepest first
                foreach (var d in dirs)
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(d).Length == 0)
                            Directory.Delete(d);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Logger.Log(_settings.Name, "prune empty subdirs failed: " + ex.Message); }
        }
    }
}
