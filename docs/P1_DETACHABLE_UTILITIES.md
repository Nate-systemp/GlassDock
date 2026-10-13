# Detachable Utilities implementation and verification

## Status pill refinement — 2026-10-12

### Click crash correction

The development-error.log recorded `NullReferenceException` in
`SystemQuickSettingsWindow.AddVerticalSlider` at line 78, reached from
`BuildDetachedModules` / `SetDetachedLayout` on launcher click. Windows Application
events at 12:22:17, 12:24:02 and 12:24:56 recorded Microsoft.UI.Xaml.dll failures
with exception code 0xc000027b. The code assumed `slider.Parent` was populated before
the first window load; it was null. Slider owners are now recorded when SliderRow
constructs the controls, and detached hosts are explicitly tracked for restoration.

Debug build: zero warnings/errors; 39 focused Utilities tests passed. In the live
Debug process, posting its registered Utilities toggle message opened a visible
Doky Quick Settings HWND. Ten further toggle requests left that same process
responsive; the error log retained its old 12:24:56 timestamp. This checks the shared
open path but is not a physical mouse click or visual-reference approval.

The detached vertical controls now use broad filled capsule surfaces, preserving
the existing native sliders for input/UIA and removing handlers when restoring the
inside-dock layout. The corrected build is running for a user visual check.

This revision supersedes the original circular launcher and compact-panel dimensions
below. Floating Utilities now uses a 124 × 28 DIP capsule, compacted for the
top-right corner while retaining the status icons and local clock. The visible pill
keeps a 28 DIP inset from the top/right work-area edges; its transparent window
gutter is handled separately. The panel is 440 × 466 DIP,
aligned to the pill's right edge with a 12 DIP visible gap below it. Colors continue
to follow Doky's five appearances rather than forcing blue in Light/Dark.

The pill displays network availability, mute/sound, battery and local time. A moon
appears only for a confirmed active Focus session. **This is not a generic DND
detector**: the existing supported adapter cannot reliably read Windows DND. The
network glyph represents connectivity; the existing service does not identify
every active route as specifically Wi-Fi. Battery estimates are not fabricated.

The same radio buttons and sliders are reparented into the detached card layout;
Inside Dock restores their original layout. Vertical sliders retain native input
and accessibility and reuse Home's track-size resources. Media uses the public
Windows media-session API, with real title/artist/artwork, playback actions and a
read-only timeline. No session or denied access yields a disabled empty state.
The package manifest now declares `uap7:Capability Name="globalMediaControl"`;
existing signed identity installations need a rebuilt/re-registered package before
claiming media support there. No signing or registration was performed here.
See [Microsoft capability documentation](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap7-capability).

Unpin source diagnosis: `SetPinnedPlacementFrame` reduced the expanded surface's
height without updating `DockIconTransition`'s expanded-height reference. Its
geometry ratio therefore scaled icons down as though the dock were collapsing.
The controller now updates that reference on every pin-placement frame. Regression
coverage checks 0/25/50/75/100% placement depth. Visual reproduction/confirmation is
still a manual check, not proven by that test alone.

Additional changed files for this revision:

- `src/GlassDock.App/Desktop/DockAnimationController.cs`
- `src/GlassDock.App/Desktop/SystemQuickSettingsWindow.Detached.cs`
- `src/GlassDock.App/Desktop/UtilityPopupStyle.cs`
- `src/GlassDock.Windows/Desktop/WindowsMediaControls.cs`
- `packaging/NotificationIdentity/AppxManifest.xml`
- `tests/GlassDock.Core.Tests/DockIconMotionTests.cs`

Also revised: FloatingUtilitiesWindow, DesktopOverlayWindow, SystemQuickSettingsWindow,
FloatingUtilityPlacement and UtilitiesPlacementTests. Validate unpinning without
hovering, reopening the panel, switching placement back to Inside Dock, real media
playback, all five appearances and mixed-DPI positioning. The native UI is not
available to the current automation tool, so reference fidelity remains unverified.

## Repository audit

Status-pill validation: Debug and Release builds completed with zero warnings and
errors. Each full suite reports 384 Core + 186 Windows passes and the same single
pre-existing menu-radius failure described below. `scripts/Validate.ps1` therefore
still fails on that assertion. Logs are `artifacts/utilities-pill-build.log`,
`artifacts/utilities-pill-tests.log` and `artifacts/utilities-pill-release.log`.
The Debug app was launched after resetting only the saved launcher coordinates;
the previous settings were backed up beside settings.json. No runtime visual test
has been reported as passed for this revision.

The existing implementation already supplied utility discovery, hardware controls,
radio controls, Calendar, Hidden Tray, popup transitions/dismissal, theme resources,
Liquid Glass capture, settings persistence, and fullscreen geometry detection.
`DesktopOverlayWindow` owns and caches utility windows; `UtilityPopupRequests`
serializes requests. `SystemQuickSettingsWindow` already owns the real sliders and
radio controls. These are retained rather than duplicated.

Before this change, independent Utilities placement, a floating launcher, its
position persistence and its shortcut were not implemented. Existing positioning
was dock-anchored. Monitor and fullscreen infrastructure existed but needed a
separate launcher HWND. Focus and Night Light do not expose unrestricted supported
toggle APIs in this implementation; they continue to open the appropriate Windows
settings, with truthful labels. The existing virtual-desktop crash patch still
requires the user's actual desktop-deletion stress test.

## Status

| Area | Status | Implementation / remaining verification |
|---|---|---|
| Placement and settings persistence | DONE in code | Inside Dock, Floating Button, Shortcut Only; legacy default stays Inside Dock |
| Dock layout | DONE in code | Removes the actual cluster and its 248 DIP width contribution |
| Existing Control Center reuse | DONE in code | Same cached window, controls and services; retained two-column compact layout |
| Launcher and context actions | DONE in code | 48 DIP circle; toggle, Settings, Return to Dock, Calendar and Hidden Tray |
| Drag/snap | DONE in code | 6 DIP threshold, pointer capture, 160 ms smoothstep snap, reduced-motion fallback |
| Hover | DONE in code | Existing palette states plus 120 ms scale to 1.04 |
| Shortcut | DONE in code | Registered Ctrl+Alt+U by default; optional Space; conflict reported; no new keyboard hook |
| Fullscreen and display recovery | PARTIAL verification | Uses existing display tick, monitor-specific predicate and work-area clamp; hardware tests pending |
| Themes and screenshots | PARTIAL verification | Existing UtilityPopupStyle/PopupLiquidGlassSurface and screenshot lifecycle reused; visual tests pending |
| Virtual desktops | PARTIAL verification | No new private COM interfaces or desktop tracking; all-desktop launcher presence is not implemented |
| Manual acceptance | NOT DONE | Startup process checked; user interaction checklist below remains pending |

Ctrl+Alt+Space is already registered by Home. Utilities therefore defaults to
Ctrl+Alt+U rather than stealing that binding. The elevated helper and its protocol
are unchanged; no helper reinstall is needed.

The reference is represented by a compact title row, two-by-two tiles, then volume
and brightness. Calendar and Hidden Tray remain accessible from the launcher's
context menu and the Control Center's header menu (including Shortcut Only mode).
The main dock's existing appearance and controls layout remain available in Inside
Dock mode. The compact panel is 240 by 330 DIP plus the existing transparent popup
gutter, and uses existing utility materials and corner treatment.

## Files changed

- `src/GlassDock.Core/Settings/UtilitiesPlacement.cs`
- `src/GlassDock.Core/Settings/GlassDockSettings.cs`
- `src/GlassDock.Core/Settings/SettingsCatalog.cs`
- `src/GlassDock.Core/Desktop/FloatingUtilityPlacement.cs`
- `src/GlassDock.Windows/Desktop/WindowsUtilitiesLauncherHost.cs`
- `src/GlassDock.App/Desktop/FloatingUtilitiesWindow.cs`
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.cs`
- `src/GlassDock.App/Desktop/SystemQuickSettingsWindow.cs`
- `src/GlassDock.App/Desktop/WindowsSystemControlService.cs`
- `src/GlassDock.App/Desktop/SettingsWindow.xaml`
- `src/GlassDock.App/Desktop/SettingsWindow.xaml.cs`
- `src/GlassDock.App/Desktop/SettingsWindow.Shell.cs`
- `tests/GlassDock.Core.Tests/UtilitiesPlacementTests.cs`
- `tests/GlassDock.Core.Tests/SettingsCatalogTests.cs`
- `tests/GlassDock.Windows.Tests/UtilitiesLauncherTests.cs`
- `docs/P1_DETACHABLE_UTILITIES.md`

## Validation on 2026-10-10

- Debug build: succeeded, zero warnings/errors.
- Release build: succeeded, zero warnings/errors.
- Full suites in each configuration: Core 382 passed; Windows 186 passed, 1 failed.
- The existing failure is `DockAppMenuWiringTests.GlassAppMenuMatchesSilverJumpListReferenceWithoutChangingSolidThemes`.
  It expects the literal `MenuCornerRadius = 9;`, while existing production code uses
  `9 * MenuSizeScale`. Neither its assertion nor the unrelated menu implementation
  was changed. Consequently `scripts/Validate.ps1` is not green.
- New coverage includes legacy preferences, JSON round-trip, malformed preferences,
  negative monitor coordinates, edge snapping, disconnected-monitor fallback,
  small work areas, DPI-scaled drag thresholds, real RegisterHotKey conflicts and
  releasing the registration on disposal.
- Existing popup request, theme, dock, desktop-removal and lifecycle tests ran in
  the full suite; this is not proof of interactive Windows behavior.
- Logs: `artifacts/utilities-debug-build.log`, `artifacts/utilities-debug-tests.log`,
  `artifacts/utilities-release-validation.log`.

For the requested testing configuration, the current user's Utilities placement
was set to Floating Button, fullscreen hide/snap/shortcut enabled, Ctrl+Alt+U.
Other settings were preserved; a backup was saved beside settings.json as
`settings.json.before-floating-utilities.bak`. No pins were modified. The Debug
executable was launched and its process observed running. Visual verification is
pending, not passed.

## Windows manual checklist and risks

1. Switch through all three placement modes. Verify correct dock width, centering,
   no empty cluster, and restoration of existing utilities.
2. Click launcher repeatedly; check compact layout and readable radio details.
   Test volume, brightness, Wi-Fi, Bluetooth and the labeled settings fallbacks.
3. Close with Escape and outside-click. Toggle with Ctrl+Alt+U. Verify a conflicting
   shortcut reports failure, and normal Home and Win shortcuts are unchanged.
4. Drag with small movements and beyond threshold; ensure release never clicks.
   Snap to each edge, restart and confirm persistence; reset position in Settings.
5. Drag between mixed-DPI monitors; disconnect one, change resolution, and confirm
   clamping and popup anchoring. The bottom reserved band needs a visual check
   against unusually large/custom dock sizes.
6. Test fullscreen entry/exit on both monitors, including Shortcut Only and Pin Dock.
   Exclusive-fullscreen games were not tested.
7. Switch Dark/Light/Frosted/Acrylic/Clear while open; verify material, control
   contrast, screenshot inclusion, no feedback and no unnecessary capture border.
8. Switch/remove virtual desktops repeatedly, including the launcher-owning desktop.
   Public APIs do not provide all-desktop pinning here: Windows may keep this
   independent window on its assigned desktop. Do not claim omnipresent launcher.
9. Confirm Calendar/Hidden Tray and their existing activation behavior, dock stacks,
   previews, animations, drag lens, input helper and watchdog recovery.
10. Close Doky during snap or with a panel open; confirm clean shutdown and no orphan
    launcher. Hardware sleep/wake and display recovery remain manual checks.

No commit, push, tag, installer, publication or release was performed.
