# Utility glass implementation and validation

## Rendering

The expanded dock previously rendered thin XAML wave rim/glow paths separately from the native backdrop's physical-pixel mask. The paths could have fractional-DIP stroke placement and independently rasterized coverage at their perimeter. The rendering fix removes those visible XAML strokes. The retained wave path still supplies native input geometry; input behavior is unchanged.

`DesktopGlassBackdrop.UseInnerEdge` opts the dock and utility popups into a shared compositor path for body and edge. Shape-mask coverage is sampled at twice physical resolution and linearly downsampled. The desktop image itself is not supersampled or blurred more to hide aliasing. Animated curves keep continuous subpixel positions rather than rounding each curve point and introducing stepping.

An alpha-masked, narrow inner band composites a slightly brighter/more saturated version of the existing live blurred desktop sample. The same outer mask clips the band, including at wave crests. This is real compositor backdrop sampling, blur, saturation, exposure, and coverage masking. The apparent refraction is simulated; there is no displacement or optical ray bending. Border thickness/opacity settings control band width/strength, including zero. Other surfaces remain on their existing rendering path unless explicitly opted in.

## Shared utility chrome

`UtilityPopupStyle` owns the material, radius, gutter, monitor-clamped placement, and motion-aware fade. Tray, quick settings, and calendar reuse it. Static GlassSurface rim and painted lighting are disabled for these surfaces; its soft shadow remains. The calendar retains native date selection/month navigation with transparent styling. Tray accessibility discovery/default invocation/right-click fallback are retained. Stable tray lists update their action references without rebuilding focused buttons every second.

The tray bridge provides accessible names/actions, not original tray icon bitmaps. Existing labeled initial placeholders remain; no claim is made that these are the applications' original icons. Explorer can expose no accessible items, in which case the honest empty state remains.

## Checks

- Release build: zero warnings/errors; Validate.ps1: Core 99, Windows 60 (159 total).
- Follow-up drag fix removes the pinned-only input restriction and applies the existing saved identity order after combining pinned and running applications. Running apps retain their unpinned identity. Regression tests cover reload, reconciliation, duplicate rejection, and legacy JSON. The reported rightmost-app drag still requires a physical runtime check.
- Runtime launched with live watchdog. Status: taskbar Visible=false, Enabled=false, AutoHide=false.
- User confirmed the first dock-edge pass was translucent/smoother but reported stacking behind windows.
- Added a 250 ms no-activation stacking recheck for external foreground apps. It skips the recheck while a GlassDock window is foreground. Native regression test verifies repairing a demoted dock without changing foreground activation.
- Final popup/topmost visual checks are pending user confirmation. The desktop UI helper could not bind this app in this session's earlier checks.
- Not automatically verified: all combinations of 100/125/150/175/200 percent display scaling, multiple live monitors, dark/bright/colorful wallpapers, the full physical Windows-key shortcut list, and all drag/drop paths. Passing unit tests does not substitute for these visual/input checks.

## Shared appearance and anchored presentation follow-up

UtilityMaterial derives blur, opacity, tint, saturation, and borders from the dock appearance snapshot. Open windows receive ApplyAppearance from the existing settings-change handler; new windows receive the current snapshot. Popup geometry stays compact with a 24 DIP radius and shared shadow.

UtilityPopupPresentation uses one 190 ms eased clock for XAML content and the native backdrop mask: 0.965 to 1 scale, 6 DIP upward settling, and opacity. Escape and explicit toggle dismissal reverse the transition; switching popups and shutdown close immediately to prevent overlapping panels or delayed teardown. Like the explicitly requested dock transformation, popup motion remains enabled when Windows optional animations are disabled. Opening is queued after initial loading and its clock starts on the first frame tick. The anchor uses the clicked utility button, including monitor-clamp compensation.

PrepareSystemPopup clears the held dock wave, removing the connector-like bump without changing ordinary dock hover behavior. The edge remains the existing supersampled compositor mask, with simulated refractive contrast rather than optical displacement.

Validation: Release build zero warnings/errors; Core 102 and Windows 60 tests pass (162 total). Runtime app/watchdog launched; status reports Visible=false, Enabled=false, AutoHide=false. User confirmed live appearance matching. They reported no visible motion in the first build; the optional-animation skip was removed and opening was deferred past initialization. Desktop automation cannot enumerate the overlay; revised motion, keyboard and physical bottom-hover/topmost checks await user verification.

## Hover-wave continuity follow-up

The previous outline changed between separate corner-merge branches at a position threshold; both the native and XAML implementations duplicated this discontinuity. The fixed 16 ms timer also applied a fixed interpolation fraction independent of elapsed time.

DockWaveGeometry now supplies a single DIP-space cubic outline to the native body/rim mask and the input geometry. A compact smooth profile supplies analytic tangents; only the wave footprint is subdivided, with continuous corner endpoints and no threshold-based corner replacement. The body and edge still share the same CompositionPathGeometry, 2x physical coverage masks, and linear downsampling. No material blur, alpha, stroke-width or glow adjustment hides the edge. Subpixel positions are retained rather than snapping moving curves to integer pixels.

CompositionTarget.Rendering replaces the dispatcher timer; exponential interpolation uses elapsed time so 30/60/144 Hz converge consistently. Invisible duplicate glow geometry is no longer rebuilt. Tests cover tangent continuity at five positions, continuity across the former corner threshold, and frame-rate independence. Physical visual assessment across DPI scales remains necessary.
