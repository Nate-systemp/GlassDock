# P0 #2 core dock behavior

## Changes

Dock icon clicks now use current foreground/minimized state. A single active
window receives a minimize request; inactive/minimized windows activate/restore,
including the existing elevated restoration fallback. Groups retain activation
and preview selection rather than minimizing an arbitrary group member. Preview
and Home actions remain activate-only. Failed known-window validation cannot
fall through to launching another process.

The overlay previously reasserted topmost on every foreground change. It now
checks foreground window bounds against the dock monitor, excluding our process
and desktop/taskbar shell windows. A covering foreground window suppresses only
Doky's HWND without activation. The existing 200 ms display check also detects
size-only fullscreen changes. No game process is opened, modified or injected.

Fullscreen overrides visibility even when pinned, preserving pin settings and
logical geometry. Popups dismiss, hidden Clear capture stops, and the retained
appearance resumes on exit. No watchdog/input-helper protocol changes were made.

## Validation and limits

Debug/Release builds and full suites are required. Added click-policy cases,
monitor-coverage/entry-exit cases and invalid native-window command rejection.
Existing app collection, preview, identity, file-drop and P0 animation tests
remain part of the full suite. Policy tests do not validate games or elevated
foreground behavior, and do not establish complete taskbar parity.

Manual checks still required:

1. Single ordinary app: inactive activate, active minimize, minimized restore.
2. Multiple windows: predictable group activation and individual preview choices.
3. Elevated Task Manager/Visual Studio: restoration, denied commands, no launch
   fallback. Elevated minimization may be rejected by Windows; it is not forced.
4. Browser F11 and borderless/exclusive games including Valorant/League: dock
   absent on that monitor, second-monitor dock unaffected, no focus stealing.
5. Repeat fullscreen with Pin Dock on/off; exit via Alt+Tab and fullscreen toggle.
6. Monitor changes, normal maximized windows, auto-hide taskbar configurations,
   all appearances, popups and reversible dock animations.

Bounds-based detection is conservative: a maximized window occupying the entire
monitor (for example with no reserved work area) can be treated as fullscreen.
Fullscreen windows spanning monitors and protected-game edge cases are not yet
certified. There is no claim of tested gameplay or anti-cheat compatibility.
The 200 ms size-change check has bounded detection latency. Startup and current
automated results must be reported separately from manual acceptance.
