# Doky window previews

Hovering a running dock application for 300 ms opens its existing HWND group.
The coordinator provides a 400 ms leave grace period and retains the dock hold
through the closing animation. Reorder, external drag, utility popups and shutdown
prevent conflicting preview entry. Existing application identity/grouping is reused.

`WindowPreviewWindow` owns the retained card chrome and shared dock material.
`WindowThumbnail` owns native DWM thumbnail registration and cleanup.
`WindowPreviewSession` owns selection and lifecycle; `WindowPreviewLayout` owns
DIP layout and physical monitor clamping. The desktop focus/mirror path is retained.

One window gets a larger card; groups use a horizontal grid with at most two rows,
then pagination. Clicking a card activates that exact window. The separate top-right
close button requests normal `WM_CLOSE`, allowing the source application to prompt
for unsaved work. Nothing terminates the application process directly.
Activation removes the preview and desktop peek layers before requesting foreground.
Windows can reject restoring elevated applications from the normal dock with
`ERROR_ACCESS_DENIED`. Preview activation then requests restore/foreground through
the existing protected input helper, which validates HWND/PID/process lifetime and
desktop session. Only one restore request can be in flight; stale commands expire
after one second and the caller times out after two seconds. Existing keyboard
state/events are unchanged. Older clients ignore the optional capability, and
older helpers yield an explicit activation failure instead of a false success.

DWM supplies live visible-window pixels without a screenshot loop. Minimized
windows use an existing bounded last-visible frame when available, otherwise DWM
or an explicit unavailable label. They are never restored just to obtain a preview.
A cached minimized image is not live. A window minimized before any frame was
cached may have no useful image. Protected or unsupported sources can be blank.
Frames now preserve native resolution up to 1920x1080, retaining the 24 MiB total
budget and single capture limit. Larger frames evict sooner. Sources larger than
Full HD are still scaled; their full-size mirrors can be softer. No source is
restored just to refresh a cached image. Cards release an evicted cached bitmap.
The cache also remembers last displayed physical bounds during existing
reconciliation, so minimized snapped/maximized mirrors do not use a different
normal restored rectangle. Without a prior visible observation, placement falls
back to Windows' restored bounds.

Appearance updates reuse `UtilityPopupStyle`, `UtilityPopupTheme`, `GlassSurface`
and `DesktopGlassBackdrop`. Native setup precedes backdrop attachment. Dark,
Light, Frosted, Acrylic and Clear use the dock's existing settings. Animation
uses the Windows animation-enabled preference and stops its timer when settled.

## Validation

Debug build and repository Release validation pass with zero warnings/errors.
Core: 159 passed; Windows: 94 passed; total: 253 passed, zero failed/skipped.
Tests cover layout wrapping, monitor/DPI clamping, floating/pinned positioning,
stale selection, native DWM cleanup, zero-size filtering and safe minimized cache
reads, alongside the existing grouping, activation and lifecycle suite.
The native suite also verifies last displayed maximized bounds survive minimization.

Runtime visual verification is still required. The normal Debug dock was launched;
its watchdog remains active and reports the taskbar hidden and disabled. The UI
bridge cannot target the native dock overlay. Check actual live thumbnails,
icon-to-popup travel, close/activation, minimized/maximized sources, rapid app
switching, Pin Dock and all five appearance modes locally. Native API test success
does not establish that the real layered WinUI popup renders correctly on every GPU.
The manual pass confirmed minimized positioning and elevated activation (including
bare Win/Win+Space in the helper check). The Visual Studio
activation problem was traced to a rejected restore call (error 5), rather than
failed HWND selection. The protected helper was updated and activation was
confirmed by the user. The user then reported blur only for minimized sources;
the former 960x540 cache limit was raised, pending a final visual comparison.
