using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RoboCopyGUI
{
    public class TrayApp : ApplicationContext
    {
        readonly NotifyIcon _icon;
        AppSettings _app;
        readonly Dictionary<string, Watcher> _watchers =
            new Dictionary<string, Watcher>(StringComparer.OrdinalIgnoreCase);
        SettingsForm _form;

        public TrayApp(bool launchedMinimizedFlag)
        {
            ProfileStore.EnsureInitialized();
            _app = AppSettings.Load();

            try { StartupRegistry.Set(_app.StartWithWindows); }
            catch (Exception ex) { Logger.Log("startup-registry sync failed: " + ex.Message); }

            _icon = new NotifyIcon
            {
                Icon = AppIcon.Load(SystemInformation.SmallIconSize),
                Text = "RoboCopyGUI",
                Visible = true,
            };
            _icon.DoubleClick += (s, e) => ShowSettings();
            _icon.BalloonTipClicked += (s, e) => ShowSettings();

            SyncWatchersFromDisk();
            RebuildMenu();
            UpdateIconText();

            bool firstRun = AllProfilesUnconfigured();
            bool shouldShow = firstRun || (!launchedMinimizedFlag && !_app.StartMinimized);
            if (shouldShow) ShowSettings();
        }

        bool AllProfilesUnconfigured()
        {
            foreach (var w in _watchers.Values)
            {
                var p = ProfileStore.Load(w.ProfileName);
                if (!string.IsNullOrEmpty(p.WatchFolder) && !string.IsNullOrEmpty(p.DestinationFolder))
                    return false;
            }
            return true;
        }

        void SyncWatchersFromDisk()
        {
            var names = ProfileStore.ListNames();
            var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

            // Stop & remove deleted/renamed-out profiles
            foreach (var key in _watchers.Keys.Where(k => !nameSet.Contains(k)).ToList())
            {
                try { _watchers[key].Stop(); } catch { }
                _watchers.Remove(key);
            }

            // Add or update remaining
            foreach (var name in names)
            {
                var ps = ProfileStore.Load(name);
                Watcher existing;
                if (_watchers.TryGetValue(name, out existing))
                {
                    existing.UpdateSettings(ps);
                }
                else
                {
                    var w = new Watcher(ps);
                    w.RunCompleted += OnRunCompleted;
                    w.StatusChanged += OnStatusChanged;
                    _watchers[name] = w;
                    w.Start();
                }
            }
        }

        void OnStatusChanged(string profile, string status)
        {
            if (_form != null && !_form.IsDisposed)
            {
                bool paused;
                Watcher w;
                paused = _watchers.TryGetValue(profile, out w) && w.IsPaused;
                _form.SetStatusFor(profile, status, paused);
            }
            UpdateIconText();
        }

        void UpdateIconText()
        {
            int running = _watchers.Values.Count(w => w.IsRunning);
            int paused = _watchers.Values.Count(w => w.IsPaused);
            string t;
            if (_watchers.Count == 0) t = "RoboCopyGUI";
            else if (_watchers.Count == 1)
            {
                var w = _watchers.Values.First();
                t = "RoboCopyGUI — " + w.ProfileName + (w.IsPaused ? " (paused)" : " (running)");
            }
            else t = "RoboCopyGUI — " + running + " running, " + paused + " paused";
            // NotifyIcon.Text is capped at 63 chars on older shells; truncate to be safe.
            if (t.Length > 63) t = t.Substring(0, 63);
            _icon.Text = t;
        }

        void RebuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show Settings…", null, (s, e) => ShowSettings());
            menu.Items.Add(new ToolStripSeparator());

            if (_watchers.Count == 0)
            {
                menu.Items.Add("(no profiles)").Enabled = false;
            }
            else
            {
                foreach (var kv in _watchers.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var name = kv.Key;
                    var w = kv.Value;
                    var prefix = w.IsPaused ? "⏸ " : "▶ ";
                    var item = new ToolStripMenuItem(prefix + name);
                    item.DropDownItems.Add(w.IsPaused ? "Resume" : "Pause", null, (s, e) => TogglePause(name));
                    item.DropDownItems.Add("Run Now", null, (s, e) => RunNow(name));
                    menu.Items.Add(item);
                }
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Run All Now", null, (s, e) => { foreach (var w in _watchers.Values) w.RunOnce(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open Log", null, (s, e) => OpenLog());
            menu.Items.Add("Open Settings Folder", null, (s, e) => OpenSettingsFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitApp());

            var old = _icon.ContextMenuStrip;
            _icon.ContextMenuStrip = menu;
            if (old != null) old.Dispose();
        }

        void TogglePause(string profile)
        {
            Watcher w;
            if (!_watchers.TryGetValue(profile, out w)) return;
            var p = ProfileStore.Load(profile);
            p.Paused = !p.Paused;
            try { ProfileStore.Save(p); }
            catch (Exception ex) { Logger.Log(profile, "save on pause failed: " + ex.Message); }
            w.UpdateSettings(p);
            RebuildMenu();
            UpdateIconText();
            if (_form != null && !_form.IsDisposed)
                _form.SetStatusFor(profile, p.Paused ? "Paused" : "Running", p.Paused);
        }

        void RunNow(string profile)
        {
            Watcher w;
            if (_watchers.TryGetValue(profile, out w)) w.RunOnce();
        }

        void ShowSettings()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new SettingsForm(_app);
                _form.AppSettingsSaved += a =>
                {
                    _app = a;
                };
                _form.ProfileSaved += p =>
                {
                    Watcher w;
                    if (_watchers.TryGetValue(p.Name, out w))
                    {
                        w.UpdateSettings(p);
                    }
                    else
                    {
                        // New profile created via form's New flow path (already saved to disk before this fires).
                        SyncWatchersFromDisk();
                        _watchers.TryGetValue(p.Name, out w);
                    }
                    RebuildMenu();
                    UpdateIconText();
                    if (p.RunOnExisting && w != null) w.RunOnce();
                };
                _form.ProfilesListChanged += () =>
                {
                    SyncWatchersFromDisk();
                    RebuildMenu();
                    UpdateIconText();
                };
                _form.RunNowRequested += RunNow;
                _form.PauseToggleRequested += TogglePause;
            }
            _form.Show();
            if (_form.WindowState == FormWindowState.Minimized)
                _form.WindowState = FormWindowState.Normal;
            _form.Activate();
        }

        void OnRunCompleted(string profile, int exitCode, bool success)
        {
            ProfileSettings p;
            try { p = ProfileStore.Load(profile); }
            catch { return; }
            if (p.RunSilent || !p.AlertWhenDone) return;
            try
            {
                _icon.BalloonTipTitle = "RoboCopyGUI — " + profile;
                _icon.BalloonTipText = success
                    ? "Sync completed (exit " + exitCode + ")"
                    : "Sync completed with errors (exit " + exitCode + ")";
                _icon.BalloonTipIcon = success ? ToolTipIcon.Info : ToolTipIcon.Warning;
                _icon.ShowBalloonTip(3000);
            }
            catch { }
        }

        void OpenLog()
        {
            try
            {
                if (!System.IO.File.Exists(Logger.LogPath))
                    System.IO.File.WriteAllText(Logger.LogPath, "");
                Process.Start("notepad.exe", Logger.LogPath);
            }
            catch (Exception ex) { Logger.Log("open log failed: " + ex.Message); }
        }

        void OpenSettingsFolder()
        {
            try { Process.Start("explorer.exe", AppSettings.AppDataDir); }
            catch (Exception ex) { Logger.Log("open folder failed: " + ex.Message); }
        }

        void ExitApp()
        {
            foreach (var w in _watchers.Values)
            {
                try { w.Stop(); } catch { }
            }
            try { _icon.Visible = false; _icon.Dispose(); } catch { }
            ExitThread();
        }
    }
}
