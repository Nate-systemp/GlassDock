GlassDock smooth directional easing patch
==========================================

This replaces the earlier snappy utility-popup easing.

OPEN
- 220 ms
- gentle sine ease-out
- immediate but soft movement
- smooth settle
- no aggressive snap

CLOSE
- 210 ms
- gentle sine ease-in
- smooth departure
- naturally accelerates back into the clicked source

Important fix:
The previous patch had presentation duration constants that did not match the
duration still hard-coded inside PopupMorph.Progress(). That could cause the
composition track to finish before progress mathematically reached 1.0, followed
by a visible final correction/snap.

This patch makes PopupMorph.Progress() use the same OpenDurationSeconds /
CloseDurationSeconds constants as UtilityPopupPresentation, so the visual track
and progress math stay synchronized.

No changes to:
- V/funnel geometry
- popup state machine
- taskbar/watchdog
- Win-key handling

Performance:
- no new timers
- no new animation controller
- no new polling
- no new geometry rebuilding
- no additional per-frame managed work
- no intentional increase in RAM/CPU/GPU usage

Replace:
- src\GlassDock.Core\Desktop\PopupMorph.cs
- src\GlassDock.App\Desktop\UtilityPopupPresentation.cs

Then:
  dotnet build
  dotnet test

Runtime checks:
- Tray open/close repeatedly
- Quick Settings open/close repeatedly
- Calendar open/close repeatedly
- spam-toggle the same utility
- switch utilities rapidly
- verify no final snap
- verify no lag buildup / RAM growth
- verify taskbar suppression, bare-Win behavior, normal Win shortcuts,
  dock topmost, max-width pinned layout, and hover-wave behavior
