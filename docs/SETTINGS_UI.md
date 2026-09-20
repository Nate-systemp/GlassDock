# Settings window

Step 3 adds the first product-facing GlassDock Settings window. It exposes only
safe dock behavior and does not implement startup, taskbar-mode, appearance, or
multi-monitor settings.

## Architecture

App asynchronously loads GlassDockSettingsStore once during normal launch and
constructs one GlassDockSettingsSession. DesktopOverlayWindow and SettingsWindow
share that normalized in-memory session. Interaction code never rereads JSON.

DesktopOverlayWindow retains one SettingsWindow while it is open. Repeated Dock
Settings commands activate the existing instance. Closing it releases that
instance; a later command creates a fresh window backed by the same runtime
session. DevelopmentWindow remains separate and is available through the
explicit --controls launch path.

## Exposed settings

| Setting | UI | Default | Normalized range |
|---|---|---:|---:|
| BottomMargin | DIP NumberBox | 24 | 16–100 DIP |
| AutoHideDelayMilliseconds | seconds NumberBox | 1.0 s | 0–10 s |
| PeekDelayMilliseconds | seconds NumberBox | 2.0 s | 0–30 s |

Reset to Defaults updates only these three controls. Apply is required to save
or activate the reset.

## Apply flow

SettingsWindow creates a candidate from the current session, replacing only the
three editable values. This preserves LaunchAtStartup,
SuppressWindowsTaskbar, and future unexposed values. It normalizes the candidate,
saves it atomically through GlassDockSettingsStore, then replaces the session
snapshot. A failed save leaves runtime settings unchanged and displays an
inline error.

DesktopOverlayWindow subscribes to the session. BottomMargin is repositioned
immediately after a successful Apply. New collapse schedules read
AutoHideDelayMilliseconds; new pill-lowering schedules read
PeekDelayMilliseconds. Already-running delay operations keep the snapshot they
started with.

## Persistence and scope

Settings are stored as indented JSON at:

    %LOCALAPPDATA%\GlassDock\settings.json

Missing or corrupt JSON still falls back to defaults as described in
SETTINGS_FOUNDATION.md. LaunchAtStartup and SuppressWindowsTaskbar remain
persisted preferences only. No registry, Startup folder, scheduled task,
taskbar mode switch, watchdog redesign, appearance control, or Step 4 feature is
implemented here.

## Validation

- Locked restore: passed.
- Release solution build: passed with 0 warnings and 0 errors.
- Core tests: 94 passed.
- Windows tests: 40 passed.
- Total: 134 passed, 0 failed, 0 skipped.
- scripts/Validate.ps1 and git diff --check: passed.

The WinUI XAML compiled as part of the full solution build. Visual layout,
NumberBox interaction, menu activation, and live pointer timing still require a
manual pass in the rebuilt desktop app.
