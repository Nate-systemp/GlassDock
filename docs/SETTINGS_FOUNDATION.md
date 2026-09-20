# Settings foundation

Step 2 added the persistence infrastructure. Step 3 wires only BottomMargin,
AutoHideDelayMilliseconds, and PeekDelayMilliseconds through the product
Settings window. Taskbar/startup preferences remain stored but inactive.

## Model and defaults

GlassDock.Core.Settings.GlassDockSettings is platform-neutral and contains:

| Setting | Default | Current-source basis |
|---|---:|---|
| SchemaVersion | 1 | First persisted schema |
| LaunchAtStartup | false | Startup registration is not implemented |
| SuppressWindowsTaskbar | true | Current app launch requests taskbar suppression |
| BottomMargin | 24 | Current expanded dock margin |
| AutoHideDelayMilliseconds | 1000 | Current expanded-collapse grace |
| PeekDelayMilliseconds | 2000 | Current raised-pill hold |

The model contains normalization constants and a Normalize method so future
schema migration can be added without placing Windows persistence in Core.
Unknown, missing, zero, or future schema values currently normalize to schema 1.

Numeric normalization ranges are:

- BottomMargin: 16–100 DIP. Non-finite values use 24.
- AutoHideDelayMilliseconds: 0–10,000 ms.
- PeekDelayMilliseconds: 0–30,000 ms.

## Windows persistence

GlassDock.Windows.Settings.GlassDockSettingsStore saves human-readable JSON to:

    %LOCALAPPDATA%\GlassDock\settings.json

The default constructor resolves that Windows path. Tests inject a file path
under the process temporary directory and never use the real LocalAppData file.

LoadAsync returns defaults when the file is missing, unreadable, unsupported,
or invalid JSON. Loading a corrupt file does not overwrite or delete it; a later
successful save replaces it. Unknown JSON properties are ignored. Every loaded
snapshot is normalized before return.

SaveAsync normalizes the snapshot, creates the parent directory, writes
indented JSON to a uniquely named temporary file in the same directory, flushes
it, then atomically requests an overwrite move to settings.json. Failure or
cancellation removes the temporary file when possible. A per-store semaphore
serializes saves so concurrent calls cannot interleave file writes.

## Tests

Core tests verify defaults and normalization. Windows tests verify missing-file
fallback, directory creation, save/load round trips, readable JSON,
SchemaVersion output, corrupt JSON preservation, loaded-value normalization,
unknown-property tolerance, repeated replacement saves, and temporary-file
cleanup.

See SETTINGS_UI.md for the safe runtime settings now connected. The foundation
still does not mutate registry/Startup locations or wire SuppressWindowsTaskbar.
