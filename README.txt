GlassDock Utility Interaction Polish
====================================

Base expected:
- Post-Astra utility popup architecture already applied.
- Smooth hover-wave + Hover Wave setting patch already applied and building.

This patch updates the utility popup interaction without changing taskbar,
keyboard hotkey, app pin/reorder, or window activation code outside the
utility system.

Included behavior
-----------------
- Dock-anchored open/close motion inspired by a macOS-style collapse-to-source
  relationship (not a literal Genie warp).
- Non-uniform X/Y scaling so a panel visibly funnels toward its clicked utility
  control instead of only doing a generic fade/scale.
- Exact X/Y source anchor, including when the popup is clamped near screen edges.
- XAML content and native desktop-glass/backdrop edge use the same transform.
- 225 ms open / 180 ms close with Stopwatch-based timing and an 8 ms dispatcher
  cadence for smoother high-refresh displays.
- Respects Windows "Animations" accessibility setting: transitions become instant
  when OS animations are disabled.
- Click-outside dismissal via window deactivation.
- Escape still dismisses through the same animated close path.
- Only one utility panel is presented at a time: switching waits for the outgoing
  panel to finish its close before opening the incoming one.
- Active utility button gets a restrained selected state while its panel is open.
- Per-popup source anchors survive popup repositioning / monitor-DPI movement.
- Hover wave/magnification remain suppressed during utility popup transitions.
- Popup construction failures are caught and reported through GlassDock status
  instead of crashing the desktop overlay.
- Calendar and dock clock now follow the system short-time format (12/24 hour).

Files replaced
--------------
src\GlassDock.App\Desktop\DesktopOverlayWindow.cs
src\GlassDock.App\Desktop\UtilityPopupPresentation.cs
src\GlassDock.App\Desktop\SystemTrayWindow.cs
src\GlassDock.App\Desktop\SystemQuickSettingsWindow.cs
src\GlassDock.App\Desktop\CalendarPopoverWindow.cs
src\GlassDock.App\Rendering\DesktopGlassBackdrop.cs

Build / test
------------
cd C:\Dev\GlassDock
dotnet build
dotnet test

Mandatory runtime validation
----------------------------
Do not consider the task complete just because build/tests pass. Launch GlassDock
and verify:
1. Windows taskbar stays suppressed when the pointer reaches the bottom edge.
2. Bare Win still controls GlassDock / does not open Start while Glass Home is open.
3. Win+Space still toggles Glass Home.
4. Win+R, Win+E, Win+L, Win+D, Win+Tab still pass through normally.
5. Dock remains topmost above normal app windows.
6. Maximum-width pinned-app layout has no clipping/overflow/collision.
7. Hidden Tray, Quick Settings, Calendar each open from the exact clicked source.
8. Clicking the same utility closes it smoothly back toward the source.
9. Switching Tray -> Quick Settings -> Calendar never leaves two stable popups open.
10. Clicking outside and Esc dismiss the active popup.
11. Dock wave/bump stays absent while a utility popup or transition is active.
12. Hover wave works again after the popup closes (when Hover Wave is enabled).
13. Appearance changes still apply live to open utility popups.
14. Popup positioning remains correct on multiple monitors and DPI scales.
15. No flicker, jagged/pixelated rim, stale wave geometry, or popup left behind.

Note
----
The code was statically checked for balanced syntax and kept on top of the latest
post-Astra + smooth-wave files supplied in chat. A Windows WinUI runtime/build is
still the authoritative validation environment.
