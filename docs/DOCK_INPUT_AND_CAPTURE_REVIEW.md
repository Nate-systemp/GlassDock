# Dock input and capture review — 2026-09-19

## Findings and bounded changes

- Yellow flashes originate in `WindowFrameCache`: one-shot Windows Graphics Capture sessions start with the default capture indicator. The repeated 8/45-second refresh policy makes the indicator transient. No borderless consent/capability flow exists. This is not the white desktop-focus outline.
- Expanded dock input previously used the complete transparent 640x144 host region. The App samples its existing animated wave path and passes the outline to the native host as an input-only HRGN. The expanded HWND has no SetWindowRgn clipping, preserving the drawable shadow/glow area. WM_NCHITTEST handles the silhouette; WS_EX_LAYERED plus WS_EX_TRANSPARENT lets outside input reach other processes. HTTRANSPARENT alone is only documented to traverse same-thread windows. A 50ms native timer runs only during expansion/expanded/collapse to reacquire hover when the HWND is transparent. Idle/peek now uses the same input-only hit-test path, including its small downward click extension, so the pill's composition shadow/glow is not clipped. No global input hook was added.
- One-shot capture uses one full-size frame-pool buffer instead of two. This avoids one BGRA source-sized GPU buffer (about 7.9 MiB at 1080p or 31.6 MiB at 4K), not a guaranteed equal working-set reduction. Retained frame resolution, budget, minimized-frame retention and activation paths are unchanged.
- Backdrop mask construction now skips identical geometry inputs. Opacity is still updated independently; disconnect invalidates the cached geometry signature. Existing CanvasGeometry disposal remains in place. CompositionPath is not IDisposable, so no manual release workaround was introduced.

## Capture mitigation and remaining border cases

The app stays unpackaged, as requested. There are no periodic 8/45-second WGC refreshes anymore. Each window identity is primed once when first observed eligible and visible. Valid frames are reused regardless of age. Failed or evicted frames are retried only on an explicit preview request while the real window is visible, with a 30-second per-window retry cooldown. Eviction does not trigger automatic recapture. Minimized sources are never restored or captured on demand.

Remaining flashes: initial priming of a newly seen visible window; first visible appearance of a window initially seen minimized; a visible-window preview request whose cached frame is absent after failure/eviction (subject to cooldown). Restarting GlassDock rebuilds the in-memory cache. Other capture applications can also display their own indicator. Successful valid frames cause no additional WGC sessions from tracking or repeated preview hover.

The deliberate tradeoff is stale cached content: the retained image can predate the final visible contents at minimization. A window minimized before priming succeeds, or whose sole frame was evicted, cannot be guaranteed a cached image. The native DWM path remains available. Existing 24 MiB/16-frame bounds and 960x540 retained resolution remain unchanged.

Minimize-start notifications are not a reliable sole capture trigger: asynchronous event delivery and GPU frame arrival can occur after minimization. PrintWindow delegates rendering to the target app and may block; success does not prove that GPU/DirectComposition child content was captured. DWM thumbnails do not provide a documented CPU snapshot/readback API. No undocumented DWM API, forced restore, screen scrape, or blocking PrintWindow worker was introduced.

Microsoft documents package capability `graphicsCaptureWithoutBorder` plus user consent before disabling WGC's indicator. Setting IsBorderRequired alone is not a supported fix. Package identity was not added.

Source: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired

## Validation

- Full Release build: zero warnings/errors.
- `scripts/Validate.ps1`: restore/build pass; Core tests 81 pass, one existing WindowsKeyGestureTests failure at line 49.
- Windows tests: 31 pass, one existing DesktopWindowHighlightTests reflection NullReferenceException (now line 267).
- Native input regression covers wave/base inclusion, transparent top/side/bottom exclusion, negative desktop coordinates, absence of expanded visual clipping, WS_EX_TRANSPARENT transitions and 20 expanded/idle cycles on the same HWND.
- Measured with the same real WGC source and 18-second tracking workload: before mitigation 3 sessions, after mitigation 1 session. SessionsStarted and wgc-session-started local diagnostic events count actual StartCapture calls. This is a measured single-source workload, not an extrapolated all-desktop benchmark.
- Existing last-visible capture -> minimize -> cached mirror test passes with the one-buffer pool, including keeping the source iconic and foreground unchanged.
- No taskbar, recovery, keyboard, search, launch or source-window state code changed.

## Still requires live verification

A rebuilt app was launched. The computer-use helper could list its window but twice rejected the owner binding when capturing it; manual hover/click-through and visual shadow/glow checks remain unverified. The user could not confirm those checks. No claim of complete visual parity or a long-duration leak-free result is made. Short process samples are recorded separately in artifacts/capture-idle-memory-samples.json; they do not cover repeated Home/search/preview interaction. Native tests verify the expanded window has no clipping region, but cannot substitute for visually checking layered-window rendering on the user's GPU.

The five runtime samples span approximately 60 seconds: working set rose from 217.62 to 228.34 MiB; private bytes rose from 149.25 to 159.89 MiB; handles were 1504 to 1530. This does not establish continuous retention versus ordinary allocations before GC, but it does not establish a stable plateau either. The no-continuous-growth criterion remains open. No forced collection or working-set trimming was used. The user exited the test app normally; the final full solution build then passed with zero warnings/errors.
