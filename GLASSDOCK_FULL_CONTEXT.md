# GlassDock — Complete Current-State Audit

**Audit date:** 2026-09-20 (Asia/Manila)  
**Repository:** C:\Dev\GlassDock  
**HEAD at audit:** b373d22 (main -> origin/main)  
**Audit mode:** read-only source/config/test inspection. No source, tests, existing documentation, or project settings were changed. The only file created by this audit is this document.

This document records the earlier audit snapshot. See docs/STEP_1_STABILIZATION.md for subsequent fixes and current validation; the historical results below are retained as audit evidence. C# source, project files, generated test output, and the running executable are the authority. Older documentation is called out when it disagrees with source. A dirty working tree was present before this audit; those changes are preserved and are not attributed to this audit.

## 1. Project overview

GlassDock is a Windows desktop dock that places a floating, glass-styled dock along the bottom of the primary monitor. It discovers running windows and pinned applications, displays adaptive application icons, previews an application's open windows, and offers a retained Glass Home launcher/search surface. It is an overlay and window-management companion; it is not a replacement Windows shell and does not implement a full launcher/catalog product yet.

The normal experience is:

1. A small iOS-like home-indicator pill sits at the bottom center.
2. Pointer entry or the dock activation hotkey raises/expands a glass dock.
3. Icons magnify around the pointer and a wave follows the pointer.
4. Leaving the dock collapses it, holds the small peek pill for two seconds, then lowers the pill close to the screen edge.
5. Hovering an app with multiple windows opens a compact then expanded preview panel. Visible windows use DWM thumbnails; minimized windows use a retained frame when available.
6. Win+Space opens Glass Home in compact mode with its search editor focused.

Technologies and targets:

- C#/.NET 10 (global.json pins SDK 10.0.401).
- WinUI 3 / Windows App SDK 2.4.0, unpackaged app-local deployment.
- Win2D 1.4.0 for the composition effect graph.
- Windows target framework net10.0-windows10.0.26100.0; minimum app platform 10.0.19041.0.
- win-x64 and PlatformTarget=x64.
- Windows native interop lives principally in GlassDock.Windows.
- The current application manifest requests requireAdministrator; it does not request uiAccess.

The solution is GlassDock.sln with seven projects:

| Project | Purpose | Important dependencies/responsibilities |
|---|---|---|
| src/GlassDock.App | WinUI 3 executable and UI composition | Windows App SDK, Win2D, Core, Windows; owns dock, Glass Home, preview windows and visual composition. |
| src/GlassDock.Core | Platform-neutral state/domain contracts | No external package; dock state, application/window records, preview layout/session, search ranking. |
| src/GlassDock.Windows | Windows services and interop | Core reference; Shell/window enumeration, icons, launcher, pins, DWM, WGC, taskbar and input services. |
| src/GlassDock.Watchdog | Independent taskbar/recovery helper | Core/Windows; owns recovery lease and can restore taskbar after parent failure. |
| src/GlassDock.Licensing | Placeholder project | Core reference only; no licensing behavior is implemented. |
| tests/GlassDock.Core.Tests | Core unit tests | xUnit/Test SDK. |
| tests/GlassDock.Windows.Tests | Windows/native service tests | xUnit/Test SDK, Windows target. |

The current app is unpackaged (WindowsPackageType=None) and self-contained for publish. The publish target copies the watchdog executable beneath Recovery\. The repository also contains ignored/stale publish, installer, and ZIP artifacts; these are distribution outputs, not source of truth.

## 2. Complete feature inventory

### Dock

- Expanded dock: a borderless, always-on-top overlay window managed by WindowsOverlayManager. The normal host is 640x144 DIP and the visible glass surface resizes to the calculated application width. Width is count * 40 + (count - 1) * 6 + 36, clamped to 100–560 DIP. The empty dock remains at 100 DIP.
- Collapsed dock: after pointer exit, the dock animates down to the configured bottom margin (BottomMargin=24), leaving the indicator visible.
- Peek/minimized pill: after the collapse grace period, the dock is held in a raised peek position. After PillHoldMilliseconds=2000, it animates to PeekRestBottom=-2; the indicator remains visible with a small lower input extension.
- Hover behavior: native hit-testing and a 50 ms pointer-reacquisition timer keep hover alive over transparent/layered portions. Pointer entry cancels pending collapse/peek work. Pointer movement updates icon magnification and the dock wave only while expanded.
- Click behavior: tapping the idle/peek dock requests expansion. App button activation is handled by WindowPreviewCoordinator: one window activates/restores, multiple windows open the preview panel.
- Collapse timing: expanded/expanding leave schedules a one-second collapse (ExpandedCollapseGraceMilliseconds=1000). Idle/hovering leave schedules the two-second pill hold. Existing delay tokens are canceled when pointer re-enters or interaction is held.
- Pill raise/lower behavior: DockAnimationController.AnimateBottom moves the host; PeekRestBottom is approximately three DIP visible at the physical lower edge. Input polygon geometry is updated with placement.
- Icon layout: app buttons are 40x44 DIP with 6 DIP spacing. Adaptive icons render at 28 DIP with hover scale up to 1.24. The icon service preserves aspect ratio and uses high-resolution Shell/executable sources where available.
- Neighbor magnification: a Gaussian-like distance curve (sigma=52) produces a maximum 1.24 scale, smoothed at 0.34 per 16 ms tick. Magnification is stopped once settled.
- Dock wave: DesktopOverlayWindow samples the current animated pointer position and calls WindowsOverlayManager.SetInteractionPolygon plus DesktopGlassBackdrop.SetWave. The top edge is deformed around the pointer; legacy SetDockBump/ClearDockBump is not used.
- First/last icon behavior: the target width is based on all currently synchronized app items and is bounded at 560 DIP. Icons remain centered in the glass surface; no separate first/last special-case is present beyond the normal width clamp.
- Running/active indicators: each app button displays a small running indicator; active application state is supplied by DockApplicationsViewModel/WindowsApplicationService.
- Glass appearance: GlassSurface and DesktopGlassBackdrop provide blur, tint, saturation, opacity, edge lighting and shadow. The expanded surface uses a larger radius/shadow; the peek indicator uses a small radius.
- Shadows/glow: the host is not clipped to the polygon. Hit-testing is input-only, so the outer shadow/glow can paint outside the interactive silhouette.
- Context menus: the actual app-icon menu is built by WindowPreviewCoordinator. A dock-level menu is built in DesktopOverlayWindow and currently exposes only the commands listed below.
- Pinning: pin/unpin is implemented through WindowsApplicationService and DockPinStore; the pin file is under %LOCALAPPDATA%\GlassDock\dock-pins.json.
- Dock locking/removal/adding: no lock-dock, add-application, remove-app or appearance-settings command is present in the current dock menu. Do not infer these from older plans.

### Hotkeys and keyboard hooks

- WindowsKeyboardService installs a process-lifetime WH_KEYBOARD_LL hook.
- A bare left or right Windows key release is recognized and posts HomeRequested, which toggles the dock through the normal expansion/collapse path. The hook injects a tagged neutral menu-mask key/release batch to suppress Start when possible.
- Win+Space posts LauncherRequested and consumes the space portion; the app opens Glass Home. Existing modifier gestures (Win+R/E/L/D/Tab and similar) pass through where the recognizer permits.
- Ctrl+Alt+Space is registered as a dock hotkey. Ctrl+Alt+F12 is the emergency recovery hotkey and requests taskbar restoration.
- Ctrl+Alt+Delete/secure desktops are not intercepted by this user-mode hook.
- Older docs still say bare-Win interception is deferred; current source and the later taskbar/keyboard follow-up are the current behavior.

### Glass Home

- A retained WinUI window (GlassHomeWindow) is shown/hidden rather than recreated.
- Win+Space opens compact mode, clears the previous query, activates the window, calls SetForegroundWindow, and repeatedly requests focus through a short dispatcher/timer retry. The search TextBox receives FocusState.Keyboard; it is not focused only from Loaded.
- Compact height is approximately 78 DIP; pointer motion of about 30 physical pixels latches the session expanded. The Home window uses the primary monitor work area only.
- Search combines installed Shell/Start-menu applications with a nine-entry Windows Settings catalog. GlassSearch ranks exact, prefix, word-boundary and substring matches, deduplicates stable identities, and caps displayed results at eight.
- The application index starts once on first retained Home use. Metadata is discovered on an STA worker; icons load progressively and are cached in memory. Search does not implement web search, recent files, pinned launcher groups, power actions or Task View.
- Escape/outside click hides Home. A successful ShellExecute launch hides the current Home session after revision checking; launch errors leave Home visible.
- Home uses the same glass material direction as the dock and disables its own DWM border where supported. It is not a separate packaged Windows shell.

### Window previews

- One-window app buttons activate/restore through WindowsApplicationService.ActivateWindow on click.
- Multi-window app buttons create a WindowPreviewSession and WindowPreviewWindow. The panel begins compact, expands with a visible animation, and only enables desktop focus emphasis when expansion reaches progress 1.
- Visible targets use DwmRegisterThumbnail/DwmUpdateThumbnailProperties. Minimized targets are never restored for hover; DesktopWindowFocus renders a retained cached frame when available and positions it using the target's restored bounds.
- WindowFrameCache uses a one-shot WGC session to retain a last valid BGRA frame. It does not periodically capture every window. Capture is primed once while a visible eligible window is tracked; an explicit preview demand may retry a missing visible frame after a cooldown.
- A preview card has a minimal close button. It sends WM_CLOSE through WindowsApplicationService.CloseWindow, does not activate the target and refreshes the session after close. The current close-button placement is at the card's upper-left overlay coordinates despite the intended top-right design; this is a confirmed current implementation detail.
- Page navigation is available when more windows exist than fit. WindowPreviewLayout calculates capacity from available width rather than hard-coding four.

### Taskbar

- The watchdog child reads and journals taskbar auto-hide state, clears ABS_AUTOHIDE, hides/disables the detected taskbar window, and restores the journaled state on normal exit, initialization failure, parent/watchdog loss or the independent restore command.
- No code kills or restarts Explorer.exe.
- Current source behavior: DesktopOverlayWindow.root.Loaded calls StartTaskbarTestAsync(whileAppActive: true) unconditionally. Therefore the running app currently owns an active taskbar suppression lease, even though older README/architecture/roadmap text describes taskbar suppression as opt-in/development-only.

### Application management

- WindowsApplicationService listens to WinEvent foreground/create/show/hide/location events and reconciles eligible top-level windows on a worker STA, coalescing refreshes up to roughly three seconds.
- Shell/AppUserModelId/executable resolution supplies display names and icons. Shell/taskbar pins are imported; user pins and exclusions are persisted atomically in dock-pins.json.
- Existing app activation restores minimized windows and uses SetForegroundWindow. A pinned app can be launched with ShellExecuteEx when no eligible window exists.
- Run-as-administrator and open-file-location operations are available from the app context menu. There is no startup registration, updater, telemetry, networking, payment or licensing implementation.

### Actual context menus

#### A. App icon context menu

WindowPreviewCoordinator.BuildMenu creates this menu for a dock application:

| Command | Visibility/enabled state | Handler/effect |
|---|---|---|
| Open / Activate | Always for a valid item; wording depends on whether an eligible running window exists | WindowsApplicationService.LaunchOrActivate; existing window is restored/activated, otherwise a pinned item may launch. |
| New window | Visible only while running and a launcher target is available | WindowsApplicationLauncher.Target/Shell launch. |
| Show All Windows | Visible for a running app | Defers/reopens the preview session for the current application. |
| Individual window entries | Visible when more than one window is present | Activates the selected handle through the existing preview/session path. |
| Close Window / Close All Windows | Singular when one window, submenu/action when multiple | WindowsApplicationService.CloseWindow sends WM_CLOSE; no forced termination. |
| Pin / Unpin | Always for a valid app identity | Atomic DockPinStore update, then service refresh. |
| Run as Administrator | Visible when an executable target is known | ShellExecuteEx with runas; UAC cancellation is reported as a failed action. |
| Open File Location | Visible when a valid executable or link target is known | Explorer /select launch. |

The coordinator holds dock/preview interaction while a menu or submenu is open, snapshots the application/window identity, and revalidates membership on close.

#### B. Dock/empty-area/pill context menu

DesktopOverlayWindow.BuildMenu currently exposes:

- Dock Settings → opens the retained product SettingsWindow for bottom spacing and dock hide/peek delays.
- Open Glass Home → opens Home.
- Restore Windows taskbar → calls RestoreTaskbar immediately.
- Glass Material Laboratory → opens the material laboratory window.
- Exit GlassDock → closes the app and performs recovery.

There are no current menu entries for Add Application, Lock Dock, dock size, glass appearance editing, Restart, or app removal. DevelopmentWindow remains available through the explicit --controls developer path.

## 3. Dock state machine

### States

DockStateMachine defines Hidden, Idle, Hovering, Expanding, Expanded, and Collapsing. The overlay starts visually in Idle with the indicator at the peek-rest position. The dock's animation controller has independent bottom/width/height/icon progress values, so visual progress and logical state are deliberately separate.

### Actual transition diagram

~~~text
Hidden (only during shutdown/initial construction)
   │ Show
   ▼
Idle / raised indicator
   │ pointer enters, tap, bare Win, Ctrl+Alt+Space
   ▼
Hovering ── request expansion ──► Expanding ── animation complete ──► Expanded
   ▲                                  │                                  │
   │ pointer re-enters/cancel         │ leave + one-second grace          │ leave + one-second grace
   │                                  ▼                                  ▼
   └────────────── Collapsing ◄───────┴────────────────────────────────────┘
                                      │ collapse complete
                                      ▼
                                 Idle / pill raised
                                      │ two-second hold expires
                                      ▼
                                 Idle / peek lowered (-2)
                                      │ pointer enters or tap
                                      └──────────────► Hovering/Expanding
~~~

### Trigger and cancellation details

- root.PointerEntered cancels pending collapse and pill-hide delays, reacquires native input and raises the peek/hover state. Placement animation callbacks are ignored so a moving window cannot falsely force expansion.
- root.PointerMoved samples native screen coordinates. While expanded it updates magnification and the wave; while idle/peek it can request a raised peek.
- root.PointerExited uses native pointer state to distinguish a real leave from transparent-window routing. Expanded/expanding leaves schedule ScheduleCollapse after one second. Idle/hovering leaves schedule SchedulePillHide after two seconds.
- A tap in idle/peek calls ToggleDockAsync; the same method is used by the bare-Win callback. Expanded/expanding bare-Win toggles through the collapse path.
- A context-menu or preview hold calls DockStateMachine.HoldTransition, freezes animation values and suppresses dock keyboard toggles. Release calls ResumeAfterHold; pointer-inside state determines whether the dock returns to hover or expanded.
- The native input polygon is updated from the animated wave and from the peek rectangle. In peek it includes a small lower clickable extension while preserving the visual pill's position.
- Outside the input polygon the layered overlay returns HTTRANSPARENT/uses WS_EX_TRANSPARENT; applications above it continue to receive input. The host has no clipping region, so its visual shadow remains drawable.

Differences from older intended flow: the indicator is not faded away after the two-second delay; it remains a very low pill. The current source also starts taskbar suppression automatically on load rather than waiting for a user-started development test.

## 4. Context-menu and interaction-hold system

Right-click on an app button is handled by WindowPreviewCoordinator:

1. It increments its open-menu identity set, cancels pending preview show/hide work and calls BeginContextMenu.
2. The coordinator hides the preview and raises HoldChanged. DesktopOverlayWindow.OnInteractionHoldChanged sets keyboard.SuppressDockToggle=true, calls state.HoldTransition(), freezes DockAnimationController values and keeps the native pointer/input geometry alive.
3. Pointer leave and collapse timers are ignored while the hold is active. A bare Win arriving while the menu remains open is suppressed; it cannot collapse the dock.
4. Menu/submenu commands operate on a snapshot of the app/window identity. If reconciliation changes membership while a menu is open, stale entries are dismissed or ignored.
5. On Closed, snapshot entries are removed, EndContextMenu is deferred one dispatcher turn, and hold is released only if no menu/submenu remains. The dock resumes the frozen transition and reopens a pending Show All request against current state.

Preview hover holds and context-menu holds share the HoldChanged event but are tracked separately. The actual menu state does not rely on a global openMenus integer; coordinator identity sets and ContextMenuOpen provide the guard. Correction from Step 1 source reinspection: OnInteractionHoldChanged contains one guarded refresh/enqueue block. The earlier duplicate-block claim was incorrect.

The requested regression scenario is covered by source behavior: dock open → right-click → menu held → press Win leaves the dock held open; menu open → pointer leaves also remains held until the menu closes. Physical shell-level verification is not claimed because the current app is elevated and UI automation cannot bind reliably.

## 5. Hit-testing and input architecture

WindowsOverlayManager owns the dock overlay HWND. It is borderless, topmost, layered and transparent to the desktop outside the active polygon. WM_NCHITTEST reads signed 16-bit screen coordinates from lParam, tests the current input polygon, and returns HTCLIENT inside or HTTRANSPARENT outside. A timer (InputTimer, 50 ms while active) reacquires the pointer because layered/transparent windows can miss ordinary XAML pointer events.

The polygon is input-only. SetInteractionPolygon creates/deletes a GDI polygon for hit tests and maintains a native pointer reacquisition region; it does not call SetWindowRgn for rendering. A separate SetInteractionRegion round-region helper exists, but the current app uses the polygon path and removes any window region. Therefore the glass shadow/glow is not visibly clipped. The peek polygon is centered at the bottom, approximately 120 DIP wide with a five-DIP visible band and an eight-DIP lower clickable extension.

WS_EX_LAYERED, WS_EX_TRANSPARENT transitions and the no-activate style let clicks pass through outside the dock. Preview windows have their own hit-testing/placement host; context menus are normal menu popups and temporarily hold dock interaction. DesktopWindowFocus creates separate click-through dim and mirror windows; they return HTTRANSPARENT and MA_NOACTIVATE so they never steal focus from the real target.

No visual SetWindowRgn clipping is active in the current source. Native tests verify that the expanded host has no clipping region and that the polygon includes the intended wave/base and peek bottom row.

## 6. Animation system

| Animation | Responsible code | Cadence/easing/duration | Cancellation/idle behavior |
|---|---|---|---|
| Dock width/height/opacity expansion | DockAnimationController.AnimateAsync | Explicit WinUI storyboard; width/height/surface about 320 ms; icons about 380 ms; cubic/keyframe easing where available. | New direction cancels/replaces the prior storyboard; controller keeps current values. |
| Dock collapse | Same controller | Width about 300 ms, height/surface about 280–300 ms, icons about 220 ms, indicator about 300 ms. | Pointer re-entry or hold freezes/cancels without resetting to black/zero. |
| Bottom raise/lower | AnimateBottom/placementTimer | 16 ms tick; smooth interpolation until settled. Peek lower animation uses about 210 ms. | Stops when within epsilon; delayed pill token is canceled on re-entry/hold. |
| Icon magnification | magnificationTimer | 16 ms; 1.24 maximum, sigma 52, smoothing .34, epsilon .0015. | Runs only while active/expanded, stops when settled. |
| Dock wave | dockWaveTimer and DesktopGlassBackdrop.SetWave | Approximately 16 ms while pointer is active; same animated pointer geometry drives backdrop and native input polygon. | Stops when dock is idle; geometry is cleared/reset during collapse. |
| Context-menu hold | HoldMagnification/FreezeCurrentTransitions | No independent visual animation; current values are held. | Resume uses pointer state after menu close. |
| Glass Home expansion | GlassHomeWindow animation timer | 16 ms, approximately 200 ms between compact and expanded height. | Timer stops when hidden/settled. |
| Glass Home focus retry | focusRetryTimer | 20 ms, up to 12 attempts after Show/Activate. | Stops after focus succeeds or attempts/limit. |
| Preview panel | WindowPreviewWindow timer | Approximately 16 ms; compact→expanded progress is eased; desktop focus is gated until progress==1. | Collapse cancels focus and hides preview; completion updates session state. |
| Preview close/focus fade | WindowPreviewWindow selection animation | Card emphasis/dimming is interpolated; desktop focus hide is delayed about 180 ms to avoid flicker. | New selection retargets existing state. |
| Watchdog heartbeat | DesktopOverlayWindow heartbeat | 1 s parent/watchdog lease check; child protocol heartbeat is about 5 s. | Stops/disposes on close; watchdog restores after lease failure. |

The dock animation timers are not permanently high-frequency while idle. The native 50 ms input timer is active during expanded/peek/collapse interaction, not as a continuous background animation loop. Capture, app reconciliation and Home indexing run on demand/background workers rather than a forever-running UI timer.

## 7. Glass and visual rendering architecture

- GlassSurface is a XAML/Composition material control for the dock/Home/preview surface. It supplies rounded geometry, opacity, optional rim/border, edge lighting and shadow. Current dock usage disables the expanded border and uses the indicator as a simpler shape.
- DesktopGlassBackdrop is the desktop backdrop implementation. It creates a CompositionVisualSurface, a backdrop brush, and a Win2D effect graph. The graph applies Gaussian blur, saturation, exposure and tint; the mask follows a CanvasGeometry path. A fallback solid color is used when composition/COM setup fails.
- GlassEffectGraph owns the Win2D effect graph. Current default material values are roughly blur 28, opacity .88, saturation 1.15, brightness/exposure 1.08, tint near DCEAFF, border/rim alpha around .48 and radius 32, with shadow alpha .32, blur 40 and offset 16. Dock-specific calls use a softer blur around 20, expanded opacity .78/radius 34, and peek radius around 2.5.
- GlassMaterial contains presets and scalar settings. RefractionLayer is not enabled (IsSupported=false); edge lighting is used instead of a refractive fake.
- DWM integration supplies window backdrop attributes/blur and the separate DWM thumbnail path for previews. Glass Home suppresses its own DWM border where supported; unsupported attributes are ignored.
- Outline/rim is XAML/Composition geometry, not the old desktop-window highlight. DesktopWindowHighlight is intentionally a no-op. There is no current white border around the live DesktopWindowFocus mirror.
- Wave architecture: DesktopOverlayWindow samples pointer position, calculates a wave outline, calls SetDockWave on the backdrop and sends the same polygon to native hit-testing. Legacy SetDockBump and ClearDockBump are not called by current code.

The overlay window's drawable rectangle remains larger than the interactive outline. This is the mechanism that preserves the outer glass shadow/glow while fixing click-through.

## 8. Application icon system

AdaptiveAppIcon receives an ApplicationIcon pixel source, trims transparent alpha only to determine natural artwork bounds, then renders the original aspect-ratio pixels with Image.Stretch=Uniform on an unmasked Canvas. A subtle rounded glass tile/background is used behind the icon when appropriate; the transparent artwork itself is not clipped to a forced square. The tile opacity is reduced for substantially filled artwork.

WindowsApplicationIconService asks Shell for a 256-pixel image first, then tries 128/96 and executable PrivateExtractIcons fallback. It handles packaged/Shell icons, links and executable/HICON ownership. Pixels are premultiplied BGRA8; AdaptiveAppIcon writes a WriteableBitmap in the matching format and preserves alpha. The rasterizer downsamples with an area filter when a source is larger than the requested display/hover pixel size; it avoids blindly upscaling a small source.

The icon cache is bounded FIFO (default 128 entries; Home-specific requests use smaller 64/24 targets). It retains the source long enough for normal and 1.24x hover rendering and includes debug diagnostics for source/cropped/display dimensions, DPI and hover pixels. Running dock icons and Home search results reuse the service but differ in target size: the dock requests compact/high-resolution tile sizes, while Home requests 64/24-style progressive result icons.

## 9. Installed application search

- WindowsApplicationIndex starts one retained STA worker when Glass Home is first used. It enumerates Shell AppsFolder plus current-user/common Start Menu .lnk entries, avoids inaccessible/reparse paths, and publishes metadata batches.
- ShellApplicationMetadata preserves launchable Shell/shortcut identities and uses stable identity/path keys. It does not scan the full disk or Program Files.
- WindowsSettingsCatalog contains nine Settings entries with names, descriptions, aliases and ms-settings: URIs.
- GlassSearch trims the query, ranks exact=0, prefix=1, word-boundary=2 and substring=3, deduplicates applications by normalized title and Settings by stable id, and caps the result list at eight.
- Icons are loaded progressively from the shared native icon service; results remain usable before all icons arrive. Search state retains selection identity when the underlying entry is refreshed.
- WindowsApplicationLauncher uses ShellExecute/ShellExecuteEx for applications and Settings. A successful Shell acceptance is not proof that the target finished starting.
- The current live probe recorded 293 application entries, correct examples for Notepad/Calculator/File Explorer and Settings aliases, and 1000 cached queries in 169 ms on this machine. These are probe measurements, not UI latency guarantees.
- Duplicate handling is identity/title based and can leave separately registered apps with the same display name; that is deliberate rather than a hardcoded name list.

## 10. Window preview architecture

### Components

- WindowPreviewCoordinator connects dock app buttons to preview sessions, frame cache requests, context menus and activation/close commands.
- WindowPreviewWindow is the topmost borderless preview UI. It creates cards, drives compact/expanded animation, pages, close buttons, opacity dimming and selection.
- WindowPreviewSession owns ordered ApplicationWindow records, state (Hidden, Waiting, Compact, Expanding, Expanded, WindowHovered, Collapsing, Activating), selected HWND and revision.
- WindowPreviewLayout calculates card width (max 208), height (max 130), capacity from available monitor width and page geometry. It is not hard-coded to four cards.
- WindowThumbnail wraps DWM thumbnail registration/query/update. Visible thumbnails crop margins for the preview card; minimized thumbnails avoid live-crop assumptions.
- WindowFrameCache provides a last-valid CPU BGRA frame for a minimized source.
- DesktopWindowFocus presents a no-activate dim window plus a mirror window around the target's restored bounds. It never calls SetForegroundWindow on the target and does not alter target z-order.

### Visible source path

~~~text
real visible HWND
   → DwmRegisterThumbnail / DwmQueryThumbnailSourceSize
   → DwmUpdateThumbnailProperties
   → WindowThumbnail rendered in the preview card
   → DesktopWindowFocus mirror if the expanded preview card is selected
~~~

### Minimized source path

~~~text
visible eligible HWND is tracked
   → one-shot WGC session captures a valid BGRA frame
   → WindowFrameCache retains bounded frame
   → source is minimized without restoration
   → DesktopWindowFocus computes restored bounds from WINDOWPLACEMENT
   → cached frame is painted with GDI StretchDIBits/BitBlt
   → real HWND remains iconic until preview click
~~~

Current frame limits are 960x540 maximum retained dimensions, at most 16 retained frames, a 24 MiB retained-byte budget and one concurrent WGC session. One-shot frame pools are disposed after a frame; the D3D11 device is released when no capture is active.

WindowFrameCache.Track primes only visible eligible windows that do not yet have a frame. Request can retry missing visible frames on explicit preview demand, with a 30-second failed-attempt cooldown. Minimized windows are never captured on demand. Eviction preserves minimized frames where possible and prefers visible/inactive entries for removal.

DesktopWindowFocus uses GetWindowPlacement.NormalPosition plus MonitorFromWindow/GetMonitorInfo conversion for minimized targets, including negative virtual coordinates. It logs source HWND/process/minimized state, monitor/work rectangles, virtual-screen origin, final mirror rectangle, selected path and draw result. The real target is only restored/activated when the user clicks a preview through the existing activation path.

## 11. Yellow capture-border status

The visible yellow capture indicator is produced by Windows Graphics Capture when a WGC session starts with the default border/indicator behavior. It is separate from the old desktop-window highlight: DesktopWindowHighlight is currently a no-op, and no yellow color constants exist in the preview/highlight source.

Current mitigation in WindowFrameCache:

- Removed the old periodic 8/45-second refresh policy.
- Prime each visible eligible window once.
- Reuse a valid retained frame regardless of age.
- Retry only on explicit preview demand while the source is visible, with a 30-second cooldown.
- Never refresh/capture a minimized source.
- Limit sessions to one concurrent capture and retain one frame-pool buffer.

The current review documentation reports a measured single-source, 18-second workload of **3 sessions before mitigation versus 1 session after mitigation**. SessionsStarted and local wgc-session-started diagnostics count actual starts; this is not an all-desktop benchmark.

Yellow flashes can still occur when a newly observed visible window is first primed, when a window is first seen minimized before a valid frame exists, or when an explicit visible-preview retry follows failure/eviction. Restarting GlassDock rebuilds the in-memory cache. A perfect borderless result is not guaranteed in this unpackaged app: Microsoft's supported graphicsCaptureWithoutBorder capability/consent flow requires package identity, and the current app does not set GraphicsCaptureSession.IsBorderRequired=false as an unsupported workaround. The remaining tradeoff is potentially stale cached content for minimized previews.

## 12. Memory and performance optimizations

- Glass Home indexing is lazy and retained for the Home lifetime; query filtering is in-memory and capped at eight results.
- Native icon extraction is progressively loaded and bounded (dock cache default 128; Home targets/cache sizes are smaller).
- WGC retains at most 16 frames, 24 MiB and 960x540 per frame maximum, with one active session. A valid frame is reused rather than recaptured on every hover.
- One-shot capture uses one frame-pool buffer instead of keeping two full source-sized GPU buffers.
- Backdrop geometry signatures are cached; identical mask geometry does not rebuild the Win2D path. Opacity updates remain independent; disconnect invalidates geometry state.
- Animation timers stop when values settle; capture jobs are disposed after one frame; device resources are released when idle; window/event hooks and timers are disposed during shutdown.
- Preview cards/windows are rebuilt only when membership/layout changes; existing selection and card state are reused where possible.
- Search result identity and selected item are preserved across progressive index/icon refresh.

The current review records five process samples over approximately 60 seconds: working set 217.62→228.34 MiB, private bytes 149.25→159.89 MiB, handles 1504→1530. The same review explicitly says this does not prove a stable plateau or continuous leak. Supplied reference values of approximately 77 MiB fresh and 94 MiB after heavy interaction are not recorded by the project and must not be treated as code-derived measurements.

Likely remaining hotspots are WGC/D3D device creation, Shell/COM enumeration, repeated native polygon allocation during animated placement, and the large DesktopOverlayWindow class. No formal profiler or long-duration leak test is part of this audit.

## 13. CPU, timers and background work

| Location | Interval/trigger | Purpose | Idle behavior |
|---|---|---|---|
| DockAnimationController.placementTimer | ~16 ms while bottom is moving | Smooth host placement and input polygon updates | Stops at target. |
| DockAnimationController.magnificationTimer | ~16 ms while pointer/magnification is unsettled | Icon scale and wave target | Stops at epsilon/idle. |
| DesktopOverlayWindow.dockWaveTimer | ~16 ms while active | Pointer wave animation/geometry | Stopped outside active dock interaction. |
| WindowsOverlayManager.InputTimer | 50 ms while active/peek/collapse | Reacquire pointer for transparent layered HWND | Not continuously active while hidden/idle. |
| DesktopOverlayWindow.heartbeat | 1 s | Parent/watchdog lease and taskbar status | Runs while app/taskbar session is active. |
| Watchdog child heartbeat | roughly 5 s protocol | Detect parent and recover taskbar | Child runs for the lease lifetime. |
| WindowPreviewWindow animation timer | ~16 ms | Preview panel/card transition | Stops when settled/hidden. |
| Glass Home animation timer | ~16 ms | Compact/expanded Home height | Stops while hidden/settled. |
| Glass Home focus retry | 20 ms, max 12 tries | Focus retained TextBox after Show/Activate | Stops after success/limit. |
| Glass Home polling | 25 ms while visible | Pointer/outside-click/session sampling | Stops when hidden. |
| Preview show/hide delays | 350/400 ms one-shot | Avoid accidental app-button preview flicker | Canceled on pointer changes/hold. |
| Dock collapse/pill delays | 1000/2000 ms one-shot | Grace/collapse/peek timing | Canceled by re-entry/hold. |
| WindowsApplicationService reconciliation | event-driven, coalesced up to ~3 s | Enumerate/reconcile running windows and pins | Worker waits when no refresh. |
| WindowFrameCache capture | on Track/Request, one-shot | Prime/retry a missing visible frame | No periodic capture loop. |
| WindowsApplicationIndex | once per retained Home, STA worker | Discover installed apps/metadata/icons | No full-disk scan or continuous watcher. |

## 14. Taskbar and watchdog architecture

TaskbarDevelopmentSession launches Recovery\GlassDock.Watchdog.exe from AppContext.BaseDirectory. The child uses SHAppBarMessage (ABM_GETSTATE/ABM_SETSTATE) to read/clear auto-hide, finds the taskbar HWND, hides/disables it and writes the original state to a flushed journal under %LOCALAPPDATA%\GlassDock\taskbar-state-{session}.txt. A mutex/event handshake covers READY/HIDE/HIDDEN/PING/RESTORE. The parent heartbeat is one second; the child lease heartbeat is about five seconds.

Normal app close calls TaskbarRecovery.RestoreNow, stops timers, disposes the session and shuts down native services. Initialization exceptions also request restore. If the parent dies, the watchdog restores the saved state; if both are unavailable, GlassDock.Watchdog.exe --restore is the independent recovery path. A later lease first recovers an old journal. No Explorer process is killed or restarted, and no registry/system-shell replacement is present.

The current DesktopOverlayWindow automatically starts --watch-active when the root loads, with no 60-second cap. DevelopmentWindow also exposes a bounded development taskbar test path (the non-active mode has a maximum around 60 seconds). The source's active launch behavior therefore differs from stale docs that describe taskbar suppression as explicit opt-in.

Ctrl+Alt+F12 calls the recovery handler. The taskbar implementation currently assumes one detected Windows taskbar; multi-monitor taskbar recovery is not claimed.

## 15. Persistent settings

The only product-level persistent dock state currently confirmed is the pin/exclusion store:

- %LOCALAPPDATA%\GlassDock\dock-pins.json.
- Atomic temporary-file replacement is used for saves.
- It contains pinned application identities and exclusions; Windows taskbar pins are imported periodically but are not the same as user pins.

Taskbar recovery journals are persistent only for the duration of a lease and are removed after verified restoration. Bottom spacing and auto-hide/peek delays persist through the settings service. Dock size, blur, transparency, glow, lock state and other appearance values do not. There is no startup registration or updater configuration.

## 16. Multi-monitor support

WindowsOverlayManager places the dock on the primary monitor (MonitorFromPoint(0, MONITOR_DEFAULTTOPRIMARY)) and uses GetMonitorInfo/GetDpiForWindow. Glass Home and the preview panel are also positioned against the primary/dock monitor work area. There is no active-monitor dock relocation setting.

Desktop focus mirrors a target window on whichever monitor owns the target. For minimized targets it converts WINDOWPLACEMENT.rcNormalPosition using the target monitor/work rectangle and supports negative virtual desktop coordinates. Logs include monitor/work/virtual origins. Visible target bounds use DWM extended frame bounds when available and GetWindowRect fallback.

The mirror uses physical target bounds and does not currently rescale the cached frame by a source-monitor DPI factor beyond the target rectangle; mixed-DPI behavior is therefore source-code supported at coordinate level but not independently visually certified. Preview UI remains on the dock's monitor. Monitor-specific taskbar suppression is not implemented; the watchdog detects one taskbar.

## 17. Important native Windows APIs

### Window/overlay management

CreateWindowEx, DestroyWindow, SetWindowPos, ShowWindow, IsWindow, IsWindowVisible, IsIconic, GetWindowRect, GetClientRect, GetWindowLongPtr, SetWindowLongPtr, SetWindowSubclass, DefSubclassProc, SetTimer, KillTimer, SetWindowRgn, GetWindowRgn, WM_NCHITTEST, WM_MOUSEACTIVATE, HTTRANSPARENT, MA_NOACTIVATE.

### DWM and previews

DwmRegisterThumbnail, DwmUnregisterThumbnail, DwmQueryThumbnailSourceSize, DwmUpdateThumbnailProperties, DwmGetWindowAttribute/extended frame bounds, DwmSetWindowAttribute, DwmExtendFrameIntoClientArea, DwmFlush.

### Window/application discovery and activation

EnumWindows, WinEvent hooks for foreground/create/show/hide/location, GetWindowThreadProcessId, process metadata, GetWindowPlacement, MonitorFromWindow, GetMonitorInfo, GetDpiForWindow, SetForegroundWindow (only existing activation paths, never desktop focus hover), ShowWindowAsync (SW_RESTORE), PostMessage (WM_CLOSE).

### Input and keyboard

SetWindowsHookExW (WH_KEYBOARD_LL), CallNextHookEx, GetAsyncKeyState, RegisterHotKey, UnregisterHotKey, SendInput, PostMessage for tagged UI-thread requests. Secure desktop input is not intercepted.

### Shell/taskbar/recovery

SHAppBarMessage (ABM_GETSTATE/ABM_SETSTATE), taskbar window lookup, EnableWindow, ShowWindow, named mutex/event and process identity checks.

### Capture and graphics

Windows Graphics Capture (GraphicsCaptureSession, Direct3D11CaptureFramePool, GraphicsCaptureItem), D3D11 device creation, Win2D/Windows Composition effect objects, GDI StretchDIBits/BitBlt for cached mirror painting.

## 18. Error handling and recovery

- ActionFailed events surface launch, activation, close, context-menu and Shell errors to the UI status path.
- WindowsApplicationService validates HWND/process identity and rechecks eligibility before activation/close. Missing executables return false rather than crashing. Run-as failures, including UAC cancellation, are reported.
- Preview registration/query failures leave a text fallback card; DWM thumbnails are disposed/replaced without activating the source. Cached frame drawing records success/failure and falls back to the DWM/text path if unavailable.
- WGC rejects unsupported capture, all-black frames and device/frame failures; it clears queued flags, disposes the one-shot job and applies retry cooldown. Diagnostics are JSONL at %LOCALAPPDATA%\GlassDock\preview-diagnostics.jsonl, capped/rotated at about 4 MB.
- Invalid/minimized window handles are skipped by eligibility checks. Target HWND state is not changed during hover.
- Taskbar startup wraps exceptions and immediately calls TaskbarRecovery.RestoreNow. Normal close, child loss, heartbeat loss and independent recovery all restore the journaled state.
- Most tracing uses Debug.WriteLine/diagnostic JSONL. Release builds still retain bounded diagnostic events; no telemetry/network sink exists.

## 19. Test suite

### Current fresh run

Commands executed read-only:

~~~text
dotnet build GlassDock.sln -c Release --no-restore
dotnet test tests/GlassDock.Core.Tests/GlassDock.Core.Tests.csproj -c Release --no-build --no-restore --logger "console;verbosity=minimal"
dotnet test tests/GlassDock.Windows.Tests/GlassDock.Windows.Tests.csproj -c Release --no-build --no-restore --logger "console;verbosity=minimal"
~~~

Results:

| Suite | Discovered | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| Core | 85 | 84 | 1 | 0 |
| Windows | 33 | 32 | 1 | 0 |
| **Total** | **118** | **116** | **2** | **0** |

Failures:

1. GlassDock.Core.Tests.WindowsKeyGestureTests.Existing_modifier_and_two_windows_keys_are_not_bare (line 49). The current recognizer treats the tested dual-Windows-key release as bare; this is unrelated to Glass Home/search, capture or dock rendering, but it is a real current test failure.
2. GlassDock.Windows.Tests.DesktopWindowHighlightTests.Overlay_is_hollow_nonactivating_and_leaves_targets_unchanged (lines 267/306). Reflection dereferences an overlay field that no longer exists because DesktopWindowHighlight is intentionally a no-op. The test is stale relative to the current implementation.

scripts/Validate.ps1 performs locked restore/build and then the suites, stopping at the first failing test. Its historical documentation reports smaller totals; the fresh run above is authoritative for this checkout.

Test areas covered include Core dock state/gesture/layout/search/Home session/application records, Windows input polygon/transparent hit-testing, taskbar/watchdog contracts, capture/cache diagnostics and minimized-frame behavior, window preview layout/session, icon pixels, native process/launcher/application-service contracts. Automated tests cannot certify live elevated UI visual output.

## 20. Known bugs and limitations

### Confirmed current bugs or code/document drift

- Two tests fail as listed above.
- DesktopWindowHighlight is a no-op, so a white desktop-window highlight is not implemented; the stale reflection test still expects an overlay field.
- The preview close button is currently positioned using Canvas.Left=rect.X+9/Top=rect.Y+8 (upper-left overlay), despite the requested top-right visual design.
- Corrected audit finding: no duplicated refresh/enqueue block was found in OnInteractionHoldChanged during Step 1.
- DesktopOverlayWindow automatically starts the active taskbar watchdog lease, while README/ARCHITECTURE/ROADMAP sections still describe taskbar suppression as opt-in and bare-Win interception as deferred.
- .gitignore ends with literal @" / "@ | Add-Content .gitignore lines, which are malformed here-string command text rather than meaningful ignore comments.

### Known limitations

- Yellow WGC capture indicators can still flash on initial priming or explicit retries; unpackaged capability means perfect borderless capture is unsupported.
- Taskbar suppression detects one taskbar and is not claimed as multi-monitor shell support.
- Dock, Glass Home and preview UI are anchored to the primary/dock monitor; only the desktop-focus mirror follows a target on another monitor.
- Mixed-DPI mirror coordinates are handled, but source-monitor visual scaling is not separately certified.
- App requires administrator elevation; elevated ownership prevents the current UI automation helper from binding reliably.
- WGC cached content can be stale at minimization, and a window minimized before any valid frame may have no cached image.
- No long-duration profiler proves a stable working-set plateau; historical samples show growth that is not classified as a leak.
- The installer script uses absolute local paths and the checked-in installer/ZIP/publish outputs are ignored, stale distribution artifacts.

### Unverified behavior

- Live visual dock hover/click-through, outer shadow/glow and minimized preview on this exact running build were not certified by automation during this audit. The app was running, but the helper could not bind to the elevated window.
- Physical bare-Win/Win+Space delivery, Explorer taskbar recovery after every failure mode and mixed-DPI multi-monitor visual quality still need manual testing.

### Future ideas explicitly not implemented

Full launcher categories, recent files, web search, startup registration, licensing/payment, networking, telemetry, updater, shell replacement, multi-monitor dock relocation and packaged borderless WGC capability are outside the current source.

## 21. Build and publish

The solution and project paths are:

~~~powershell
dotnet restore GlassDock.sln --locked-mode
dotnet build GlassDock.sln -c Debug --no-restore
dotnet build GlassDock.sln -c Release --no-restore
dotnet publish src/GlassDock.App/GlassDock.App.csproj -c Release -r win-x64 --self-contained true
~~~

Directory.Build.props enables nullable/implicit usings, deterministic output and TreatWarningsAsErrors=true. The app project is the actual runnable WinUI executable: src/GlassDock.App/GlassDock.App.csproj, OutputType=WinExe, UseWinUI=true, WindowsPackageType=None, app-local Windows App SDK and RuntimeIdentifier(s)=win-x64. Publish targets copy the watchdog executable to Recovery\GlassDock.Watchdog.exe and copy the generated .pri file.

Expected publish layout is a self-contained GlassDock.App.exe plus Windows App SDK/runtime DLLs, resources and a Recovery subdirectory containing GlassDock.Watchdog.exe. Do not infer a successful publish from the ignored existing publish\GlassDock folder without rebuilding it.

## 22. Installer and distribution

The Inno Setup script is scripts/GlassDock.iss:

| Field | Current value |
|---|---|
| MyAppName | Doki |
| MyAppVersion | 1.0 |
| Publisher | Natesystemp |
| URL | https://www.dokidock.com |
| Install directory | {autopf}\GlassDock |
| Architecture | x64-compatible, 64-bit install mode |
| Minimum Windows | 10.0.22000 |
| Source files | C:\Dev\GlassDock\publish\GlassDock\* recursively |
| Output directory | C:\Dev\GlassDock\Installer |
| Output filename | GlassDock-Setup-x64.exe |
| Shortcuts | Start Menu; optional desktop shortcut |
| Post-install | Launches GlassDock.App.exe after install (runascurrentuser) |

The [Files] entry includes the whole publish folder recursively, not just the main EXE; that includes Recovery when it is present. The absolute source path makes the script machine-specific. The existing root installer\GlassDock-Setup-x64.exe (~94 MB) and GlassDock-Windows-x64.zip (~135 MB) are ignored/stale artifacts, not reproducible release evidence. installer\README is a placeholder. No release signing or GitHub artifact workflow is present.

## 23. GitHub/release workflow and ignore policy

.github/workflows/build.yml runs on Windows latest for pushes/PRs, installs the SDK from global.json, and runs scripts/Validate.ps1 with read-only contents permission. It does not publish, build an installer, sign binaries or create a GitHub Release.

.gitignore intentionally ignores bin/, obj/, .vs/, .vscode/, TestResults/, artifacts/, publish/installer output, the portable ZIP, user settings, logs and credential-like files. No tracked publish, Installer, TestExtract, ZIP or generated installer files were found. The final two literal here-string lines in .gitignore are malformed and should be treated as a documentation/configuration defect, but were not changed in this audit.

For a release, build a clean win-x64 publish, include the entire publish tree in the installer/ZIP, validate Recovery\GlassDock.Watchdog.exe, and attach artifacts through GitHub Releases rather than committing generated binaries to the source branch. That release process does not currently exist in CI.

## 24. File-by-file architecture map

| File | Project | Major classes | Purpose/dependencies/key behavior |
|---|---|---|---|
| src/GlassDock.App/Desktop/DesktopOverlayWindow.cs | App | DesktopOverlayWindow | Main dock window, state/timers, material, menu, taskbar lease, app buttons, keyboard callbacks; depends on Core state, Windows services, backdrop/animation/preview. |
| src/GlassDock.App/Desktop/DockAnimationController.cs | App | DockAnimationController | Storyboards, bottom placement, icon magnification, context freeze/resume. |
| src/GlassDock.Windows/Desktop/WindowsOverlayManager.cs | Windows | WindowsOverlayManager | Native HWND styles/placement, input polygon, hit-testing, DPI, transparency and DWM setup. |
| src/GlassDock.Windows/Interop/NativeMethods.cs | Windows | NativeMethods | User32/DWM/GDI/Shell declarations and structs. |
| src/GlassDock.App/Rendering/DesktopGlassBackdrop.cs | App | DesktopGlassBackdrop | Composition visual surface, masked backdrop brush, wave geometry and fallback material. |
| src/GlassDock.App/Controls/GlassSurface.xaml.cs | App | GlassSurface | XAML/Composition glass shape, rim, lighting and shadow; paired with GlassSurface.xaml. |
| src/GlassDock.App/Rendering/GlassEffectGraph.cs | App | GlassEffectGraph | Win2D blur/saturation/exposure/tint graph. |
| src/GlassDock.Core/Materials/GlassMaterial.cs | Core | GlassMaterial, GlassMaterialPreset | Material presets/scalars; paired with App rendering and refraction support. |
| src/GlassDock.App/Rendering/RefractionLayer.cs | App | RefractionLayer | Optional refraction surface; currently unsupported/disabled. |
| src/GlassDock.App/Desktop/GlassHomeWindow.cs | App | GlassHomeWindow | Retained Home HWND, compact/expanded layout, search editor, focus retry, launch/hide. |
| src/GlassDock.App/ViewModels/DockApplicationsViewModel.cs | App | DockApplicationsViewModel | Observable dock app/window state and synchronization. |
| src/GlassDock.Windows/Applications/WindowsApplicationService.cs | Windows | WindowsApplicationService | WinEvent hooks, enumeration/reconciliation, pins, icons, activate/close/launch. |
| src/GlassDock.Windows/Applications/WindowsApplicationIndex.cs | Windows | WindowsApplicationIndex | Lazy installed-app metadata worker. |
| src/GlassDock.Windows/Applications/WindowsApplicationIconService.cs | Windows | icon service | Shell/executable/HICON extraction and bounded cache. |
| src/GlassDock.Windows/Applications/WindowsApplicationLauncher.cs | Windows | launcher | ShellExecute/ShellExecuteEx, run-as and file-location operations. |
| src/GlassDock.Core/Applications/GlassSearch.cs | Core | GlassSearch | Ranking/dedup/result cap. |
| src/GlassDock.App/Desktop/WindowPreviewCoordinator.cs | App | WindowPreviewCoordinator | Bridges dock app buttons, preview sessions/windows, frame cache, context menus and activation. |
| src/GlassDock.App/Desktop/WindowPreviewWindow.cs | App | WindowPreviewWindow | Cards, thumbnail/fallback rendering, preview transition, focus selection, close buttons and pages. |
| src/GlassDock.Core/Applications/WindowPreviewSession.cs | Core | WindowPreviewSession | Preview state/order/selection/revision/activation. |
| src/GlassDock.Core/Applications/WindowPreviewLayout.cs | Core | WindowPreviewLayout | Capacity, card rectangles, compact/expanded dimensions and paging. |
| src/GlassDock.Windows/Desktop/DesktopWindowFocus.cs | Windows | DesktopWindowFocus | Dim/mirror layered windows, DWM/cached-frame paths, restored-bounds coordinate conversion. |
| src/GlassDock.Windows/Applications/WindowFrameCache.cs | Windows | WindowFrameCache | One-shot WGC capture, frame budget/eviction, diagnostics and minimized fallback. |
| src/GlassDock.Windows/Applications/WindowThumbnail.cs | Windows | WindowThumbnail | DWM thumbnail registration, source size and property updates. |
| src/GlassDock.Windows/Desktop/WindowsKeyboardService.cs | Windows | WindowsKeyboardService | Low-level keyboard hook, Win gestures, registered hotkeys, recovery. |
| src/GlassDock.Windows/Desktop/Taskbar* | Windows | taskbar/recovery classes | Lease, journal, suppression, restoration and watchdog protocol. |
| src/GlassDock.Watchdog/Program.cs | Watchdog | Program | --watch, --watch-active, --status, --restore child process. |
| tests/GlassDock.Core.Tests/DesktopTests.cs | Core tests | xUnit fixtures | State, gesture, layout, Home/search and application contracts. |
| tests/GlassDock.Windows.Tests/DockInteractionRegionTests.cs | Windows tests | xUnit fixtures | Native polygon, hit-testing, transparency and no-clipping contracts. |
| scripts/Validate.ps1 | Scripts | validation script | Locked restore/build then test suites. |
| scripts/GlassDock.iss | Scripts | Inno Setup script | Whole publish-folder installer. |

## 25. Major class relationship map

~~~text
App.xaml.cs
 └─ DesktopOverlayWindow
     ├─ DockApplicationsViewModel
     │   └─ WindowsApplicationService
     │       ├─ WindowsApplicationLauncher
     │       ├─ WindowsApplicationIconService
     │       └─ DockPinStore / Shell + WinEvent interop
     ├─ DockStateMachine (Core)
     ├─ DockAnimationController
     ├─ DesktopGlassBackdrop
     │   └─ GlassEffectGraph / GlassMaterial / GlassSurface
     ├─ WindowsOverlayManager
     ├─ WindowsKeyboardService
     ├─ TaskbarDevelopmentSession / TaskbarRecovery
     └─ WindowPreviewCoordinator
         ├─ WindowPreviewSession (Core)
         ├─ WindowPreviewWindow
         │   ├─ WindowPreviewLayout (Core)
         │   ├─ WindowThumbnail
         │   └─ DesktopWindowFocus
         └─ WindowFrameCache

 GlassHomeWindow
   ├─ GlassHomeSession / GlassHomeInput
   ├─ WindowsApplicationIndex
   ├─ GlassSearch (Core)
   ├─ WindowsSettingsCatalog
   └─ WindowsApplicationLauncher / WindowsApplicationIconService

 GlassDock.Watchdog.exe
   └─ TaskbarRecovery / Windows taskbar native APIs
~~~

## 26. Event and data flows

### A. Bare Win

WindowsKeyboardService low-level callback recognizes a bare Win release, injects tagged cleanup input, posts a revision-guarded HomeRequested, and DesktopOverlayWindow.ToggleDockAsync expands or collapses through the normal state/animation path. While a context menu is held, the request is suppressed.

### B. Win+Space

The keyboard gesture consumes Space, posts LauncherRequested, and DesktopOverlayWindow.ShowHome calls GlassHomeWindow.Toggle. Home shows/activates the retained HWND, focuses the search TextBox through dispatcher/retry, and starts visible polling.

### C. Hover dock icon

Dock app button PointerEntered enters the coordinator's delayed preview path (about 350 ms). The coordinator begins/updates a WindowPreviewSession, requests missing visible frames, then shows WindowPreviewWindow compact and positions it.

### D. Click dock icon

One-window apps use WindowChosen/ActivateWindow and restore/foreground the target. Multiple-window apps expand the preview panel; a card click activates only after the normal selection path.

### E. Right-click dock icon

Coordinator cancels preview delays, snapshots identity, hides preview, holds dock interaction and opens the app menu. Commands route to service launch/close/pin/admin/location methods.

### F. Close context menu

Menu/submenu Closed callbacks remove identity snapshots, defer EndContextMenu, release the hold if no nested menu remains, and resume/collapse according to current pointer state.

### G. Move mouse away

Native pointer testing distinguishes real departure from transparent-window routing. Expanded states schedule one-second collapse; idle/hovering schedules two-second pill hide. Re-entry cancels the corresponding token.

### H. Expanded dock collapses

CollapseDockAsync hides previews, resets wave/magnification, runs reverse storyboard, moves to the bottom margin, updates input, then schedules the two-second pill hold.

### I. Pill enters peek

After the two-second delay, SchedulePillHide verifies no pointer/hold, transitions to Idle, animates bottom to PeekRestBottom=-2 over about 210 ms and leaves the indicator visible.

### J. Hover peek pill

The peek input polygon and native pointer timer detect entry, cancel pill hiding, raise the indicator and request dock hover/expansion without changing the actual visual dock design.

### K. Click peek pill

The idle/peek tap calls ToggleDockAsync, cancels pending delays, requests expansion and lets the normal storyboard reveal the dock/icons.

### L. Hover an app with multiple windows

The coordinator begins a session ordered by active/recent time, displays compact cards, expands to the calculated capacity, pages remaining windows and enables focus selection only after expansion completion. Other cards dim when a selection is active.

### M. Preview a minimized window

WindowPreviewWindow selects only after CanFocus. DesktopWindowFocus keeps the source iconic, computes saved/restored bounds on the source monitor, hides the DWM thumbnail if a cached frame exists, and paints the retained frame into the mirror overlay. If no frame exists, the DWM/text fallback is used.

### N. Click preview

WindowPreviewSession.Activate returns the selected ApplicationWindow; coordinator calls the existing ShowWindowAsync(SW_RESTORE) + SetForegroundWindow activation path, hides focus/preview and refreshes application state.

### O. Exit GlassDock

OnClosed disposes previews/application service, cancels timers, restores taskbar synchronously if a lease/start was present, stops keyboard/animation/manager and disposes recovery/session resources. Watchdog remains an independent recovery process if the parent failed before normal close.

## 27. Current user experience summary

A user launches the elevated GlassDock executable and sees a small bottom-center indicator. Moving to or clicking the bottom center reveals a glass dock whose icons magnify around the pointer. Moving away collapses it, holds a small pill for two seconds, then lowers the pill near the edge. Clicking an app opens/activates it; apps with multiple windows show animated preview cards. Hovering an expanded preview emphasizes the corresponding desktop representation without activating the real window; clicking restores/activates it. Each card can request a normal WM_CLOSE with its small X control.

Right-clicking an app offers Open/Activate, New Window when available, Show All Windows, individual windows, Close, Pin/Unpin, Run as Administrator and Open File Location. Right-clicking the dock itself offers Dock Settings, Glass Home, taskbar restore, the material laboratory and Exit. The current source has no add-app or persistent dock-appearance editor.

Win+Space opens Glass Home compact with the search field focused. Typing searches installed applications and a small Settings catalog; up to eight results are shown and Enter launches the selected result. Pointer movement expands Home; Escape/outside click hides it. Ctrl+Alt+F12 provides emergency taskbar restore. The watchdog is intended to restore the taskbar after normal exit or process failure, but physical shell behavior still needs manual verification on this build.

## 28. Release readiness

### Blocker before RC1

- Resolve the two failing tests or explicitly replace their stale contracts with current behavior.
- Decide and document whether automatic active taskbar suppression is truly intended; current source and product/architecture docs disagree, and shell safety is not yet production-certified.
- Complete manual visual verification of the elevated app: dock shadow/click-through, taskbar edge behavior, Win/Win+Space delivery, minimized multi-monitor previews and capture indicators.

### Should fix before RC1

- Move the preview close X to the intended top-right position and add/repair a matching test.
- Preserve the existing single guarded OnInteractionHoldChanged refresh block.
- Reconcile README/ARCHITECTURE/ROADMAP/taskbar docs with current keyboard/taskbar source.
- Repair .gitignore's malformed trailing lines.
- Replace absolute installer paths and establish a reproducible publish/installer workflow.
- Decide how the missing desktop-window white highlight is handled, since the current class is an intentional no-op.

### Safe to defer after v1.0

- Full launcher categories/recent files/web search/power/Task View.
- Packaged/MSIX borderless WGC capability.
- Multi-monitor dock relocation and multi-taskbar management.
- Persistent appearance/size/peek settings, startup registration, updater, telemetry, licensing/payment/network features.

### Unverified / needs manual testing

- Exact interactive Win+Space typing and repeated focus after hide/reopen under the elevated manifest.
- Physical bare-Win toggle while preserving every Win+ shortcut.
- Live WGC yellow-border timing and stale-frame behavior on Edge/OBS-like GPU applications.
- Long-duration memory/handle plateau and repeated preview/menu interaction.
- Target-monitor mixed-DPI visual placement and monitor-left/above-primary cases.

## 29. Current GlassDock Snapshot

**Product:** GlassDock floating Windows desktop dock  
**Version:** No product version in source; app assembly metadata is 0.1.0.0; installer script says 1.0; audit HEAD b373d22 with a dirty working tree  
**Framework:** .NET 10, WinUI 3 / Windows App SDK 2.4.0, Win2D 1.4.0  
**OS:** Windows target 10.0.26100.0, minimum app platform 10.0.19041.0  
**Architecture:** win-x64, x64  
**Packaging:** Unpackaged, app-local/self-contained Windows App SDK; manifest requireAdministrator  
**Installer:** scripts/GlassDock.iss; intended output GlassDock-Setup-x64.exe; current ignored artifact is stale/local  
**Main executable:** GlassDock.App.exe from src/GlassDock.App  
**Watchdog:** Recovery\GlassDock.Watchdog.exe in publish output; source project src/GlassDock.Watchdog  
**Idle/observed RAM:** No current code-derived idle benchmark; historical review samples were 217.62→228.34 MiB over ~60 s. User-supplied ~77/~94 MiB values are reference only.  
**Main shortcuts:** bare Win toggles dock; Win+Space opens Glass Home; Ctrl+Alt+Space registered dock hotkey; Ctrl+Alt+F12 restores taskbar; ordinary Win+R/E/L/D/Tab are intended to pass through  
**Dock states:** Hidden, Idle, Hovering, Expanding, Expanded, Collapsing, plus raised/lowered pill timing  
**Preview system:** DWM live thumbnails for visible windows; one-shot bounded WGC last-good-frame cache for minimized windows; no target activation during hover  
**Search system:** Lazy Shell/Start-menu app index plus nine Windows Settings entries; ranked/deduplicated, max eight results, progressive icons  
**Context menus:** App menu as documented above; dock menu has settings/Home/taskbar restore/lab/exit only  
**Known limitation:** Two failing tests, unpackaged WGC yellow flashes, primary-monitor dock/Home UI, manual elevated UI verification incomplete, source/docs drift  
**Build status:** Release solution build PASS, 0 warnings, 0 errors (dotnet build GlassDock.sln -c Release --no-restore)  
**Test status:** 118 discovered, 116 passed, 2 failed, 0 skipped (one Core Windows-key gesture failure; one stale Windows highlight reflection failure)  
**Release phase:** Phase 2–3 floating desktop dock foundation with bounded taskbar/recovery work; stop before full launcher/future phases

### Audit evidence and repository state

At the time of inspection, GlassDock.App process ID 14012 (GlassDock — Floating Dock) was running and responding. A read-only watchdog status query reported Available=true, Visible=false, Count=1, Enabled=false, AutoHide=false; this indicates an active taskbar suppression lease during the audit. No process was started, stopped, killed or modified by this audit. Existing dirty files were:

~~~text
 M docs/DOCK_INPUT_AND_CAPTURE_REVIEW.md
 M src/GlassDock.App/Desktop/DesktopOverlayWindow.cs
 M src/GlassDock.App/Desktop/DevelopmentWindow.cs
 M src/GlassDock.App/Desktop/DockAnimationController.cs
 M src/GlassDock.App/Desktop/WindowPreviewCoordinator.cs
 M src/GlassDock.App/ViewModels/DockApplicationsViewModel.cs
 M src/GlassDock.Core/Applications/DockApplication.cs
 M src/GlassDock.Core/Desktop/DockStateMachine.cs
 M src/GlassDock.Windows/Applications/WindowsApplicationLauncher.cs
 M src/GlassDock.Windows/Applications/WindowsApplicationService.cs
 M src/GlassDock.Windows/Desktop/WindowsKeyboardService.cs
 M src/GlassDock.Windows/Desktop/WindowsOverlayManager.cs
 M src/GlassDock.Windows/Interop/NativeMethods.cs
 M tests/GlassDock.Core.Tests/DesktopTests.cs
 M tests/GlassDock.Windows.Tests/DockInteractionRegionTests.cs
?? docs/DOCK_INTERACTION_REVIEW.md
~~~

The only new file from this audit is GLASSDOCK_FULL_CONTEXT.md. No source code, tests, existing documentation, installer scripts, project files, Git history or runtime state were changed.

## Appendix A. Repository structure observed

The following tree is the complete non-generated file inventory returned by the repository audit. bin and obj outputs are intentionally excluded; ignored publish/installer artifacts are described in section 22.

~~~text
AGENTS.md
ARCHITECTURE.md
.editorconfig
.gitattributes
.gitignore
artifacts-brush-backup.tmp
Directory.Build.props
GlassDock.sln
global.json
NuGet.Config
PRODUCT_SPEC.md
README.md
ROADMAP.md
GLASSDOCK_FULL_CONTEXT.md
.github/
  workflows/
    build.yml
docs/
  DESKTOP_FOUNDATION_CHECKPOINT.md
  DESKTOP_RECOVERY_DESIGN.md
  DOCK_INPUT_AND_CAPTURE_REVIEW.md
  DOCK_INTERACTION_REVIEW.md
  GLASS_HOME_SEARCH.md
  PHASE_0_VALIDATION.md
  PHASE_1_FINDINGS.md
  PHASE_2_3_VALIDATION.md
  TASKBAR_AND_WINDOWS_KEY_FIX.md
  TOOLCHAIN.md
scripts/
  GlassDock.iss
  Test-SearchIndex.ps1
  Validate.ps1
src/
  GlassDock.App/
    app.manifest
    App.xaml
    App.xaml.cs
    GlassDock.App.csproj
    packages.lock.json
    Controls/
      AdaptiveAppIcon.cs
      GlassSurface.xaml
      GlassSurface.xaml.cs
    Desktop/
      DesktopOverlayWindow.cs
      DevelopmentWindow.cs
      DockAnimationController.cs
      GlassHomeWindow.cs
      WindowPreviewCoordinator.cs
      WindowPreviewWindow.cs
    Rendering/
      DesktopGlassBackdrop.cs
      GlassCompositionBrush.cs
      GlassEffectGraph.cs
      IconRasterizer.cs
      RefractionLayer.cs
    ViewModels/
      DockApplicationsViewModel.cs
      GlassLabViewModel.cs
    Views/
      GlassLabView.xaml
      GlassLabView.xaml.cs
  GlassDock.Core/
    GlassDock.Core.csproj
    packages.lock.json
    README.md
    Applications/
      DockApplication.cs
      DockApplicationCollection.cs
      DockApplicationItem.cs
      GlassSearch.cs
      WindowPreviewLayout.cs
      WindowPreviewSession.cs
    Desktop/
      DesktopPlacement.cs
      DockStateMachine.cs
      GlassHomeSession.cs
      IKeyboardService.cs
      ITaskbarController.cs
      TaskbarLease.cs
      WindowsKeyGesture.cs
    Materials/
      GlassMaterial.cs
      GlassMaterialPreset.cs
  GlassDock.Licensing/
    GlassDock.Licensing.csproj
    packages.lock.json
    README.md
  GlassDock.Watchdog/
    GlassDock.Watchdog.csproj
    packages.lock.json
    Program.cs
    README.md
  GlassDock.Windows/
    GlassDock.Windows.csproj
    packages.lock.json
    README.md
    Applications/
      ApplicationNative.cs
      DockPinStore.cs
      PreviewDiagnostics.cs
      ShellApplicationMetadata.cs
      WindowFrameCache.cs
      WindowsApplicationIconService.cs
      WindowsApplicationIndex.cs
      WindowsApplicationLauncher.cs
      WindowsApplicationService.cs
      WindowsSettingsCatalog.cs
      WindowThumbnail.cs
    Desktop/
      DesktopWindowFocus.cs
      DesktopWindowHighlight.cs
      GlassHomeInput.cs
      InteractiveGlassWindowHost.cs
      TaskbarAutoHide.cs
      TaskbarDevelopmentSession.cs
      TaskbarRecovery.cs
      WindowPreviewPlacement.cs
      WindowsCompositionSupport.cs
      WindowsKeyboardService.cs
      WindowsOverlayManager.cs
      WindowsTaskbarController.cs
    Interop/
      NativeMethods.cs
tests/
  GlassDock.Core.Tests/
    ArchitectureTests.cs
    DesktopTests.cs
    DockApplicationTests.cs
    GlassDock.Core.Tests.csproj
    GlassDock.lnk
    GlassHomeSessionTests.cs
    GlassMaterialTests.cs
    GlassSearchTests.cs
    packages.lock.json
    TaskbarLeaseTests.cs
    WindowPreviewTests.cs
    WindowsKeyGestureTests.cs
  GlassDock.Windows.Tests/
    ArchitectureTests.cs
    DesktopWindowHighlightTests.cs
    DockInteractionRegionTests.cs
    GlassDock.Windows.Tests.csproj
    IconAlphaTests.cs
    IconServiceTests.cs
    packages.lock.json
    WindowsSettingsCatalogTests.cs
website/
  README.md
~~~
