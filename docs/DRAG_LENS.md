# Internal dock drag lens

The existing pointer-captured reorder gesture starts the lens after its drag threshold.
Insertion slots, the stack dwell state machine, and external OLE drops retain their
existing ownership. The lens does not accept input or create another window.

`DokyLiquidGlassSurface` shares the dock's monitor capture and `LiquidGlass` shader.
During a gesture, `DockDragLensScene` uploads native icon artwork once and draws the
visible app row, running indicators, and badges into a retained GPU render target.
The captured desktop excludes Doky, so this explicit app-content pass is required.
The lens is presented through a premultiplied `CompositionDrawingSurface` in the
XAML tree. It must not use a full-host `CanvasSwapChainPanel` above the app row:
WinUI 3's swap-chain presentation does not support transparency to underlying XAML,
so it removes the normal icons even where the lens shader outputs transparent pixels.
The sampled copies inside the capsule remain visible, explaining the reported
disappearance during dragging and return on release. The main dock's existing
swap-chain surface stays below the app row and is unchanged.

Only the dragged icon is excluded; its original XAML content is temporarily lifted
into an unclipped, non-interactive root canvas. Running unpinned apps are included.

Hover magnification is settled immediately before measuring the scene. Otherwise
cached geometry retains the old enlarged/lifted icon bounds while the live buttons
settle back, and the opaque refracted output covers icons with misaligned artwork.
A one-shot diagnostic texture confirmed icons were present in the input while
button bounds were still approximately 55 x 60 DIP instead of 40 x 44 DIP.
Temporary texture-export code was removed after this investigation.

Pointer movement updates retained transforms and shader parameters. Following uses
a bounded 22 ms exponential time constant, with 45 ms shape following, a 120 ms
entrance, and a 160 ms smoothstep exit. The existing stack-ready state widens the
lens; it does not change the dwell threshold or create a stack independently.
Reduced-motion settings skip the transitions.

Completion/cancellation restores the original content and disposes the scene's
bitmaps, text layouts, render target, shader, composition surface, brush, visual, and
graphics-device wrapper. The shared Canvas device is borrowed. Rendering subscriptions
are detached. Clear keeps its existing shared capture; other modes release capture
at the end of the gesture. Settings/monitor retargeting and shutdown abort the lens.
Capture failure retains ordinary reorder and the native material fallback.

## Runtime validation

The user confirmed the lifted pill is fully visible and dragging works. They then
reported disappearing neighboring icons. Geometry changes did not resolve that
regression. After changing the lens presentation to a composition drawing surface,
the user confirmed the icons are visible during dragging. The verified Debug
process was PID 29680 from the repository's `bin/Debug` output.

Debug and Release builds completed with zero warnings/errors; both configurations
passed 363 tests (215 Core, 148 Windows), including `scripts/Validate.ps1`.
The broader 20-drag, multi-monitor, all-appearance, cancellation, stack/drop, and
external-file-drop matrix has not been individually confirmed for this revision.
Automated tests do not establish visual correctness or absence of GPU resource leaks.
Windows' capture indicator remains enabled during capture.

Platform constraint: [Microsoft SwapChainPanel remarks](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swapchainpanel?view=windows-app-sdk-2.0#remarks).
