# RoboCopyGUI

A small native Windows tray app that polls a watch folder every N seconds and runs `robocopy.exe` to a destination folder. Supports multiple profiles, "wait for files to finish writing" detection, and proper move semantics (it deletes source files robocopy would otherwise leave behind).

No installer, no .NET SDK to set up — it compiles against the .NET Framework `csc.exe` already on every Windows 10/11 box and ships as a single ~80 KB `.exe`.

![Icon](RoboCopyGUI.ico)

## Features

- **System tray app**, no console window. Right-click for the menu, double-click to open Settings.
- **Multiple profiles** — each profile has its own watch/destination folders, poll interval, options, and pause state. Manage profiles from the Settings window with New / Rename / Duplicate / Delete; the tray menu surfaces each profile with Pause/Resume and Run-Now.
- **Wait-for-file-finish detection** combines a file-lock probe and a size-stable check — only files that pass both are handed to robocopy. Busy files are excluded for the current cycle via `/XF` and retried on the next tick.
- **Copy or Move (per profile)** — Move handles source-deletion in C# rather than relying on robocopy's `/MOV`, so files already at the destination (e.g. from a prior Copy-mode run) still get cleaned up.
- **Start with Windows** (writes HKCU `Run` key with `--minimized`) and **Start minimized to tray**.
- **Tray balloon** notifications on completion, with a Silent mode to suppress them.
- **Per-profile log** at `%AppData%\RoboCopyGUI\log.txt` with auto-rotation at 2 MB.

## Requirements

- Windows 10 or 11 (any edition)
- .NET Framework 4.x (preinstalled on every supported Windows)
- PowerShell 5.1+ (preinstalled) — used only by the icon generator at build time

## Build

```cmd
build.cmd
```

`build.cmd` regenerates `RoboCopyGUI.ico` if missing (via `tools\make-icon.ps1`), then compiles all `src\*.cs` with the framework `csc.exe` into `RoboCopyGUI.exe`. Typical build time is under a second.

## Run

Double-click `RoboCopyGUI.exe`. On first launch the Settings window opens because no profiles are configured. Pick a watch folder, a destination folder, set your options, and click **Save & Close**. The app keeps running in the tray.

Closing the Settings window hides it; **Exit** from the tray menu fully quits.

The exe is single-instance (a named mutex prevents two copies). The `--minimized` flag skips showing the Settings window on launch (used by the auto-start registry entry).

## Settings window

**Profile bar** — dropdown of profiles, plus New / Rename / Duplicate / Delete. Switching profiles with unsaved changes prompts to save/discard.

**Per-profile fields**

| Field | Default | Notes |
|---|---|---|
| Watch folder | — | Where new files arrive |
| Destination folder | — | Where files are synced to |
| Poll interval (s) | 60 | How often to scan |
| Stable threshold (s) | 10 | A file's size must be unchanged for this long before it's considered "done writing" |
| Wait for files to finish writing | on | Lock probe + size-stable. Off = just run robocopy each cycle and let it handle locked files. |
| Include subfolders | on | Adds `/E` to robocopy |
| Move instead of copy | off | Deletes source files after a successful sync (see Move semantics below) |
| Show tray balloon when a sync completes | on | |
| Silent mode | off | Suppresses all balloons regardless of the above |
| Extra robocopy args | `/R:1 /W:1 /NP` | Appended to every invocation |

**App-wide (Startup)**

- Start with Windows
- Start minimized to tray

## Tray menu

```
Show Settings…
─────────────────
▶ ProfileA     →  Pause | Run Now
⏸ ProfileB     →  Resume | Run Now
─────────────────
Run All Now
─────────────────
Open Log
Open Settings Folder
─────────────────
Exit
```

The leading symbol shows status: `▶` running, `⏸` paused. The icon tooltip summarizes counts (`RoboCopyGUI — 2 running, 1 paused`).

## Copy vs Move semantics

**Copy mode** — robocopy invoked with `/E` (if Recursive) plus your Extra args. Standard incremental copy.

**Move mode** — robocopy is *not* passed `/MOV`. Robocopy's `/MOV` only deletes source files it actually copies, so files already present at the destination (size + timestamp match) get "Same"-skipped and the source is left behind. Instead, after a successful robocopy run, RoboCopyGUI walks the source folder and deletes every file whose destination counterpart exists with the same size, then prunes any empty subdirectories. Mismatched sizes are left in place and logged as `move cleanup: ... N size-mismatch (kept)`.

Files classified as "busy" (lock-held or size still changing) are excluded from both the robocopy run and the move cleanup for that cycle.

## File layout

```
RoboCopyGUI/
├── build.cmd                 ← one-shot compile via framework csc.exe
├── RoboCopyGUI.ico           ← generated; committed for convenience
├── src/
│   ├── Program.cs            ← entry point, single-instance mutex
│   ├── TrayApp.cs            ← NotifyIcon, menu, watcher orchestration
│   ├── SettingsForm.cs       ← Settings window, profile picker
│   ├── Watcher.cs            ← timer, lock+size-stable, robocopy runner, move cleanup
│   ├── ProfileSettings.cs    ← per-profile model + INI load/save
│   ├── AppSettings.cs        ← app-wide model + INI load/save
│   ├── ProfileStore.cs       ← list / load / save / delete / rename / legacy migration
│   ├── StartupRegistry.cs    ← HKCU Run key helper
│   ├── Logger.cs             ← rolling log file
│   └── AppIcon.cs            ← loads the embedded icon resource
└── tools/
    ├── icon-draw.ps1         ← shared drawing routine (GDI+)
    ├── make-icon.ps1         ← writes RoboCopyGUI.ico (multi-size, PNG-in-ICO)
    └── preview-icon.ps1      ← renders icon-preview-*.png for review
```

## Settings storage

```
%AppData%\RoboCopyGUI\
├── app.ini                   ← StartMinimized, StartWithWindows, LastProfile
├── profiles\
│   ├── Default.ini           ← one file per profile, INI format
│   └── ...
└── log.txt                   ← rolling log (rotates to log.txt.1 at 2 MB)
```

Old `settings.ini` files from before the multi-profile change are migrated automatically to `profiles\Default.ini` on first launch and renamed `settings.ini.bak`.

## Icon

The icon (teal robot holding a paper document) is generated by `tools\make-icon.ps1` using System.Drawing — no external assets. To tweak colors or shapes, edit `tools\icon-draw.ps1`, delete `RoboCopyGUI.ico`, and rerun `build.cmd`. Run `tools\preview-icon.ps1` for PNG previews at 16/32/64/256 px.

## License

MIT
