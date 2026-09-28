# Settings window

Step 4 extends the product-facing Doky Settings window with safe dock
appearance controls. Startup, taskbar-mode, and multi-monitor settings remain
inactive.

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
| GlassMaterialMode | style ComboBox | Frosted | Frosted, Acrylic, Clear |
| IconSize | DIP NumberBox | 28 | 20–40 DIP |
| MagnificationScale | scale NumberBox | 1.24 | 1.0–1.6 |
| IconSpacing | DIP NumberBox | 6 | 0–20 DIP |
| GlassBlurAmount | amount NumberBox | 20 | 0–60 |
| DockOpacity | percentage NumberBox | 78% | 35–100% |
| BorderThickness | DIP NumberBox | 1.05 | 0–2 DIP |
| BorderOpacity | percentage NumberBox | 78% | 0–100% |

Reset to Defaults updates all editable controls. Apply is required to save or
activate the reset.

## Apply flow

SettingsWindow creates a candidate from the current session, replacing only the
editable dock behavior and appearance values. This preserves LaunchAtStartup,
SuppressWindowsTaskbar, and future unexposed values. It normalizes the candidate,
saves it atomically through GlassDockSettingsStore, then replaces the session
snapshot. A failed save leaves runtime settings unchanged and displays an
inline error.

DesktopOverlayWindow subscribes to the session. BottomMargin is repositioned
immediately after a successful Apply. New collapse schedules read
AutoHideDelayMilliseconds; new pill-lowering schedules read
PeekDelayMilliseconds. IconSize controls the icon and button dimensions;
IconSpacing recalculates dock width; MagnificationScale controls the existing
smooth magnification field; GlassBlurAmount and DockOpacity update the existing
expanded-dock material graph. BorderThickness and BorderOpacity update the
existing expanded dock wave rim. GlassMaterialMode selects the base material
values used by that same graph, then the exposed blur, opacity, and border
controls fine-tune it. Already-running delay operations keep the snapshot they
started with.

Selecting a material updates only the pending blur, opacity, and border controls;
the running dock changes only after Apply. The preset values are:

| Mode | Blur | Opacity | Saturation | Brightness | Tint | Border thickness | Border opacity |
|---|---:|---:|---:|---:|---|---:|---:|
| Frosted | 20 | 0.78 | 1.15 | 1.08 | #DCEAFF | 1.05 DIP | 0.78 |
| Acrylic | 10 | 0.58 | 1.22 | 1.06 | #D8E9FF | 0.8 DIP | 0.48 |
| Clear | 4 | 0.42 | 1.06 | 1.03 | #E8F3FF | 0.5 DIP | 0.25 |

## Persistence and scope

Settings are stored as indented JSON at:

    %LOCALAPPDATA%\GlassDock\settings.json

Missing or corrupt JSON still falls back to defaults as described in
SETTINGS_FOUNDATION.md. LaunchAtStartup and SuppressWindowsTaskbar remain
persisted preferences only. No registry, Startup folder, scheduled task,
taskbar mode switch, or watchdog redesign is implemented here.

## Validation

- Locked restore: passed.
- Release solution build: passed with 0 warnings and 0 errors.
- Core tests: 99 passed.
- Windows tests: 44 passed.
- Total: 143 passed, 0 failed, 0 skipped.
- scripts/Validate.ps1 and git diff --check: passed.

The WinUI XAML compiled as part of the full solution build. Visual layout,
NumberBox interaction, menu activation, and live pointer timing still require a
manual pass in the rebuilt desktop app.
