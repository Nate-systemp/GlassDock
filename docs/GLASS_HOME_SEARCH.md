# Glass Home application and Settings search

## Scope and behavior

This follow-up implements only the user's app/Settings search request on the existing Phase 1 Glass Home working tree. It does not implement pinned launcher content, recent files, power actions, Task View, or web search. Existing browse placeholders remain as supplied.

First Home open starts a single retained application index. Typing filters cached application and Settings entries synchronously in memory, without disk work or background tasks per query. Results appear below the search bar, capped at eight; low available vertical space scrolls the list. Empty text hides the panel. Unknown text displays No results. Search retains the original window top position and does not alter the pointer origin or expansion state. Moving 30 physical pixels still latches Expanded; active search keeps its query/results visible, and clearing it reveals the existing browse view.

The first result is selected after a query change. PreviewKeyDown on the TextBox handles Up/Down with clamped selection and scroll-into-view, retaining input focus. Enter and item clicks use the same launch path. Index/icon refresh preserves the selected stable identity when still present. Escape uses the original handler. Launch completion checks the Home session revision before hiding it; an old launch cannot hide a newly opened session. Launch failure leaves Home visible with an error, retained across icon refreshes until the query changes.

## Discovery, caching, Settings and icons

- Extended existing ShellApplicationMetadata; did not duplicate the dock's running-window/pin reconciliation service.
- Enumerates Windows Shell AppsFolder, including Shell-registered desktop, built-in, and packaged apps.
- Supplements it with .lnk application shortcuts under the current user's and common Start Menu Programs directories. Ignores inaccessible entries and reparse points; does not scan Program Files or the full disk.
- Reuses ApplicationIdentity, Shell property extraction, ResolveLink, and WindowsApplicationIconService. Deduplicates by stable identity. Keeps launchable shortcut/Shell paths intact instead of assuming executable paths.
- One STA worker starts once per retained Home window. Publishes read-only metadata snapshots in batches, then loads native icons in batches. UI notifications coalesce and always filter current text. Index and icon state are in memory; restart GlassDock to pick up subsequent installs/removals.
- WindowsSettingsCatalog stores nine WindowsSettingEntry records with names, descriptions, aliases and URIs. The UI contains no ms-settings strings. Reference: [Microsoft Settings URI catalog](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings).
- Launching reuses WindowsApplicationLauncher / ShellExecuteEx, with a single in-flight UI request and STA worker for search launches. A successful return means Shell accepted the request, not that the target app finished startup.
- Existing native icon infrastructure owns and releases PIDLs, COM objects, HBITMAPs and owned HICONs. Core transports managed BGRA pixels; AdaptiveAppIcon renders them. Null icons use app or Settings glyphs.

## Validation on 2026-09-15

### Build and automated checks

- `scripts/Validate.ps1`: full Release solution build succeeded with **0 warnings, 0 errors**. Script stopped on the pre-existing Core Windows-key test failure below.
- Final source full solution build succeeded with **0 warnings, 0 errors**, using `dotnet build GlassDock.sln -c Release --no-restore -p:BaseOutputPath=C:\Dev\GlassDock\artifacts\search-build\`. An elevated test process held the normal output executable open, so the separate output directory avoided the file lock. No compile errors remain.
- Focused Core search + existing Home-session tests: **15 passed** (ranking, aliases, empty/unknown, eight-result bound, selection/reset, stationary pointer/window growth, threshold, reopening and outside-button edges).
- Focused Settings catalog tests: **9 passed**, covering all requested aliases and expected launch URIs.
- Full suites: Core **81 passed / 1 failed**; Windows **29 passed / 1 failed**. Failures are in source/tests unchanged from HEAD and from the incoming working tree:
  - `WindowsKeyGestureTests.Existing_modifier_and_two_windows_keys_are_not_bare`, line 49: dual Windows-key release is treated as bare. No search changes touch this recognizer.
  - `DesktopWindowHighlightTests.Overlay_is_hollow_nonactivating_and_leaves_targets_unchanged`, line 234: reflection dereferences the nonexistent `overlay` field. No search changes touch highlight code/tests.

### Live Windows service probe

`scripts/Test-SearchIndex.ps1 -Launch` builds an ignored harness under artifacts/search-index-probe. The default script only indexes/searches; -Launch explicitly opens Notepad and Bluetooth Settings and checks a missing executable failure.

Observed:

- **293** application entries discovered in **6250 ms**, no warnings.
- `notepad` → Notepad; `calc` → Calculator; `expl` → File Explorer.
- `code` → Visual Studio Code and GhostCoder (real third-party apps on this machine).
- `bluetooth`, `display`, `wifi`, `sound`, `startup`, `update` all returned the appropriate Settings result first.
- **1000 cached queries in 169 ms** on this machine (indicative service measurement, not a UI latency benchmark).
- Native icons observed for **15 entries** before the probe stopped its index worker; metadata was usable before icon loading finished.
- Shell launch returned true for Notepad and Bluetooth & devices. Desktop enumeration confirmed Notepad and Settings windows. Exact Settings content could not be inspected by the helper, so the live Bluetooth page itself is not visually certified.
- Missing executable returned false without crashing.

### Interactive validation limits

The existing app manifest is `requireAdministrator`. The test instance ran elevated; the desktop helper reported higher-integrity access limitations, could not reliably capture or drive Home, and process close/stop attempts were denied. A second launch encountered the existing recovery-hotkey registration conflict. The independent recovery executable reported RESTORED. The user was asked to exit the test instances; no manifest, hotkey, taskbar service, or Windows permission change was made by this search work.

Consequently **the exact Win+Space → type → arrows → Enter → hide sequence is not claimed as interactively verified**. Autofocus, actual result-row layout/clicks, Escape, reopening focus, rapid typing in WinUI, physical mouse thresholds, click-outside, and dock/preview rendering still need an interactive pass. Unit tests and source review cover their state contracts but do not substitute for those checks. Calculator/File Explorer discovery was checked, not their live launch. Full dock/taskbar/recovery regression was not repeated.

## Known limitations

- Apps absent from both AppsFolder and Start Menu (for example unregistered portable executables) are not discovered. No hardcoded application name list.
- Packaged apps use Shell-provided AppsFolder targets. Availability, permissions, removal, or broken registrations can still prevent launch. The live launch probe ran at normal process integrity; packaged launch from the existing elevated UI needs manual confirmation.
- Stable identity deduplication can leave differently registered entries with the same display name (observed for some updater entries); arguments/identities are deliberately not discarded.
- Settings names/aliases are currently English. Some pages depend on OS version, hardware, or administrative policy (notably Wi-Fi and battery).
- Shell calls can be slow; cancellation is cooperative between calls. A hung third-party Shell extension cannot be forcibly interrupted, but its background worker does not block Home opening or process shutdown.
- Native icons are best effort and load progressively. The index retains icon pixel arrays for the Home lifetime; no on-disk cache, live install watcher, or memory-budget tuning is added in this phase.

## File inventory for this run

Created source/test/script/document files:

- src/GlassDock.Core/Applications/GlassSearch.cs
- src/GlassDock.Windows/Applications/WindowsApplicationIndex.cs
- src/GlassDock.Windows/Applications/WindowsSettingsCatalog.cs
- tests/GlassDock.Core.Tests/GlassSearchTests.cs
- tests/GlassDock.Windows.Tests/WindowsSettingsCatalogTests.cs
- scripts/Test-SearchIndex.ps1
- docs/GLASS_HOME_SEARCH.md

Modified by this run:

- src/GlassDock.App/Desktop/GlassHomeWindow.cs (already present but untracked when this run began)
- src/GlassDock.Windows/Applications/ShellApplicationMetadata.cs
- src/GlassDock.Windows/Applications/WindowsApplicationLauncher.cs
- PRODUCT_SPEC.md
- ARCHITECTURE.md
- ROADMAP.md

Existing Phase 1 prerequisites were preserved and included with the integration commit so it can build from checkout: DesktopOverlayWindow.cs, DesktopGlassBackdrop.cs, IKeyboardService.cs, WindowsKeyboardService.cs, GlassHomeSession.cs, GlassHomeInput.cs, InteractiveGlassWindowHost.cs, and GlassHomeSessionTests.cs. This run did not edit their contents. The pre-existing GlassDock.App.csproj BOM/trailing-space change is unrelated and remains outside the commit.

Generated probe sources/build outputs and isolated full-build outputs are under ignored artifacts/, bin/, and obj/ directories, not product source or committed machine inventories.
