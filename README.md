# GlassDock
A cleaner way to use Windows.

Seven C# projects with a floating desktop dock, a preserved Glass Material Laboratory, architecture boundaries, tests and documentation.

## Development
Windows x64, .NET SDK 10.0.401 and Windows SDK 10.0.26100.0. The desktop foundation is visually tested on Windows 11, one primary monitor at 125% scaling.
Use Visual Studio with WinUI development tools for the full IDE experience; VS Code is suitable for editing. No Visual Studio installation was detected on this machine; CLI builds are validated separately.
Run from the repository root:
```powershell
./scripts/Validate.ps1
dotnet run --project src/GlassDock.App -c Release -- --controls
dotnet run --project src/GlassDock.App -c Release -- --lab
```
Launch one App instance at a time. Default launch shows a peek pill; hover raises the pill and clicking expands the dock of running and pinned applications. Right-click for persisted Dock Settings, recovery, the laboratory, or Exit. Bottom spacing and dock hide/peek delays are applied from the settings file.

`--controls` also opens development controls and makes the overlay visible to window inspection tools/Alt+Tab. Default mode is a non-activating tool window. `--lab` opens only the Phase 1 laboratory; compare its three presets and five scenes. Close running GlassDock instances before rebuilding.

Default launch requests a watchdog-owned taskbar lease while the app is active. The separate development button offers a bounded 60-second test. Ctrl+Alt+F12 restores immediately; normal exit and failure recovery also restore. Bare Win and Ctrl+Alt+Space toggle the dock; Win+Space opens Glass Home with application/Settings search. A low-level keyboard hook recognizes the Win gestures and passes normal Win shortcuts through. Context menus suppress dock toggling until they close.

Independent recovery after a Release build (does not require App):
```powershell
& ./src/GlassDock.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/Recovery/GlassDock.Watchdog.exe --restore
& ./src/GlassDock.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/Recovery/GlassDock.Watchdog.exe --status
```
Suppression is a reversible visibility experiment, not production taskbar replacement. See [recovery design](docs/DESKTOP_RECOVERY_DESIGN.md) and [desktop verification](docs/PHASE_2_3_VALIDATION.md) before testing.
NuGet restore requires internet access for development dependencies; application networking is absent.
Use GlassDock.sln in Visual Studio. The current development app is x64 and unpackaged.

Read [product specification](PRODUCT_SPEC.md), [architecture](ARCHITECTURE.md), [roadmap](ROADMAP.md), and [validation](docs/PHASE_0_VALIDATION.md).
Phase 2–3 reuses the Phase 1 glass graph with a native system-backdrop adapter. Refraction remains an explicitly labelled lighting approximation. No discovery, real app launching, full launcher, startup registration, commercial services or shell replacement is implemented.
