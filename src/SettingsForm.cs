using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RoboCopyGUI
{
    public class SettingsForm : Form
    {
        AppSettings _app;
        ProfileSettings _current;
        bool _loading;
        bool _dirty;

        // Profile bar
        ComboBox _cboProfile;
        Button _btnNew, _btnRename, _btnDuplicate, _btnDelete;

        // Per-profile fields
        TextBox _txtWatch, _txtDest, _txtExtra;
        NumericUpDown _numPoll, _numStable;
        CheckBox _chkWait, _chkMove, _chkRecursive, _chkAlert, _chkSilent;

        // App-wide
        CheckBox _chkStartMin, _chkStartup;

        // Footer
        Button _btnSave, _btnApply, _btnClose, _btnRunNow, _btnPause, _btnOpenLog;
        Label _lblStatus;

        public event Action<AppSettings> AppSettingsSaved;
        public event Action<ProfileSettings> ProfileSaved;
        public event Action ProfilesListChanged;
        public event Action<string> RunNowRequested;        // profile name
        public event Action<string> PauseToggleRequested;   // profile name

        public SettingsForm(AppSettings app)
        {
            _app = app;
            BuildUi();
            ReloadProfileList(preferred: _app.LastProfile);
        }

        public void SetStatusFor(string profile, string status, bool paused)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, string, bool>(SetStatusFor), profile, status, paused); return; }
            if (_current == null || !string.Equals(_current.Name, profile, StringComparison.OrdinalIgnoreCase)) return;
            _lblStatus.Text = "Status: " + status;
            _btnPause.Text = paused ? "Resume" : "Pause";
        }

        public void RefreshProfilesList()
        {
            if (InvokeRequired) { BeginInvoke(new Action(RefreshProfilesList)); return; }
            ReloadProfileList(preferred: _current != null ? _current.Name : _app.LastProfile);
        }

        void BuildUi()
        {
            Text = "RoboCopyGUI";
            Icon = AppIcon.Load();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 600);
            ShowInTaskbar = true;

            const int leftPad = 12;
            const int rightPad = 12;
            int y = 12;

            // --- Profile bar ---
            var profileBox = new GroupBox { Text = "Profile", Left = leftPad, Top = y, Width = ClientSize.Width - leftPad - rightPad, Height = 60 };
            Controls.Add(profileBox);

            _cboProfile = new ComboBox
            {
                Left = 12,
                Top = 24,
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _cboProfile.SelectedIndexChanged += (s, e) =>
            {
                if (_loading) return;
                if (!ConfirmDiscardIfDirty()) { RevertProfileSelection(); return; }
                string name = _cboProfile.SelectedItem as string;
                if (name != null) LoadProfile(name);
            };
            profileBox.Controls.Add(_cboProfile);

            _btnNew = MakeBtn("New", 220, 22, 70, OnNew);
            _btnRename = MakeBtn("Rename", 296, 22, 70, OnRename);
            _btnDuplicate = MakeBtn("Duplicate", 372, 22, 80, OnDuplicate);
            _btnDelete = MakeBtn("Delete", 458, 22, 70, OnDelete);
            profileBox.Controls.Add(_btnNew);
            profileBox.Controls.Add(_btnRename);
            profileBox.Controls.Add(_btnDuplicate);
            profileBox.Controls.Add(_btnDelete);

            y += profileBox.Height + 8;

            // --- Per-profile folder fields ---
            const int labelW = 130;
            const int rowH = 28;
            int fieldX = leftPad + labelW;
            int fieldRight = ClientSize.Width - rightPad;
            const int browseW = 32;

            AddLabel("Watch folder:", leftPad, y + 4, labelW);
            _txtWatch = AddText(fieldX, y, fieldRight - fieldX - browseW - 4);
            Controls.Add(MakeBtn("…", fieldRight - browseW, y - 1, browseW, (s, e) => BrowseInto(_txtWatch)));
            y += rowH;

            AddLabel("Destination folder:", leftPad, y + 4, labelW);
            _txtDest = AddText(fieldX, y, fieldRight - fieldX - browseW - 4);
            Controls.Add(MakeBtn("…", fieldRight - browseW, y - 1, browseW, (s, e) => BrowseInto(_txtDest)));
            y += rowH;

            AddLabel("Poll interval (s):", leftPad, y + 4, labelW);
            _numPoll = AddNumeric(fieldX, y, 5, 86400, 60);
            AddLabel("Stable threshold (s):", fieldX + 100, y + 4, 130);
            _numStable = AddNumeric(fieldX + 100 + 130, y, 1, 3600, 10);
            y += rowH + 4;

            // --- Behavior group ---
            var behavior = new GroupBox { Text = "Behavior (this profile)", Left = leftPad, Top = y, Width = ClientSize.Width - leftPad - rightPad, Height = 150 };
            Controls.Add(behavior);
            int gy = 22;
            _chkWait = AddCheck(behavior, "Wait for files to finish writing (lock probe + size-stable)", 12, gy); gy += 24;
            _chkRecursive = AddCheck(behavior, "Include subfolders (robocopy /E)", 12, gy); gy += 24;
            _chkMove = AddCheck(behavior, "Move instead of copy (robocopy /MOV — deletes source after success)", 12, gy); gy += 24;
            _chkAlert = AddCheck(behavior, "Show tray balloon when a sync completes", 12, gy); gy += 24;
            _chkSilent = AddCheck(behavior, "Silent mode (suppress all balloon notifications)", 12, gy);
            y += behavior.Height + 8;

            // --- Startup group (app-wide) ---
            var startup = new GroupBox { Text = "Startup (app-wide)", Left = leftPad, Top = y, Width = ClientSize.Width - leftPad - rightPad, Height = 80 };
            Controls.Add(startup);
            gy = 22;
            _chkStartup = AddCheck(startup, "Start with Windows (HKCU Run key)", 12, gy); gy += 24;
            _chkStartMin = AddCheck(startup, "Start minimized to tray (don't show this window on launch)", 12, gy);
            _chkStartup.Checked = _app.StartWithWindows;
            _chkStartMin.Checked = _app.StartMinimized;
            _chkStartup.CheckedChanged += (s, e) => MarkDirty();
            _chkStartMin.CheckedChanged += (s, e) => MarkDirty();
            y += startup.Height + 8;

            AddLabel("Extra robocopy args:", leftPad, y + 4, labelW + 30);
            _txtExtra = AddText(fieldX + 30, y, fieldRight - (fieldX + 30));
            y += rowH + 6;

            _lblStatus = new Label
            {
                Left = leftPad,
                Top = y + 6,
                Width = 200,
                Text = "Status: —",
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(_lblStatus);

            _btnRunNow = MakeBtn("Run Now", leftPad + 210, y + 2, 80, (s, e) =>
            {
                var h = RunNowRequested;
                if (h != null && _current != null) h(_current.Name);
            });
            _btnPause = MakeBtn("Pause", leftPad + 294, y + 2, 80, (s, e) =>
            {
                var h = PauseToggleRequested;
                if (h != null && _current != null) h(_current.Name);
            });
            _btnOpenLog = MakeBtn("Open Log", leftPad + 378, y + 2, 90, OnOpenLog);
            Controls.Add(_btnRunNow);
            Controls.Add(_btnPause);
            Controls.Add(_btnOpenLog);
            y += rowH + 6;

            _btnSave = MakeBtn("Save && Close", fieldRight - 110, y, 110, (s, e) => { if (Save()) Close(); });
            _btnApply = MakeBtn("Apply", fieldRight - 110 - 70, y, 65, (s, e) => Save());
            _btnClose = MakeBtn("Close", fieldRight - 110 - 70 - 70, y, 65, (s, e) => Close());
            Controls.Add(_btnSave);
            Controls.Add(_btnApply);
            Controls.Add(_btnClose);

            // Dirty tracking
            WireDirty(_txtWatch);
            WireDirty(_txtDest);
            WireDirty(_txtExtra);
            _numPoll.ValueChanged += (s, e) => MarkDirty();
            _numStable.ValueChanged += (s, e) => MarkDirty();
            _chkWait.CheckedChanged += (s, e) => MarkDirty();
            _chkRecursive.CheckedChanged += (s, e) => MarkDirty();
            _chkMove.CheckedChanged += (s, e) => MarkDirty();
            _chkAlert.CheckedChanged += (s, e) => MarkDirty();
            _chkSilent.CheckedChanged += (s, e) => MarkDirty();

            FormClosing += (s, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                if (!ConfirmDiscardIfDirty()) { e.Cancel = true; return; }
                e.Cancel = true;
                Hide();
            };
        }

        Label AddLabel(string text, int x, int y, int w)
        {
            var l = new Label { Left = x, Top = y, Width = w, Text = text };
            Controls.Add(l);
            return l;
        }
        TextBox AddText(int x, int y, int w)
        {
            var t = new TextBox { Left = x, Top = y, Width = w };
            Controls.Add(t);
            return t;
        }
        NumericUpDown AddNumeric(int x, int y, int min, int max, int value)
        {
            var n = new NumericUpDown { Left = x, Top = y, Width = 90, Minimum = min, Maximum = max, Value = value };
            Controls.Add(n);
            return n;
        }
        Button MakeBtn(string text, int x, int y, int w, EventHandler onClick)
        {
            var b = new Button { Left = x, Top = y, Width = w, Text = text };
            b.Click += onClick;
            return b;
        }
        CheckBox AddCheck(Control parent, string text, int x, int y)
        {
            var c = new CheckBox { Left = x, Top = y, Width = parent.Width - x - 16, Text = text, AutoSize = false, Height = 22 };
            parent.Controls.Add(c);
            return c;
        }
        void WireDirty(TextBox tb) { tb.TextChanged += (s, e) => MarkDirty(); }
        void MarkDirty() { if (!_loading) _dirty = true; }

        void ReloadProfileList(string preferred)
        {
            _loading = true;
            try
            {
                var names = ProfileStore.ListNames();
                _cboProfile.Items.Clear();
                foreach (var n in names) _cboProfile.Items.Add(n);

                string toSelect = null;
                if (!string.IsNullOrEmpty(preferred) && names.Contains(preferred, StringComparer.OrdinalIgnoreCase))
                    toSelect = names.Find(n => n.Equals(preferred, StringComparison.OrdinalIgnoreCase));
                else if (names.Count > 0) toSelect = names[0];

                if (toSelect != null)
                {
                    _cboProfile.SelectedItem = toSelect;
                    LoadProfile(toSelect);
                }
                else
                {
                    _current = null;
                }
            }
            finally { _loading = false; }
        }

        void RevertProfileSelection()
        {
            _loading = true;
            try
            {
                if (_current != null) _cboProfile.SelectedItem = _current.Name;
            }
            finally { _loading = false; }
        }

        void LoadProfile(string name)
        {
            _current = ProfileStore.Load(name);
            _loading = true;
            try
            {
                _txtWatch.Text = _current.WatchFolder;
                _txtDest.Text = _current.DestinationFolder;
                _numPoll.Value = Clamp(_current.PollSeconds, (int)_numPoll.Minimum, (int)_numPoll.Maximum);
                _numStable.Value = Clamp(_current.StableSeconds, (int)_numStable.Minimum, (int)_numStable.Maximum);
                _chkWait.Checked = _current.WaitForFinish;
                _chkRecursive.Checked = _current.Recursive;
                _chkMove.Checked = _current.MoveMode;
                _chkAlert.Checked = _current.AlertWhenDone;
                _chkSilent.Checked = _current.RunSilent;
                _txtExtra.Text = _current.ExtraArgs;
                _lblStatus.Text = "Status: " + (_current.Paused ? "Paused" : "Running");
                _btnPause.Text = _current.Paused ? "Resume" : "Pause";
                _dirty = false;
            }
            finally { _loading = false; }
        }

        static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        bool ConfirmDiscardIfDirty()
        {
            if (!_dirty) return true;
            var res = MessageBox.Show(this, "You have unsaved changes. Save before continuing?",
                "RoboCopyGUI", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (res == DialogResult.Cancel) return false;
            if (res == DialogResult.Yes) return Save();
            _dirty = false;
            return true;
        }

        void BrowseInto(TextBox tb)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(tb.Text) && Directory.Exists(tb.Text))
                    dlg.SelectedPath = tb.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    tb.Text = dlg.SelectedPath;
            }
        }

        void OnOpenLog(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(Logger.LogPath))
                    File.WriteAllText(Logger.LogPath, "");
                Process.Start("notepad.exe", Logger.LogPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open log: " + ex.Message, "RoboCopyGUI");
            }
        }

        void OnNew(object sender, EventArgs e)
        {
            if (!ConfirmDiscardIfDirty()) return;
            string name = PromptForName("New profile", "Profile name:", SuggestName("Profile"));
            if (name == null) return;
            if (!ProfileStore.IsValidName(name)) { MessageBox.Show(this, "Invalid profile name.", "RoboCopyGUI"); return; }
            if (ProfileStore.Exists(name)) { MessageBox.Show(this, "A profile with that name already exists.", "RoboCopyGUI"); return; }
            var p = new ProfileSettings { Name = name };
            try { ProfileStore.Save(p); }
            catch (Exception ex) { MessageBox.Show(this, "Could not create profile: " + ex.Message, "RoboCopyGUI"); return; }
            RaiseProfilesListChanged();
            ReloadProfileList(preferred: name);
        }

        void OnRename(object sender, EventArgs e)
        {
            if (_current == null) return;
            if (!ConfirmDiscardIfDirty()) return;
            string name = PromptForName("Rename profile", "New name:", _current.Name);
            if (name == null || name == _current.Name) return;
            if (!ProfileStore.IsValidName(name)) { MessageBox.Show(this, "Invalid profile name.", "RoboCopyGUI"); return; }
            if (ProfileStore.Exists(name)) { MessageBox.Show(this, "A profile with that name already exists.", "RoboCopyGUI"); return; }
            try
            {
                ProfileStore.Rename(_current.Name, name);
            }
            catch (Exception ex) { MessageBox.Show(this, "Rename failed: " + ex.Message, "RoboCopyGUI"); return; }
            RaiseProfilesListChanged();
            ReloadProfileList(preferred: name);
        }

        void OnDuplicate(object sender, EventArgs e)
        {
            if (_current == null) return;
            if (!ConfirmDiscardIfDirty()) return;
            string suggested = SuggestName(_current.Name + " copy");
            string name = PromptForName("Duplicate profile", "New profile name:", suggested);
            if (name == null) return;
            if (!ProfileStore.IsValidName(name)) { MessageBox.Show(this, "Invalid profile name.", "RoboCopyGUI"); return; }
            if (ProfileStore.Exists(name)) { MessageBox.Show(this, "A profile with that name already exists.", "RoboCopyGUI"); return; }
            var copy = _current.Clone();
            copy.Name = name;
            try { ProfileStore.Save(copy); }
            catch (Exception ex) { MessageBox.Show(this, "Could not duplicate: " + ex.Message, "RoboCopyGUI"); return; }
            RaiseProfilesListChanged();
            ReloadProfileList(preferred: name);
        }

        void OnDelete(object sender, EventArgs e)
        {
            if (_current == null) return;
            var names = ProfileStore.ListNames();
            if (names.Count <= 1)
            {
                MessageBox.Show(this, "You must keep at least one profile.", "RoboCopyGUI");
                return;
            }
            if (MessageBox.Show(this, "Delete profile '" + _current.Name + "'? This cannot be undone.",
                "RoboCopyGUI", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            ProfileStore.Delete(_current.Name);
            _dirty = false;
            RaiseProfilesListChanged();
            ReloadProfileList(preferred: null);
        }

        string SuggestName(string baseName)
        {
            if (!ProfileStore.Exists(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + " " + i;
                if (!ProfileStore.Exists(candidate)) return candidate;
            }
            return baseName + " " + Guid.NewGuid().ToString().Substring(0, 4);
        }

        void RaiseProfilesListChanged()
        {
            var h = ProfilesListChanged;
            if (h != null) h();
        }

        static string PromptForName(string title, string prompt, string defaultValue)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(320, 110);
                var lbl = new Label { Left = 12, Top = 12, Width = 296, Text = prompt };
                var tb = new TextBox { Left = 12, Top = 36, Width = 296, Text = defaultValue ?? "" };
                var ok = new Button { Left = 152, Top = 70, Width = 70, Text = "OK", DialogResult = DialogResult.OK };
                var cancel = new Button { Left = 228, Top = 70, Width = 80, Text = "Cancel", DialogResult = DialogResult.Cancel };
                f.Controls.Add(lbl); f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                tb.SelectAll();
                return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
            }
        }

        bool Save()
        {
            // App-wide first.
            _app.StartMinimized = _chkStartMin.Checked;
            _app.StartWithWindows = _chkStartup.Checked;
            if (_current != null) _app.LastProfile = _current.Name;
            try { _app.Save(); }
            catch (Exception ex) { MessageBox.Show(this, "Could not save app settings: " + ex.Message, "RoboCopyGUI"); return false; }
            try { StartupRegistry.Set(_app.StartWithWindows); }
            catch (Exception ex) { Logger.Log("startup-registry write failed: " + ex.Message); }
            var hApp = AppSettingsSaved;
            if (hApp != null) hApp(_app);

            // Per-profile.
            if (_current == null) { _dirty = false; return true; }

            string watch = _txtWatch.Text.Trim();
            string dest = _txtDest.Text.Trim();

            if (string.IsNullOrEmpty(watch) || string.IsNullOrEmpty(dest))
            {
                MessageBox.Show(this, "Both watch and destination folders are required for this profile.", "RoboCopyGUI");
                return false;
            }
            if (string.Equals(Path.GetFullPath(watch).TrimEnd('\\'), Path.GetFullPath(dest).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Watch and destination folders cannot be the same.", "RoboCopyGUI");
                return false;
            }
            if (!Directory.Exists(watch))
            {
                if (MessageBox.Show(this, "Watch folder does not exist. Save anyway?", "RoboCopyGUI",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return false;
            }

            _current.WatchFolder = watch;
            _current.DestinationFolder = dest;
            _current.PollSeconds = (int)_numPoll.Value;
            _current.StableSeconds = (int)_numStable.Value;
            _current.WaitForFinish = _chkWait.Checked;
            _current.Recursive = _chkRecursive.Checked;
            _current.MoveMode = _chkMove.Checked;
            _current.AlertWhenDone = _chkAlert.Checked;
            _current.RunSilent = _chkSilent.Checked;
            _current.ExtraArgs = _txtExtra.Text.Trim();

            try { ProfileStore.Save(_current); }
            catch (Exception ex) { MessageBox.Show(this, "Could not save profile: " + ex.Message, "RoboCopyGUI"); return false; }

            var hProf = ProfileSaved;
            if (hProf != null) hProf(_current.Clone());

            _dirty = false;
            return true;
        }
    }
}
