# Focused popup and Clear rim refinement

## Presentation

The app action panel keeps its existing window, scrollable action list, theme pipeline,
actions, submenus, keyboard focus and dismissal. Its content width changes from 240 to
224 DIP, row height from 34 to 32, header icon from 26 to 22, and corner radius from
28 to 20. Row padding is 8 DIP; separator margins are 8 horizontally and 3 vertically.
The shadow is disabled on this compact surface.

The stack popup retains its existing owner/monitor positioning. Its HWND anchor now
subtracts a 10-DIP gap and adds back the 4-DIP transparent gutter. The settled visible
surface therefore ends 10 DIP above the dock, subject to physical-pixel rounding and
work-area clamping. No pointer or additional window is introduced.

## Clear specular

The old shader evaluated a narrow exponential highlight once per output pixel on an
approximate signed-distance contour. Its subpixel edge had no coverage antialiasing
and was not the exact retained Bezier outline used to clip the wave.

The highlight now strokes that same CanvasGeometry using Direct2D antialiasing. The
existing body clip keeps the inner half of a 1.4-DIP stroke. Gradient coordinates and
width are converted to physical pixels once using the existing monitor scale. A
cached gradient brush and delegate are reused; no new timer or geometry is added.
Refraction, diffusion and dispersion remain in the existing compiled shader. The
drag lens samples the updated rim along with the dock image.

## Windows capture indicator

Both monitor capture and window preview capture now use CaptureBorderPermission.
Settings > Advanced > Capture indicator exposes an explicit permission request via
GraphicsCaptureAccess.RequestAccessAsync(Borderless). Only Allowed access results
in IsBorderRequired = false for new sessions. Unsupported, denied or unavailable
access retains the Windows indicator. Restart Doky after granting access so every
existing session is recreated with the permission.

The source identity manifest declares uap11:graphicsCaptureWithoutBorder. The locally
registered 1.0.0.1 identity inspected during this change does not declare it. No
package was built, signed or registered in this task. Therefore border removal is
not verified and may remain unavailable until a separately authorized package update.
Other recording applications can still require the indicator.

The code path that starts on Clear expansion is Windows Graphics Capture; the old
DesktopWindowHighlight is a no-op. This supports the OS-indicator explanation, but
the reported physical-screen border has not been independently visually identified.

Official references:
- https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired
- https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap11-capability

## Verification scope

Debug build: zero warnings/errors; 412/412 tests passed. Validate.ps1: locked
restore and Release build succeeded with zero warnings/errors; 412/412 tests passed.
The newest Debug executable was launched for the remaining interactive checks.

Files touched by this focused task (other working-tree changes predate it):
- src/GlassDock.App/Desktop/DockAppContextMenuWindow.cs
- src/GlassDock.App/Desktop/DockStackWindow.cs
- src/GlassDock.App/Desktop/SettingsWindow.xaml
- src/GlassDock.App/Desktop/SettingsWindow.xaml.cs
- src/GlassDock.App/Rendering/DokyLiquidGlassSurface.cs
- src/GlassDock.App/Rendering/LiquidGlassDrawingTarget.cs
- src/GlassDock.App/Rendering/LiquidGlassRim.cs
- src/GlassDock.App/Rendering/Shaders/LiquidGlass.hlsl
- src/GlassDock.App/Rendering/Shaders/LiquidGlass.bin
- src/GlassDock.Windows/Desktop/CaptureBorderPermission.cs
- src/GlassDock.Windows/Desktop/DesktopCaptureSource.cs
- src/GlassDock.Windows/Applications/WindowFrameCache.cs
- packaging/NotificationIdentity/AppxManifest.xml
- tests/GlassDock.Windows.Tests/DockAppMenuWiringTests.cs
- docs/POPUP_VISUAL_REFINEMENT.md

Run Debug build/tests and scripts/Validate.ps1. Automated tests cannot establish
optical quality or Windows consent behavior. Live acceptance must cover app menu
actions/dismissal in all appearances, stack anchoring/Pin Dock/auto-hide, Clear wave
entry/movement/settling at 100/125/150/200 percent DPI, multiple monitors, drag lens,
and consent granted/denied/unavailable paths. Do not report those checks as passed
from compilation alone. Existing user configuration and unrelated work are preserved.
