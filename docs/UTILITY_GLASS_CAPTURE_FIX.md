# Utility material and Snipping Tool recovery

The reported capture path is Win+Shift+S / Snipping Tool. Changes are confined to utility material parity and the existing capture/foreground lifecycle.

## Material causes and changes

The main dock's native `GlassEffectGraph` receives normalized material values and compositor-pixel blur. Utility rendering multiplied that blur by the layout DPI and omitted `LiquidGlassMaterial.SurfaceOpacity`, making Frosted/Acrylic OFF surfaces diverge at scaled DPI or customized opacity. The existing white ON-state fill is intentional selection styling and is preserved.

For OFF surfaces, `UtilityGlassSurface` uses the same normalized material, `DockMaterialRendering.BaseBlur` branch, tint, saturation, brightness, contrast, quality and material blend, with the same surface opacity in Frosted/Acrylic. The OFF tint overlay is fully transparent, including hover/press, so a hard-coded white interaction fill cannot wash out the dock material. Utility bodies retain the existing shared monitor capture and effect graph; no independent capture session or rendering system is introduced. The original ON-state fill, accent icons, sheen, blur scaling and opacity are restored. All geometry and Clear/Dark/Light state styling remain unchanged.

## Disappearance causes and changes

- `snippingPending` suspended the display timer, but did not guard auto-collapse, pointer transitions or an already-running expansion/collapse. The dock could hide its utilities during selection and wait for subsequent input to restore them. Screenshot preparation now cancels hide delays, holds an in-flight transition using the existing animation mechanism, and suppresses pointer-driven changes until capture ends. Normal behavior resumes afterward.
- Native foreground handling classified Snipping Tool's monitor-covering overlay as a fullscreen application and could hide/lower the dock independently of the UI timer. It now recognizes the same screenshot processes already used by popup windows, prepares capture when their fullscreen overlay appears, and leaves the selection overlay above the dock without activating or repeatedly raising the dock.
- The screenshot path disposed its only clean backdrop bitmap when stopping WGC, leaving only the presented output across display-affinity changes. It now retains an owned GPU-only copy and can re-present utility/dock output at both boundaries without sampling the screenshot-visible window. No CPU screenshot readback is introduced.
- Completion explicitly restores fullscreen policy, utility positioning and renderer invalidation. If capture stopped before freezing succeeded, the completion path can restart it without a dock click. Repeated screenshot notifications are idempotent and cannot briefly restart sampling between two freeze requests.
- The existing shortcut observer also recognizes Print Screen without consuming it. Both request and Escape cancellation use the correct keyboard-service revision.

The capture safety order remains: stop the monitor frame pool before changing `WDA_EXCLUDEFROMCAPTURE` to screenshot-visible, and restore exclusion before restarting the pool. Utility popup windows are re-excluded before the shared dock capture resumes. The owned clean GPU copy is released by the next real frame or normal shutdown.

## Validation and limits

`dotnet build GlassDock.sln -c Debug --no-restore` passed after the final source changes with 0 warnings and 0 errors. `git diff --check` passed. No automated tests or runtime capture actions were run, following the user's manual-verification preference. The existing screenshot ordering fixture was corrected to reference the actual `var frozen = ...` call rather than its obsolete direct-return spelling.

The later ON-state restoration/OFF-only tint correction was source-reviewed and passed `git diff --check`; it was not rebuilt or run.

## Screenshot follow-up: actual backdrop parity

The supplied screenshot still shows a flat pale utility while the dock retains the wallpaper's warm color. The OFF overlay is already transparent and default Button hover/pressed brushes are already suppressed, so removing another color constant does not address the remaining difference: the dock is rendered by its native system backdrop, while the utility is separately drawn from WGC pixels.

Frosted/Acrylic OFF utilities now contribute their rounded contours to the main `DesktopGlassBackdrop` coverage mask. They sample the very same native backdrop brush and material effect instance as the dock, including tint, blur, saturation, material blend and surface opacity. Their separate bitmap output is hidden and excluded from capture work while the native path is available. ON styling and Clear/Dark/Light remain on their existing paths. Identical material does not require identical RGB at different locations on a nonuniform wallpaper.

The main dock outline, wave, drag-lens geometry and presentation transform remain separate from the utility mask contours. Existing utility fades, Wi-Fi capsule width changes and Settings morph scale are mirrored into mask coverage; rendering callbacks are bounded to these animations and detached afterward. Removed utilities retain their fading mask until completion. Native mask resources are retained and disposed with the existing backdrop connection.

This follow-up is source-reviewed only. No build, test or runtime action was performed. Manual verification should compare OFF Wi-Fi/Bluetooth with the dock in Frosted/Acrylic, then check ON restoration, hover/press, fade in/out, capsule expansion, Settings morph, DPI changes and screenshot capture.

Manual acceptance:

1. Compare every utility in Frosted/Acrylic at 100%, 125%, 150% and 200% DPI, with radio states ON/OFF and edited blur, saturation, material blend and surface opacity. Check Clear/Dark/Light for unchanged styling.
2. With the dock expanded, press Win+Shift+S and move the pointer away. Capture, Escape-cancel and repeat rapidly; utilities should remain visible during selection and recover without clicking. Repeat during expansion/collapse and with Wi-Fi/Bluetooth panels open.
3. Repeat with Snipping Tool launched directly, Print Screen, clipboard completion and the 45-second cancellation fallback. Check multiple monitors, pinned/unpinned modes, normal auto-hide afterward, and that real fullscreen applications still suppress the dock.
4. Inspect captured pixels for recursive glass, blank surfaces, z-order changes and stale/dimmed frame artifacts.

`WDA_EXCLUDEFROMCAPTURE` remains necessary outside recognized screenshot sessions to keep the shared monitor capture from sampling Doky itself. Arbitrary continuous third-party recording/sharing is not made universally capture-visible by this change; that would require a different self-exclusion/capture architecture. No claim of live recording compatibility is made.

The shared radio-toggle transition now holds its original material during native work. On Frosted/Acrylic OFF-to-ON handoff, the native utility backdrop remains underneath until the ON surface has its first captured frame; capture remains enabled during that handoff. The animation affects symbols rather than glass geometry. The final Debug solution build passed with 0 warnings and 0 errors; visual/hardware verification remains manual.
