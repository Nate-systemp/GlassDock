# Dock-only Liquid Glass prototype

## Physically inspired chromatic dispersion (2026-10-07)

The existing WGC → Win2D shader → dock geometry path is retained. The drag lens
uses this same shader through its existing transparent CompositionDrawingSurface;
there is no second optical implementation or capture source.

`LiquidGlassMaterial.ChromaticDispersion` is a per-channel maximum offset in DIPs:
default 0.65, bounded to [0, 1], nonfinite values reset to 0.65. Zero disables the
spectral sampling branch. The drag lens uses 0.85. Both convert to physical pixels
in ConfigureShader through the existing DPI scale, using the existing Lens.y slot.
The maximum red-to-blue separation is therefore 1.3 DIPs for Clear and 1.7 DIPs
for the lens, before edge/refraction weighting.

The original refracted coordinate is unchanged. Red and blue sample on opposite
sides of that coordinate along the existing SDF normal; green stays centered.
Offset = dispersionPixels × edge² × saturate(2 × refractionPixels / edgeWidthPixels).
This DPI-invariant weighting vanishes at zero refraction and in the calm center.
The same wave-aware distance field supplies both edge strength and normals, so
fringing follows sides, corners and the deformed upper rim rather than a fixed
screen direction. No artificial rainbow colors or content-analysis pass are used.

Only the dominant 60% diffusion tap is dispersed; the four neutral 10% neighbor
taps are retained. Tint and neutral specular lighting run afterward. This keeps
the five-tap diffusion footprint and white highlights while avoiding fifteen
texture reads. Exact dock geometry clipping and the lens's premultiplied capsule
alpha remain unchanged. The former lens-only post-lighting channel split was
removed in favor of this shared step.

Cost is at most seven texture reads instead of five for Clear, and seven instead
of seven for the lens. The branch can skip the two added reads when disabled or
outside the optical edge; actual GPU savings depend on execution/driver behavior.
No passes, timers, captures, readbacks, windows or per-frame resources were added.
WDA exclusion, native fallback, monitor coordinates and cleanup are unchanged.
This is an RGB screen-space approximation, not physically exact spectral rendering.
HDR, color management, high-refresh throughput and long-term resource behavior
are not certified by the automated tests.

Changed in this pass: LiquidGlassMaterial.cs, DokyLiquidGlassSurface.cs,
Shaders/LiquidGlass.hlsl, its compiled LiquidGlass.bin,
tests/GlassDock.Core.Tests/LiquidGlassMaterialTests.cs, and this document.

Validation and runtime results for this pass are recorded below separately from
the historical prototype results. Manual acceptance is pending: black/white text,
horizontal/vertical lines, grayscale/color wallpaper, center/edges/corners/rim,
slow/fast hover and 20+ cycles, collapse/reopen, Dark → Clear → Acrylic → Clear,
Pin Dock/auto-hide, stacks/drag lens, multi-monitor, 100/125/150/200% DPI, GPU/CPU
profiling and resource soak. Capture exclusion prevents screenshot-based visual
certification of Clear; user observation is required.

- Shader compilation succeeded. Debug build: zero warnings/errors; 402/402 tests
  passed (250 Core, 152 Windows). An initial prematurely started test run hit a
  watchdog copy mismatch during the build; the completed-build rerun passed.
- `scripts/Validate.ps1`: locked restore, Release build with zero warnings/errors,
  and 402/402 tests passed.
- New Debug process launched and path verified (PID 13112):
  `C:\Dev\GlassDock\src\GlassDock.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\GlassDock.App.exe`.
  User visual verification requested; no optical acceptance claimed yet.

## Rendering path

Clear on each main dock opts into `DokyLiquidGlassSurface`. Other appearances and all Home/utility/stack/action surfaces keep their existing renderer. Pre-existing Home work is preserved.

Windows Graphics Capture supplies BGRA GPU monitor surfaces. `DesktopCaptureSource` requests the monitor associated with that dock HWND, uses two pooled frames, disables cursor capture, and saves/restores the dock's display affinity. `WDA_EXCLUDEFROMCAPTURE` prevents recursively capturing the dock itself. Windows' capture indicator is left enabled; no privacy permission or OS setting is changed.

Win2D `PixelShaderEffect` samples those pixels at displaced coordinates and draws only the dock host rectangle to a `CanvasSwapChainPanel` below the icons. The shader uses a rounded-rectangle signed distance with the existing C2 hover-wave profile to estimate inward normals. Displacement increases from 2.5% of the configured strength in the center to full strength at the rim. Five nearby texture samples provide slight diffusion. Luminance-dependent tint and upper-facing specular lighting remain restrained.

The final antialiased clip borrows `DesktopGlassBackdrop.DockGeometry`, the very same physical-pixel CanvasGeometry used by the existing dock body/rim. No second path generator, input region or icon tree is introduced. Only after a successful GPU draw is the native backdrop mask suppressed. Failure hides the shader layer, restores native Clear, and explicitly reports `Liquid unavailable ... native Clear fallback, no refraction` through the existing rendering-status path and startup diagnostics.

Monitor-local coordinates come from `GetWindowRect - MONITORINFO.Monitor`, never work-area origins. Source and destination remain one physical pixel per pixel; the panel converts the host size to XAML DIPs. Monitor/resolution mismatches are rejected rather than stretched. Monitor changes stop the old capture before creating its replacement.

## Verified API constraints

- Composition supports only a subset of Win2D effects. [DisplacementMapEffect](https://microsoft.github.io/Win2D/WinUI3/html/N_Microsoft_Graphics_Canvas_Effects.htm) and [PixelShaderEffect](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Effects_PixelShaderEffect.htm) are not supported directly in Composition. The existing `RefractionLayer` still truthfully reports native backdrop refraction unsupported.
- [Win2D swap chains](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/using-win2d-without-built-in-controls) allow GPU drawing without an always-running CanvasAnimatedControl.
- [Monitor capture and exclusion](https://blogs.windows.com/windowsdeveloper/2019/09/16/new-ways-to-do-screen-capture/) provide the actual desktop source. Exclusion means the Clear dock may disappear from screen recordings/screenshots. This prototype retains the system capture border.
- This is tested against the repository's Windows App SDK 2.5.1, Win2D 1.4.0, .NET 10 and Windows SDK 26100 references. It does not add a package dependency.
- Protected content, remote sessions, unsupported capture adapters and device removal can prevent capture. Failure is explicit; native Clear fallback is not described as refraction. HDR/color-managed capture and cross-adapter performance remain unverified.

## Lifetime and performance

No CPU screenshot/readback loop, no new timer and no unbounded task queue. Capture events and existing dock geometry updates coalesce to at most one pending UI draw. Capture requests a 60 Hz minimum update interval where Windows supports that property. Hover can redraw the retained frame without waiting for a desktop change.

Only one frame of the two-buffer capture pool is retained. It stays alive for GPU surface reuse until replacement; old bitmap/frame wrappers are disposed. Shader and swap chain are reused, resized only when the host pixel size changes. Capture stops when collapsed, when leaving Clear and on shutdown. A failed activation is not retried every frame; leaving/re-entering Clear or collapse/reopen permits another attempt. Device is shared with Win2D and is not disposed by the surface.

The monitor-sized capture pool is the largest GPU allocation: approximately 16 MiB for two 1080p BGRA frames or 63 MiB at 4K, plus driver allocations and dock-sized buffers. Multiple monitors use one session per visible Clear dock. Sustained GPU/CPU cost and 120 Hz behavior still require measurement; build success does not establish a performance guarantee.

## Shader build

`scripts/Build-LiquidShader.ps1` compiles the checked-in HLSL with the installed Windows SDK fxc compiler. The generated `LiquidGlass.bin` is copied to build/publish output by the app project. Rerun the script whenever HLSL changes. This is shader bytecode only; no private assets or captures are stored.

## Validation

- Debug build: zero warnings/errors. Full Debug suite: 360/360 passed.
- Release build: zero warnings/errors. Core: 212/212. Initial Windows Release run: 145/148; three native interaction-region fixture failures occurred while the live dock was being exercised. These remain separate from the passing Debug suite and need an isolated rerun.
- Current Debug executable was launched and its path verified. User reported `it works` after being asked to verify visible rim bending with center alignment. This is user-confirmed displacement on this PC, not a claim of reference-image parity.
- Theme switching, collapse/reopen, Pin Dock, second monitor and DPI checks are tracked in the task report. GPU timing, long soak and 100/125/150/200% visual checks are not yet certified.

Executable: `C:\Dev\GlassDock\src\GlassDock.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\GlassDock.App.exe`.

Final follow-up: the four native interaction-region tests passed in isolation, then the full Release Windows suite passed 148/148. Combined with 212/212 Core, Release totals 360/360; the earlier interactive failure is retained above for transparency. The user also confirmed Dark → Clear → Acrylic → Clear, hover, collapse/reopen and Pin Dock work. The answer did not identify specific second-monitor or DPI configurations, so those remain unverified.

## Files in this pass

- src/GlassDock.Core/Materials/LiquidGlassMaterial.cs
- src/GlassDock.Windows/Desktop/DesktopCaptureSource.cs
- src/GlassDock.App/Rendering/DokyLiquidGlassSurface.cs
- src/GlassDock.App/Rendering/Shaders/LiquidGlass.hlsl
- src/GlassDock.App/Rendering/Shaders/LiquidGlass.bin
- src/GlassDock.App/Rendering/DesktopGlassBackdrop.cs (borrow existing geometry; suppress native output only after a successful liquid frame)
- src/GlassDock.App/Desktop/DesktopOverlayWindow.cs (dock-only lifecycle wiring)
- src/GlassDock.App/GlassDock.App.csproj (shader output asset)
- tests/GlassDock.Core.Tests/LiquidGlassMaterialTests.cs
- scripts/Build-LiquidShader.ps1
- docs/LIQUID_GLASS.md


Whole-process observation after the user checks: 37.5 seconds elapsed, 5.09 CPU seconds, private memory 182.0 MiB (+4.3 MiB), handles 1828 → 1836. This includes all Doky services and user activity; it is not an isolated renderer benchmark or a long-term leak test. GPU utilization was not measured.
