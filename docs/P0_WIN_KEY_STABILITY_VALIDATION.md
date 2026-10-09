# P0 #1 validation — 2026-10-09

Status: partly complete. Automated checks and one unexpected-exit recovery test
pass; visual stress acceptance is still required. No commit or release created.

## Corrections

- Respect Windows disabled animations: commit surface geometry, icon presentation,
  indicator and placement to the endpoint through the existing completion path.
- Sample indicator opacity before stopping its old clock, avoiding a base-value
  jump when retargeting the pill fade.
- Unregister icon geometry callbacks when the animation controller is disposed;
  stop timers/animations and settle pending work before releasing callbacks.
- Keep the existing geometry-driven icon motion, transition revision checks and
  Liquid Glass renderer. These changes do not establish a cause for every prior
  crash and do not suppress unexpected exceptions.

## Observed validation

- Debug build: zero warnings/errors. Debug tests: 283 Core + 157 Windows passed.
- scripts/Validate.ps1: locked restore, Release build and all 440 tests passed.
- Added 500-toggle state regression with interleaved stale/immediate completions
  and holds, plus 500 ordered helper events with duplicate rejection.
- Debug launch started App, the registered protected InputHelper and Watchdog.
- With one taskbar reported hidden, terminated only the verified Debug App PID
  23368. Independent watchdog status then reported Available=true, Visible=true,
  Enabled=true. App/helper/watchdog processes had exited. Explorer was untouched.
- The installed helper's Windows DLL differs from the current Debug DLL. Startup
  succeeded, but elevated foreground gesture compatibility still needs the manual
  pass below. No protected helper installation was changed.

These are not visual animation tests. Existing state, icon-motion, mailbox and
protocol tests cannot prove compositor continuity or hardware/device recovery.

## Manual acceptance: at least 500 physical Win-key toggles

Use the current Debug build, Pin Dock off and hover-only opening off. Record any
temporary setting changes and restore them afterward. Keep Ctrl+Alt+F12 and the
independent Recovery/GlassDock.Watchdog.exe --restore command available.

1. Dark: 100 presses. Alternate settled presses with bursts during opening and
   closing. Keep the pointer away from the dock to avoid confusing hover requests.
2. Light: 100 presses, including reversals near both endpoints.
3. Frosted: 100 presses, varying intervals roughly 80–300 ms.
4. Acrylic: 100 presses, including a pause after each burst to inspect final state.
5. Clear: 100 presses, half with a normal app focused and half with elevated Task
   Manager focused. Check refraction, blur, rim and dispersion during reversals.

For each batch verify: no snap, icons follow geometry, no stuck/black/invisible
surface, no delayed replay after stopping, no Windows Start flash. From a known
collapsed state, an even number of accepted toggles should return to collapsed.
Auto-collapse and hover can change state independently; distinguish those from
lost input. Check Win+E, Win+R and Doky's Win+Space after the elevated batch.

Repeat a short batch with Windows Animation effects disabled: dock/icons/indicator
must settle immediately together. Re-enable and verify the normal animation.
Exercise context-menu interruption and normal exit during both directions.

Separately test lock/unlock, sleep/resume, monitor/DPI changes and capture denial
or interruption. Record fallback and recovery honestly; automatic graphics-device
recovery is not certified by this patch. Do not kill or modify Explorer.

Record build path, helper path/version, OS, GPU, DPI, appearance, count, timings,
final state and any exception for each failure. P0 #1 remains incomplete until
these manual/elevated/Clear checks pass.
