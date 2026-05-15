using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RoboCopyGUI
{
    public class Watcher
    {
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        ProfileSettings _settings;
        readonly Dictionary<string, FileState> _state =
            new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        bool _running;
        int _scanInFlight; // 0 = idle, 1 = a scan is already running on the threadpool
        readonly SynchronizationContext _uiContext;
        int _lastSourceFileCount = -1; // -1 = no prior scan recorded; used to delay large purges by one cycle
        const double PurgeShrinkThreshold = 0.5; // refuse to purge if current source < 50% of last seen

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
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _timer.Tick += (a, b) => TriggerScan();
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
            TriggerScan();
        }

        // Kick a scan onto the threadpool. Returns immediately. If a previous
        // scan is still running (slow network share, big folder), skip this
        // tick rather than queue up overlapping work.
        void TriggerScan()
        {
            if (Interlocked.CompareExchange(ref _scanInFlight, 1, 0) != 0)
            {
                Logger.Log(_settings.Name, "tick skipped: previous scan still in flight");
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { SafeScan(); }
                finally { Interlocked.Exchange(ref _scanInFlight, 0); }
            });
        }

        void RaiseStatus(string s)
        {
            var h = StatusChanged;
            if (h == null) return;
            string name = _settings.Name;
            _uiContext.Post(_unused => h(name, s), null);
        }

        void RaiseRunCompleted(int code, bool success)
        {
            var h = RunCompleted;
            if (h == null) return;
            string name = _settings.Name;
            _uiContext.Post(_unused => h(name, code, success), null);
        }

        void SafeScan()
        {
            try { Scan(); }
            catch (Exception ex) { Logger.Log(_settings.Name, "scan error: " + ex); }
        }

        void Scan()
        {
            // Snapshot the settings once so a concurrent UpdateSettings call
            // (UI thread) can't tear our view of the profile mid-scan.
            var s = _settings;

            string src = s.WatchFolder;
            string dst = s.DestinationFolder;
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) return;

            if (!Directory.Exists(src))
            {
                Logger.Log(s.Name, "watch folder missing: " + src);
                return;
            }
            if (!Directory.Exists(dst))
            {
                try { Directory.CreateDirectory(dst); }
                catch (Exception ex) { Logger.Log(s.Name, "create dest failed: " + ex.Message); return; }
            }

            var searchOpt = s.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] files;
            try { files = Directory.GetFiles(src, "*", searchOpt); }
            catch (Exception ex) { Logger.Log(s.Name, "enumerate failed: " + ex.Message); return; }

            if (files.Length == 0)
            {
                _state.Clear();
                return;
            }

            // Consume the one-shot RunOnExisting flag at the top of the scan so it
            // can never linger past this cycle (even if we early-return below).
            bool runOnExisting = s.RunOnExisting;
            if (runOnExisting)
            {
                s.RunOnExisting = false;
                Logger.Log(s.Name, "run-on-existing: handing " + files.Length + " file(s) to robocopy (skipping per-file lock probe)");
            }

            // When run-on-existing is set, the user is asserting "these files are
            // ready" — skip the per-file lock probe entirely. Robocopy itself
            // handles in-use files via /R retries. This is the difference
            // between a 1-second scan and a multi-minute UI freeze when the
            // watch folder is a slow network share with thousands of files.
            var busy = new List<string>();
            if (s.WaitForFinish && !runOnExisting)
            {
                foreach (var f in files)
                {
                    if (IsBusy(s, f)) busy.Add(f);
                }
            }

            if (s.WaitForFinish && !runOnExisting && busy.Count == files.Length)
            {
                Logger.Log(s.Name, "all " + files.Length + " file(s) busy; skipping cycle");
                return;
            }

            // If we skipped probing, seed the size-stable state so the next
            // regular cycle has a baseline for each file.
            if (runOnExisting)
            {
                foreach (var f in files)
                {
                    try { _state[f] = new FileState { Size = new FileInfo(f).Length, LastChangeUtc = DateTime.UtcNow }; }
                    catch { }
                }
            }

            RunRobocopy(s, src, dst, busy);

            // Mirror-style deletion is mutually exclusive with Move mode (which
            // already empties the source — mirroring after a move would wipe
            // the destination). UI prevents the combination; this is a belt.
            if (s.SyncDeletions && !s.MoveMode)
                SyncDestinationDeletions(s, src, dst, busy, files.Length);

            // Record the current source file count for the next cycle's safety
            // check. Done even if we didn't purge — a one-cycle "block then
            // permit" gives the user 60s to notice an unexpected drop.
            _lastSourceFileCount = files.Length;

            PruneState(s, src);
        }

        bool IsBusy(ProfileSettings s, string path)
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
            return stableFor < s.StableSeconds;
        }

        void Touch(string path, long size)
        {
            _state[path] = new FileState { Size = size, LastChangeUtc = DateTime.UtcNow };
        }

        void PruneState(ProfileSettings s, string src)
        {
            try
            {
                var searchOpt = s.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var existing = new HashSet<string>(Directory.GetFiles(src, "*", searchOpt), StringComparer.OrdinalIgnoreCase);
                var stale = _state.Keys.Where(k => !existing.Contains(k)).ToList();
                foreach (var k in stale) _state.Remove(k);
            }
            catch { }
        }

        void RunRobocopy(ProfileSettings s, string src, string dst, List<string> exclude)
        {
            var args = new StringBuilder();
            args.Append("\"").Append(src.TrimEnd('\\')).Append("\" ");
            args.Append("\"").Append(dst.TrimEnd('\\')).Append("\" ");
            if (s.Recursive) args.Append("/E ");
            // Note: we deliberately do NOT pass /MOV. Robocopy's /MOV only deletes
            // source files it actually copied, which leaves "Same" files behind
            // (e.g. when a previous Copy-mode run already populated the destination).
            // We handle source deletion ourselves after a successful run — see
            // DeleteMovedSourceFiles below.
            if (!string.IsNullOrWhiteSpace(s.ExtraArgs))
                args.Append(s.ExtraArgs.Trim()).Append(' ');
            if (exclude.Count > 0)
            {
                args.Append("/XF ");
                foreach (var f in exclude)
                    args.Append("\"").Append(f).Append("\" ");
            }

            string cmdLine = args.ToString().Trim();
            Logger.Log(s.Name, "robocopy " + cmdLine);

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
                    p.OutputDataReceived += (sndr, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                    p.ErrorDataReceived += (sndr, e) => { if (e.Data != null) output.AppendLine("ERR: " + e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    code = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(s.Name, "robocopy launch failed: " + ex.Message);
                RaiseRunCompleted(-1, false);
                return;
            }

            // Robocopy exit codes: bit 0 = files copied, bit 1 = extras detected,
            // bits 2-3 = mismatches, bits 4-5 = errors. >=8 means at least one failure.
            bool success = code < 8;
            bool filesCopied = (code & 1) != 0;
            Logger.Log(s.Name, "robocopy exit=" + code + (filesCopied ? " (files copied)" : " (no copies)"));
            if (output.Length > 0) Logger.Log(s.Name, "robocopy output:\r\n" + output);

            int moved = 0;
            if (s.MoveMode && success)
                moved = DeleteMovedSourceFiles(s, src, dst, exclude);

            if (filesCopied || moved > 0) RaiseRunCompleted(code, success);
        }

        int DeleteMovedSourceFiles(ProfileSettings s, string src, string dst, List<string> excludeAbsolute)
        {
            var excludeSet = new HashSet<string>(excludeAbsolute, StringComparer.OrdinalIgnoreCase);
            var searchOpt = s.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] files;
            try { files = Directory.GetFiles(src, "*", searchOpt); }
            catch (Exception ex) { Logger.Log(s.Name, "move cleanup: enumerate failed: " + ex.Message); return 0; }

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
                    Logger.Log(s.Name, "move cleanup: delete failed (" + f + "): " + ex.Message);
                }
            }

            if (s.Recursive && deleted > 0)
                PruneEmptySubdirs(s, src);

            if (deleted > 0 || mismatched > 0)
            {
                Logger.Log(s.Name,
                    "move cleanup: deleted " + deleted + " source file(s)" +
                    (mismatched > 0 ? ", " + mismatched + " size-mismatch (kept)" : ""));
            }
            return deleted;
        }

        void SyncDestinationDeletions(ProfileSettings s, string src, string dst, List<string> excludeAbsolute, int sourceFileCount)
        {
            // Hard safety: if the source is empty, refuse. This is the canonical
            // "drive unmounted" symptom and the failure mode that would wipe a
            // destination on the next poll.
            if (sourceFileCount == 0)
            {
                Logger.Log(s.Name, "sync-deletions: source has 0 files; refusing to purge destination (likely misconfig or unmounted share)");
                return;
            }

            // Soft safety: if the source count just dropped >50% vs last seen,
            // skip this cycle. The caller still updates _lastSourceFileCount, so
            // if the drop is sustained the next cycle's ratio becomes ~1.0 and
            // the purge proceeds. Net effect: a one-cycle delay (~PollSeconds)
            // between "user nukes a chunk of source" and "destination follows".
            if (_lastSourceFileCount > 0)
            {
                double ratio = (double)sourceFileCount / _lastSourceFileCount;
                if (ratio < PurgeShrinkThreshold)
                {
                    Logger.Log(s.Name,
                        "sync-deletions: source dropped from " + _lastSourceFileCount + " to " + sourceFileCount +
                        " files (more than 50%); delaying purge by one cycle as a safety check");
                    return;
                }
            }

            var searchOpt = s.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] srcFiles;
            string[] dstFiles;
            try { srcFiles = Directory.GetFiles(src, "*", searchOpt); }
            catch (Exception ex) { Logger.Log(s.Name, "sync-deletions: src enumerate failed: " + ex.Message); return; }
            try { dstFiles = Directory.GetFiles(dst, "*", searchOpt); }
            catch (Exception ex) { Logger.Log(s.Name, "sync-deletions: dst enumerate failed: " + ex.Message); return; }

            // Build the set of expected destination paths from current source
            // contents (busy files included — they belong at the destination
            // even though robocopy excluded them this cycle).
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sf in srcFiles)
            {
                string rel = MakeRelative(src, sf);
                if (rel == null) continue;
                expected.Add(Path.Combine(dst, rel));
            }

            int recycled = 0;
            int failed = 0;
            foreach (var df in dstFiles)
            {
                if (expected.Contains(df)) continue;
                try
                {
                    File.SetAttributes(df, FileAttributes.Normal);
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        df,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                        Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                    recycled++;
                    Logger.Log(s.Name, "sync-deletions: recycled " + df);
                }
                catch (Exception ex)
                {
                    failed++;
                    Logger.Log(s.Name, "sync-deletions: recycle failed (" + df + "): " + ex.Message);
                }
            }

            if (s.Recursive && recycled > 0)
                PruneEmptySubdirs(s, dst);

            if (recycled > 0 || failed > 0)
            {
                Logger.Log(s.Name,
                    "sync-deletions: recycled " + recycled + " orphan(s) from destination" +
                    (failed > 0 ? ", " + failed + " failure(s)" : ""));
            }
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

        void PruneEmptySubdirs(ProfileSettings s, string root)
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
            catch (Exception ex) { Logger.Log(s.Name, "prune empty subdirs failed: " + ex.Message); }
        }
    }
}
