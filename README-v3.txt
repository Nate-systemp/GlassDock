GlassDock Utility Interaction Polish v3

Purpose
- Fix utility popups that appeared instantly even though the animation path was present.
- Make the source relationship visibly obvious: popup grows from/collapses into the clicked utility control.

What changed from v2
- Utility popup motion no longer depends on Windows UISettings.AnimationsEnabled, which could silently disable the requested GlassDock motion.
- The collapsed first frame is intentionally held for 34 ms so Activate/layout cannot be coalesced with the first visible animation frame.
- Opening duration is 310 ms, closing 235 ms.
- Stronger non-uniform funnel transform: X 0.48 -> 1, Y 0.18 -> 1 on open; reverse on close.
- 30-DIP vertical travel and restrained end settle.
- Same per-frame transform is still applied to XAML content and DesktopGlassBackdrop/inner edge.
- Existing tray, quick settings, calendar, popup switching, selected utility state, wave suppression, positioning and DPI logic are otherwise unchanged.

Install
Extract over C:\Dev\GlassDock and replace existing files.

Verify
powershell -ExecutionPolicy Bypass -File .\VERIFY.ps1

Runtime verification is mandatory after build/tests.
