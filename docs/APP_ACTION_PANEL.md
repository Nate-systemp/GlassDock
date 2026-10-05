# Doky App Action Panel

## Architecture and scope

The existing `DockAppContextMenuWindow` remains the only app context panel, retained once per
`WindowPreviewCoordinator`/monitor surface. `AppActionPanelModel` describes header and contextual
state independently of WinUI. The coordinator binds commands to existing application services;
DesktopOverlayWindow only supplies the existing file launcher and the stack extraction callback.

The normal dock layout, icon sizes, spacing, material, badges, hover, and drag routing are unchanged.
The pre-existing local changes in AdaptiveAppIcon.cs are not part of this task.

## Data and actions

- **Open Windows:** the existing cached `DockApplication.Windows` snapshot. The current monitor
  filter intentionally limits these windows to the invoking surface. No new window enumerator.
  Four direct rows maximum; additional windows are available in a More windows view.
- Exact-window activation uses the existing activation service and elevated restore fallback.
- Open/New window uses existing shell launch semantics, with no invented app-specific arguments.
- Open file uses the Windows App SDK file picker owned by the invoking dock's WindowId. Only
  targets supported by the existing Open-With pipeline offer it. Selection invokes the same
  `WindowsApplicationLauncher.OpenWith` implementation as file-drop, on an STA worker. The UI
  prevents concurrent picker operations and checks disposal after selection. Missing files are
  rejected by the existing preparation path; paths, Unicode, shortcut arguments, and working
  directories retain their current behavior.
- Pin/unpin uses the existing shared service and pin store. Graceful close uses existing WM_CLOSE
  behavior. Show All Windows, Run as Administrator, and Open File Location are retained.
- Stack member right-click uses this same panel. **Move out of stack** calls the existing extraction
  operation; it does not create a duplicate pin. The stack itself retains Open/Rename/Ungroup.
- **Recent is unavailable and omitted.** The repository has no trustworthy per-app document
  history. No fake rows, app-name/extension guesses, shell scraping, or undocumented destination
  parsing were added. Accordingly, recent-item runtime checks are not applicable in this version.

## Presentation and lifecycle

The original dock glass/backdrop/control palette and live appearance propagation are reused.
Header shows the real app icon/name plus Pinned or Running/window count. Width remains 240 DIP
plus the existing gutter; height is capped at 490 DIP and further clamped to monitor work area.
Placement occurs before Activate; stack member anchors are converted from the popup to the
owning dock's coordinates. Keyboard navigation and Escape/outside-click dismissal are retained.
Opening is 150 ms, closing 110 ms, smoothstep easing, 0.97–1 scale and a 4-DIP settle. Reduced
motion skips animation. Glass and content use identical composition frames.

The coordinator suppresses hover previews and holds the dock while the panel or file picker is
open. Snapshot membership changes invalidate stale panel commands. No new background service,
polling loop, provider, profile, or file-drop route was introduced.

## Validation

- Debug build: zero warnings, zero errors.
- Debug tests: **341 total / 341 passed / 0 failed / 0 skipped** (197 Core + 144 Windows).
- `scripts/Validate.ps1`: locked restore, Release build (zero warnings/errors), and the same
  341 tests passed with zero failures/skips.
- Existing context-menu, file-drop, stack, order, notification, monitor, and preview tests retained.
- Runtime UI checks pending. The Windows computer-use helper failed to initialize in this run
  (`failed to write kernel assets`, OS error 3); manual confirmation will be required if it cannot recover.
- Reset/retry produced the same helper error. Debug Doky, watchdog, and input helper are running;
  no development-error.log was produced.
- User confirmed the compact panel for closed/running apps, exact-window row activation,
  Open file with a compatible app, and the shared stack-member panel with Move out of stack.
- User reported the remaining checks working except outside-click dismissal for root apps.
  Follow-up requests foreground ownership for this explicit user-opened panel and listens for
  handled pointer presses on the non-activating dock, with handler removal on disposal. This
  preserves normal application deactivation dismissal without global hooks or polling.
  User retested and confirmed dismissal on another window, the desktop, and empty dock space;
  exact-window activation still worked afterward. The correction also passed both Debug and
  Release builds and all 341 tests in each configuration.

## Files changed by this task

```text
C:\Dev\GlassDock\src\GlassDock.Core\Applications\AppActionPanelModel.cs
C:\Dev\GlassDock\src\GlassDock.App\Desktop\DockAppContextMenuWindow.cs
C:\Dev\GlassDock\src\GlassDock.App\Desktop\WindowPreviewCoordinator.cs
C:\Dev\GlassDock\src\GlassDock.App\Desktop\DesktopOverlayWindow.cs
C:\Dev\GlassDock\src\GlassDock.App\Desktop\DesktopOverlayWindow.Stacks.cs
C:\Dev\GlassDock\src\GlassDock.App\Desktop\DockStackWindow.cs
C:\Dev\GlassDock\src\GlassDock.Windows\Applications\WindowsApplicationLauncher.cs
C:\Dev\GlassDock\src\GlassDock.Windows\Desktop\InteractiveGlassWindowHost.cs
C:\Dev\GlassDock\tests\GlassDock.Core.Tests\AppActionPanelTests.cs
C:\Dev\GlassDock\tests\GlassDock.Windows.Tests\AppActionPanelWiringTests.cs
C:\Dev\GlassDock\docs\APP_ACTION_PANEL.md
```

No commit, push, tag, release, version change, or user-data reset.

Picker API reference: https://learn.microsoft.com/windows/apps/develop/files/using-file-folder-pickers
